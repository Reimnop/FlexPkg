using FlexPkg.UserInterface;
using SteamKit2.Authentication;

namespace FlexPkg;

public sealed class SteamAuthenticator(IUserInterface userInterface) : IAuthenticator
{
    public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
    {
        throw new NotImplementedException();
    }

    public async Task<bool> AcceptDeviceConfirmationAsync()
    {
        await userInterface.AnnounceAsync("STEAM GUARD! Please accept the device confirmation request.");
        return true;
    }
}