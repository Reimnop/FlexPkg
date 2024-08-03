namespace FlexPkg.UserInterface;

public interface IUserInterface
{
    // Should be non-zero for networked user interfaces (e.g. web)
    int NetworkLatency { get; }

    Task InitializeAsync(IReadOnlyList<UiCommand> commands);
    Task AnnounceAsync(string message, UiFile? file = null);
    Task AnnouncePaginatedAsync(string message, IReadOnlyList<UiPage> pages);
    Task<FormResponse?> PromptFormAsync(Form form, bool hasFormSummary = true);
}