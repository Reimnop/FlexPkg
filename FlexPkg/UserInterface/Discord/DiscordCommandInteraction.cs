using Discord.WebSocket;

namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordCommandInteraction : IUiCommandInteraction
{
    private delegate Task RespondDelegate(string message, UiFile? file, bool error);
    
    public IReadOnlyDictionary<string, object> Arguments => arguments;
    
    private readonly SocketSlashCommand socketSlashCommand;
    private readonly Dictionary<string, object> arguments;
    
    private bool delayed;
    
    public DiscordCommandInteraction(SocketSlashCommand socketSlashCommand)
    {
        this.socketSlashCommand = socketSlashCommand;
        arguments = socketSlashCommand.Data.Options.ToDictionary(x => x.Name, x => x.Value);
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

    private RespondDelegate GetRespondDelegate()
        => delayed 
            ? (message, file, error) => file is null 
                ? socketSlashCommand.FollowupAsync(message, ephemeral: error)
                : socketSlashCommand.FollowupWithFileAsync(file.Stream, file.Name, message, ephemeral: error)
            : (message, file, error) => file is null
                ? socketSlashCommand.RespondAsync(message, ephemeral: error)
                : socketSlashCommand.RespondWithFileAsync(file.Stream, file.Name, message, ephemeral: error);
}