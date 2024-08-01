namespace FlexPkg.UserInterface;

public record Form(string Title, string Description, IReadOnlyList<FormElement> Elements);