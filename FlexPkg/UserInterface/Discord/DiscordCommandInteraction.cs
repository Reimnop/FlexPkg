using Discord.WebSocket;

namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordCommandInteraction : IUiCommandInteraction
{
    public IReadOnlyDictionary<string, object> Arguments => arguments;
    
    private readonly SocketSlashCommand socketSlashCommand;
    private readonly Dictionary<string, object> arguments;
    
    private bool longRunning;
    
    public DiscordCommandInteraction(SocketSlashCommand socketSlashCommand)
    {
        this.socketSlashCommand = socketSlashCommand;
        arguments = socketSlashCommand.Data.Options.ToDictionary(x => x.Name, x => x.Value);
    }

    public async Task FlagAsLongRunning()
    {
        await socketSlashCommand.DeferAsync();
        longRunning = true;
    }

    public async Task RespondAsync(string message, UiFile? file = null, bool error = false)
    {
        // TODO: Evil if else chain
        if (file is null)
            if (longRunning)
                await socketSlashCommand.FollowupAsync(message, ephemeral: error);
            else
                await socketSlashCommand.RespondAsync(message, ephemeral: error);
        else
            if (longRunning)
                await socketSlashCommand.FollowupWithFileAsync(file.Stream, file.Name, message, ephemeral: error);
            else
                await socketSlashCommand.RespondWithFileAsync(file.Stream, file.Name, message, ephemeral: error);
    }
}