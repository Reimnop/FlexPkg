namespace FlexPkg.UserInterface;

public record UiCommandParameter(string Name, string DisplayName, string Description, UiCommandParameterType Type, IReadOnlyList<UiCommandParameterEnumOption>? EnumOptions = null, bool Required = true);