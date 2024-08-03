namespace FlexPkg.UserInterface;

public interface IUiCommandInteraction
{
    IReadOnlyDictionary<string, object> Arguments { get; }

    Task FlagAsLongRunning();
    Task RespondAsync(string message, UiFile? file = null, bool error = false);
}