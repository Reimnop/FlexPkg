using FlexPkg.UserInterface;
using SteamKit2.Authentication;

namespace FlexPkg;

public sealed class SteamAuthenticator(IUserInterface userInterface) : IAuthenticator
{
    public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
    {
        var response = await userInterface.PromptFormAsync(
            new Form(
                "🔒 Steam Guard!",
                previousCodeWasIncorrect
                    ? "The Steam Guard code you entered was incorrect. Please try again."
                    : "Please enter the Steam Guard code from your authenticator app.",
                [
                    new FormElement("code", "Code")
                ]),
            false);
        if (response is null)
            throw new OperationCanceledException();
        return response.Values["code"];
    }

    public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
    {
        var response = await userInterface.PromptFormAsync(
            new Form(
                "🔒 Steam Guard!",
                previousCodeWasIncorrect
                    ? "The Steam Guard code you entered was incorrect. Please try again."
                    : "Please enter the Steam Guard code sent to your email.",
                [
                    new FormElement("code", "Code")
                ]),
            false);
        if (response is null)
            throw new OperationCanceledException();
        return response.Values["code"];
    }

    public async Task<bool> AcceptDeviceConfirmationAsync()
    {
        await userInterface.AnnounceAsync("🔒 Steam Guard! Please accept the device confirmation request.");
        return true;
    }
}