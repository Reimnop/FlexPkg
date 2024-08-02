using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;

namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordUserInterface : IUserInterface
{
    private delegate Task ButtonExecutedCallback(SocketMessageComponent messageComponent);

    public int NetworkLatency => client.Latency;
    
    private readonly DiscordSocketClient client = new();
    
    private readonly string token;
    private readonly ulong guildId;
    private readonly ulong channelId;
    private readonly ILogger<DiscordUserInterface> logger;

    private readonly ConcurrentDictionary<string, ButtonExecutedCallback> buttonExecutedCallbacks = [];
    private readonly ConcurrentDictionary<string, SocketModal> submittedModals = [];
    private readonly ConcurrentDictionary<string, UiCommandExecuteCallback?> commandExecuteCallbacks = [];

    public DiscordUserInterface(CliOptions options, ILogger<DiscordUserInterface> logger)
    {
        token = options.DiscordToken;
        guildId = options.GuildId;
        channelId = options.ChannelId;
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
        if (buttonExecutedCallbacks.TryGetValue(arg.Data.CustomId, out var callback))
            await callback(arg);
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
            var interaction = new DiscordCommandInteraction(arg);
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
                responseSummaryEmbed.AddField(formElementNameLookup[key], value);

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
}