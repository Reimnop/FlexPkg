namespace FlexPkg.UserInterface;

public record UiPage(string Title, string? Content, IReadOnlyList<UiPageSection>? Sections);