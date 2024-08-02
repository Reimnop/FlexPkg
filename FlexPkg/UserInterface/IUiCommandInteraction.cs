namespace FlexPkg.UserInterface;

public interface IUiCommandInteraction
{
    IReadOnlyDictionary<string, object> Arguments { get; }

    Task RespondAsync(string message);
    Task RespondAsync(string message, bool error);
    Task RespondAsync(string message, UiFile file);
}