namespace FlexPkg.UserInterface;

public record UiCommand(string Name, string DisplayName, string Description, IReadOnlyList<UiCommandParameter> Parameters, UiCommandExecuteCallback? ExecuteCallback);