using Discord.WebSocket;

namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordCommandInteraction : IUiCommandInteraction
{
    private delegate Task RespondDelegate(string message, UiFile? file, bool error);
    
    public IReadOnlyDictionary<string, object> Arguments => arguments;
    
    private readonly SocketSlashCommand socketSlashCommand;
    private readonly DiscordUserInterface userInterface;
    private readonly Dictionary<string, object> arguments;
    
    private bool delayed;
    
    public DiscordCommandInteraction(DiscordUserInterface userInterface, SocketSlashCommand socketSlashCommand)
    {
        this.socketSlashCommand = socketSlashCommand;
        this.userInterface = userInterface;
        arguments = socketSlashCommand.Data.Options
            .ToDictionary(x => x.Name, x => x.Value);
    }

    public async Task DelayResponseAsync()
    {
        delayed = true;
        await socketSlashCommand.DeferAsync();
    }

    public Task RespondAsync(string message, UiFile? file = null, bool error = false)
    {
        var respondDelegate = GetRespondDelegate();
        return respondDelegate(message, file, error);
    }

    public async Task RespondPaginatedAsync(string message, IReadOnlyList<UiPage> pages)
    {
        const int initialPage = 0;
        
        if (delayed)
        {
            var text = DiscordPaginationUtil.GetPageText(message, initialPage, pages.Count);
            var embed = DiscordPaginationUtil.GetUiPageEmbed(pages[initialPage]);
            var component = DiscordPaginationUtil.GetPaginationControls(initialPage, pages.Count);
            var followupMessage = await socketSlashCommand.FollowupAsync(text, embed: embed, components: component);
            var paginatedMessage = new DiscordPaginatedMessage
            {
                Id = followupMessage.Id,
                Content = message,
                Pages = pages,
                CurrentPage = initialPage,
            };
            userInterface.AddPaginatedMessage(paginatedMessage);
        }
        else
        {
            var text = DiscordPaginationUtil.GetPageText(message, initialPage, pages.Count);
            var embed = DiscordPaginationUtil.GetUiPageEmbed(pages[initialPage]);
            var component = DiscordPaginationUtil.GetPaginationControls(initialPage, pages.Count);
            await socketSlashCommand.RespondAsync(text, embed: embed, components: component);
            var interactionMessage = await socketSlashCommand.GetOriginalResponseAsync();
            var paginatedMessage = new DiscordPaginatedMessage
            {
                Id = interactionMessage.Id,
                Content = message,
                Pages = pages,
                CurrentPage = initialPage,
            };
            userInterface.AddPaginatedMessage(paginatedMessage);
        }
    }

    private RespondDelegate GetRespondDelegate()
        => delayed 
            ? (message, file, error) => file is null 
                ? socketSlashCommand.FollowupAsync(message, ephemeral: error)
                : socketSlashCommand.FollowupWithFileAsync(file.Stream, file.Name, message, ephemeral: error)
            : (message, file, error) => file is null
                ? socketSlashCommand.RespondAsync(message, ephemeral: error)
                : socketSlashCommand.RespondWithFileAsync(file.Stream, file.Name, message, ephemeral: error);
}