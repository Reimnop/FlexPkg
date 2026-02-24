using System.Collections.Concurrent;
using System.Text;
using Discord;
using Discord.Webhook;
using Discord.WebSocket;
using FlexPkg.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordUserInterface : IUserInterface, IAsyncDisposable
{
    private delegate Task ButtonExecutedCallback(SocketMessageComponent messageComponent);

    public int NetworkLatency => client.Latency;
    
    private readonly DiscordSocketClient client = new();
    
    private readonly string token;
    private readonly ulong guildId;
    private readonly ulong channelId;
    private readonly string? webhookUrl;
    private readonly string? webhookName;
    private readonly string? webhookAvatarUrl;
    private readonly string? webhookPackageIconUrl;
    private readonly AppOptions.PackageOptions packageOptions;
    private readonly ILogger<DiscordUserInterface> logger;

    private readonly ConcurrentDictionary<string, ButtonExecutedCallback> buttonExecutedCallbacks = [];
    private readonly ConcurrentDictionary<string, SocketModal> submittedModals = [];
    private readonly ConcurrentDictionary<string, UiCommandExecuteCallback?> commandExecuteCallbacks = [];
    private readonly ConcurrentDictionary<ulong, DiscordPaginatedMessage> paginatedMessages = [];

    private readonly CancellationTokenSource paginationTimeoutTaskCts = new();

    public DiscordUserInterface(IOptions<AppOptions> options, ILogger<DiscordUserInterface> logger)
    {
        var discordOptions = options.Value.Discord;
        
        token = discordOptions.Token;
        guildId = discordOptions.GuildId;
        channelId = discordOptions.ChannelId;
        webhookUrl = discordOptions.WebhookUrl;
        webhookName = discordOptions.WebhookName;
        webhookAvatarUrl = discordOptions.WebhookAvatarUrl;
        webhookPackageIconUrl = discordOptions.WebhookPackageIconUrl;
        packageOptions = options.Value.Package;
        this.logger = logger;

        client.Log += ClientOnLogAsync;
        client.ButtonExecuted += ClientOnButtonExecutedAsync;
        client.ModalSubmitted += ClientOnModalSubmittedAsync;
        client.SlashCommandExecuted += ClientOnSlashCommandExecutedAsync;
    }

    private Task ClientOnLogAsync(LogMessage arg)
    {
        switch (arg.Severity)
        {
            case LogSeverity.Critical:
                logger.LogCritical(arg.Exception, "{}", arg.Message);
                break;
            case LogSeverity.Error:
                logger.LogError(arg.Exception, "{}", arg.Message);
                break;
            case LogSeverity.Warning:
                logger.LogWarning(arg.Exception, "{}", arg.Message);
                break;
            case LogSeverity.Info:
                logger.LogInformation(arg.Exception, "{}", arg.Message);
                break;
            case LogSeverity.Verbose:
                logger.LogTrace(arg.Exception, "{}", arg.Message);
                break;
            case LogSeverity.Debug:
                logger.LogDebug(arg.Exception, "{}", arg.Message);
                break;
            default:
                logger.LogTrace(arg.Exception, "{}", arg.Message);
                break;
        }
        return Task.CompletedTask;
    }

    private async Task ClientOnButtonExecutedAsync(SocketMessageComponent arg)
    {
        // Handle button interactions
        if (buttonExecutedCallbacks.TryGetValue(arg.Data.CustomId, out var callback))
            await callback(arg);
        
        // Handle pagination interactions
        if (paginatedMessages.TryGetValue(arg.Message.Id, out var paginatedMessage))
        {
            var messageChannel = await GetMessageChannelAsync(channelId);
            if (messageChannel is null)
                return;

            var originalMessage = await messageChannel.GetMessageAsync(paginatedMessage.Id);
            if (originalMessage is not IUserMessage userMessage)
                return;
            
            var nextIndex = arg.Data.CustomId switch
            {
                DiscordPaginationUtil.FirstPageButtonId => 0,
                DiscordPaginationUtil.PreviousPageButtonId => Math.Max(paginatedMessage.CurrentPage - 1, 0),
                DiscordPaginationUtil.NextPageButtonId => Math.Min(paginatedMessage.CurrentPage + 1, paginatedMessage.Pages.Count - 1),
                DiscordPaginationUtil.LastPageButtonId => paginatedMessage.Pages.Count - 1,
                _ => paginatedMessage.CurrentPage
            };

            if (nextIndex == paginatedMessage.CurrentPage)
            {
                await arg.RespondAsync("🏁 Reached the end of the pages!", ephemeral: true);
                return;
            }
            
            var embed = DiscordPaginationUtil.GetUiPageEmbed(
                paginatedMessage.Pages[nextIndex],
                nextIndex,
                paginatedMessage.Pages.Count);
            
            var component = DiscordPaginationUtil.GetPaginationControls(nextIndex, paginatedMessage.Pages.Count);
            
            await arg.UpdateAsync(x =>
            {
                x.Content = paginatedMessage.Content;
                x.Embed = embed;
                x.Components = component;
            });
            
            // Update the paginated message
            paginatedMessage.CurrentPage = nextIndex;
            paginatedMessage.LastUpdated = DateTime.UtcNow;
        }
    }

    private Task ClientOnModalSubmittedAsync(SocketModal arg)
    {
        submittedModals.TryAdd(arg.Data.CustomId, arg);
        return Task.CompletedTask;
    }

    private async Task ClientOnSlashCommandExecutedAsync(SocketSlashCommand arg)
    {
        if (!commandExecuteCallbacks.TryGetValue(arg.CommandName, out var callback) || callback is null)
        {
            await arg.RespondAsync("🤔 Hmm, that command doesn't appear to do anything.");
            return;
        }

        try
        {
            var interaction = new DiscordCommandInteraction(this, arg);
            await callback(this, interaction);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while executing the command {CommandName}", arg.CommandName);
            await arg.RespondAsync("🚨 An error occurred while executing the command. Please check the logs.");
        }
    }

    public async Task InitializeAsync(IReadOnlyList<UiCommand> commands)
    {
        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        await WaitUntilReadyAsync();
        
        // Register slash commands
        var guild = client.GetGuild(guildId);
        if (guild is null)
        {
            logger.LogError("The guild with ID {GuildId} was not found", guildId);
            return;
        }

        var applicationCommands = commands.Select(command =>
        {
            var slashCommand = new SlashCommandBuilder()
                .WithName(command.Name)
                .WithDescription($"{command.DisplayName} - {command.Description}");

            foreach (var parameter in command.Parameters)
            {
                var optionType = MapToDiscordCommandOption(parameter.Type);
                var slashCommandOption = new SlashCommandOptionBuilder()
                    .WithName(parameter.Name)
                    .WithDescription($"{parameter.DisplayName} - {parameter.Description}")
                    .WithRequired(parameter.Required)
                    .WithType(optionType);

                if (parameter.Type == UiCommandParameterType.Enum)
                {
                    if (parameter.EnumOptions is null)
                        throw new InvalidOperationException("Enum options must be provided for enum parameters");

                    foreach (var enumOption in parameter.EnumOptions)
                        slashCommandOption.AddChoice($"{enumOption.DisplayName} - {enumOption.Description}", enumOption.Name);
                }

                slashCommand.AddOption(slashCommandOption);
            }
            
            return slashCommand.Build();
        })
        .Cast<ApplicationCommandProperties>()
        .ToArray();
        
        await guild.BulkOverwriteApplicationCommandAsync(applicationCommands);
        
        // Register command execute callbacks
        foreach (var command in commands)
            commandExecuteCallbacks.TryAdd(command.Name, command.ExecuteCallback);
        
        // Add pagination timeout task
        _ = Task.Run(() => PaginationTimeoutAsync(paginationTimeoutTaskCts.Token));
    }

    private async Task PaginationTimeoutAsync(CancellationToken ct = default)
    {
        var timeout = TimeSpan.FromMinutes(5.0f);
        var messagesScheduledForRemoval = new Queue<ulong>();
        
        while (!ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();

            foreach (var message in paginatedMessages.Values)
            {
                if (message.LastUpdated + timeout > DateTime.UtcNow)
                    continue;
                
                // Remove all components from the message
                var messageChannel = await GetMessageChannelAsync(channelId);
                if (messageChannel is null)
                    continue;
                
                var originalMessage = await messageChannel.GetMessageAsync(message.Id);
                if (originalMessage is not IUserMessage userMessage)
                    continue;

                await userMessage.ModifyAsync(x => x.Components = new ComponentBuilder().Build());
                
                // Schedule the message for removal
                messagesScheduledForRemoval.Enqueue(message.Id);
            }

            while (messagesScheduledForRemoval.Count > 0)
            {
                var id = messagesScheduledForRemoval.Dequeue();
                paginatedMessages.TryRemove(id, out _);
            }
            
            await Task.Delay(1000, ct);
        }
    }

    private static ApplicationCommandOptionType MapToDiscordCommandOption(UiCommandParameterType type)
        => type switch
        {
            UiCommandParameterType.String => ApplicationCommandOptionType.String,
            UiCommandParameterType.Integer => ApplicationCommandOptionType.Integer,
            UiCommandParameterType.Boolean => ApplicationCommandOptionType.Boolean,
            UiCommandParameterType.Float => ApplicationCommandOptionType.Number,
            UiCommandParameterType.Enum => ApplicationCommandOptionType.String,
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    
    private Task WaitUntilReadyAsync()
    {
        var tcs = new TaskCompletionSource<bool>();

        Func<Task>? readyTask = null;
        readyTask = () =>
        {
            tcs.SetResult(true);
            client.Ready -= readyTask;
            return Task.CompletedTask;
        };
        client.Ready += readyTask;
        
        return tcs.Task;
    }

    public async Task AnnounceAsync(string message, UiFile? file = null)
    {
        if (client.ConnectionState != ConnectionState.Connected)
            throw new InvalidOperationException("The client is not connected to Discord");
        
        var messageChannel = await GetMessageChannelAsync(channelId);
        if (messageChannel is null)
            return;

        if (file is null)
            await messageChannel.SendMessageAsync(message);
        else
            await messageChannel.SendFileAsync(new FileAttachment(file.Stream, file.Name), message);
    }

    public async Task AnnouncePaginatedAsync(string message, IReadOnlyList<UiPage> pages)
    {
        if (client.ConnectionState != ConnectionState.Connected)
            throw new InvalidOperationException("The client is not connected to Discord");
        
        var messageChannel = await GetMessageChannelAsync(channelId);
        if (messageChannel is null)
            return;

        const int initialPage = 0;

        var embed = DiscordPaginationUtil.GetUiPageEmbed(pages[initialPage], initialPage, pages.Count);
        var component = DiscordPaginationUtil.GetPaginationControls(initialPage, pages.Count);
        var socketMessage = await messageChannel.SendMessageAsync(message, embed: embed, components: component);
        var paginatedMessage = new DiscordPaginatedMessage
        {
            Id = socketMessage.Id,
            Content = message,
            Pages = pages,
            CurrentPage = initialPage,
        };
        AddPaginatedMessage(paginatedMessage);
    }

    public void AddPaginatedMessage(DiscordPaginatedMessage message)
    {
        paginatedMessages.TryAdd(message.Id, message);
    }

    public async Task<FormResponse?> PromptFormAsync(Form form, bool hasFormSummary = true)
    {
        if (client.ConnectionState != ConnectionState.Connected)
            throw new InvalidOperationException("The client is not connected to Discord");
        
        var messageChannel = await GetMessageChannelAsync(channelId);
        
        if (messageChannel is null)
            return null;
        
        // Create an embed with the form title and description
        var formEmbed = new EmbedBuilder()
            .WithTitle(form.Title)
            .WithDescription(form.Description)
            .WithFooter($"Form element count: {form.Elements.Count}");

        var formButtonId = GetRandomId();
        var formId = GetRandomId();
        
        var formButtonComponent = new ComponentBuilder()
            .WithButton("Open Form", formButtonId);
        
        // Send message with embed and a button to open form
        var message = await messageChannel.SendMessageAsync(embed: formEmbed.Build(), components: formButtonComponent.Build());
        
        AddButtonExecutedCallback(formButtonId, async messageComponent =>
        {
            // Open form as a modal
            var modal = new ModalBuilder()
                .WithTitle(form.Title)
                .WithCustomId(formId);

            foreach (var formElement in form.Elements)
                modal.AddTextInput(formElement.DisplayName, formElement.Name, formElement.Multiline ? TextInputStyle.Paragraph : TextInputStyle.Short);
            
            await messageComponent.RespondWithModalAsync(modal.Build());
        });
        
        var modal = await WaitForModalSubmittedAsync(formId);
        RemoveButtonExecutedCallback(formButtonId);
        
        if (modal is null)
            return null;
        
        var formResponse = new FormResponse(modal.Data.Components
            .ToDictionary(x => x.CustomId, x => x.Value));

        var responseSummaryEmbed = new EmbedBuilder()
            .WithTitle("Form Response Summary")
            .WithAuthor(modal.User);

        if (hasFormSummary)
        {
            var formElementNameLookup = form.Elements.ToDictionary(x => x.Name, x => x.DisplayName);
            foreach (var (key, value) in formResponse.Values)
                responseSummaryEmbed.AddField(formElementNameLookup[key], StringUtils.Truncate(value, 1000));

            await modal.RespondAsync("✅ Form submitted successfully!", embed: responseSummaryEmbed.Build());
        }
        else
        {
            await modal.RespondAsync("✅ Form submitted successfully!");
        }
        
        
        // Remove the button from the message
        await message.ModifyAsync(x =>
        {
            x.Embed = new EmbedBuilder()
                .WithTitle(form.Title)
                .WithDescription(form.Description)
                .WithFooter("Form submitted!")
                .Build();
            x.Components = new ComponentBuilder()
                .Build();
        });
        
        return formResponse;
    }

    public async Task PushUpdateNotificationAsync(SteamAppManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;
        
        using var webhookClient = new DiscordWebhookClient(webhookUrl);
        await webhookClient.SendMessageAsync(embeds:
        [
            new EmbedBuilder()
                .WithAuthor(new EmbedAuthorBuilder
                {
                    Name = packageOptions.Name,
                    Url = $"https://www.nuget.org/packages/{packageOptions.Name}/{manifest.Version}",
                    IconUrl = string.IsNullOrWhiteSpace(webhookPackageIconUrl)
                        ? $"https://api.nuget.org/v3-flatcontainer/{packageOptions.Name.ToLower()}/{manifest.Version}/icon"
                        : webhookPackageIconUrl
                })
                .WithTitle(
                    $"New release: **{manifest.Version}** {(manifest.BranchName != "public" ? $"[{manifest.BranchName}] " : "")}")
                .WithFields([
                    new EmbedFieldBuilder { Name = "Package Version", Value = manifest.Version, IsInline = true },
                    new EmbedFieldBuilder { Name = "Manifest ID", Value = manifest.Id, IsInline = true },
                    new EmbedFieldBuilder { Name = "Branch", Value = $"`{manifest.BranchName}`", IsInline = true },
                    new EmbedFieldBuilder
                    {
                        Name = "Patch Notes",
                        Value = string.IsNullOrWhiteSpace(manifest.PatchNotes)
                            ? "*(none)*"
                            : $"```md\n{StringUtils.Truncate(manifest.PatchNotes, 1000)}```",
                    }
                ])
                .WithColor(0xDDAE73)
                .WithTimestamp(manifest.CreatedAt)
                .Build()
        ], username: webhookName, avatarUrl: webhookAvatarUrl);

        if (!string.IsNullOrWhiteSpace(manifest.PatchNotes))
        {
            var fileName = $"changelog-{manifest.Version}.md";

            using var contentMs = new MemoryStream(Encoding.UTF8.GetBytes(manifest.PatchNotes));
            await webhookClient.SendFileAsync(contentMs, fileName, string.Empty);
        }
    }

    private string GetRandomId()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        return new string(
            Enumerable
                .Range(0, 16)
                .Select(x => chars[Random.Shared.Next(chars.Length)])
                .ToArray());
    }
    
    private Task<IMessageChannel?> GetMessageChannelAsync(ulong channelId)
    {
        var guild = client.GetGuild(guildId);
        if (guild is null)
        {
            logger.LogError("The guild with ID {GuildId} was not found", guildId);
            return Task.FromResult<IMessageChannel?>(null);
        }
        
        var channel = guild.GetChannel(channelId);
        if (channel is not IMessageChannel messageChannel)
        {
            logger.LogError("The channel with ID {ChannelId} was not found", channelId);
            return Task.FromResult<IMessageChannel?>(null);
        }
        
        return  Task.FromResult<IMessageChannel?>(messageChannel);
    }
    
    private void AddButtonExecutedCallback(string customId, ButtonExecutedCallback callback)
    {
        buttonExecutedCallbacks.TryAdd(customId, callback);
    }
    
    private void RemoveButtonExecutedCallback(string customId)
    {
        buttonExecutedCallbacks.TryRemove(customId, out _);
    }
    
    private async Task<SocketModal?> WaitForModalSubmittedAsync(string modalId)
    {
        while (!submittedModals.ContainsKey(modalId))
            await Task.Delay(100);
        
        submittedModals.TryRemove(modalId, out var modal);
        return modal;
    }

    public async ValueTask DisposeAsync()
    {
        await paginationTimeoutTaskCts.CancelAsync();
        await client.DisposeAsync();
    }
}