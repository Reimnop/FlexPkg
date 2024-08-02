namespace FlexPkg.UserInterface;

public interface IUserInterface
{
    Task AnnounceAsync(string message, UiFile? file = null);
    Task<FormResponse?> PromptFormAsync(Form form, bool hasFormSummary = true);
}