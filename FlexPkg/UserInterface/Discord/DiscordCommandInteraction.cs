using Discord.WebSocket;

namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordCommandInteraction : IUiCommandInteraction
{
    public IReadOnlyDictionary<string, object> Arguments => arguments;
    
    private readonly SocketSlashCommand socketSlashCommand;
    private readonly Dictionary<string, object> arguments;
    
    public DiscordCommandInteraction(SocketSlashCommand socketSlashCommand)
    {
        this.socketSlashCommand = socketSlashCommand;
        arguments = socketSlashCommand.Data.Options.ToDictionary(x => x.Name, x => x.Value);
    }

    public async Task RespondAsync(string message, UiFile? file = null)
    {
        if (file is null)
            await socketSlashCommand.RespondAsync(message);
        else
            await socketSlashCommand.RespondWithFileAsync(file.Stream, file.Name, message);
    }
}