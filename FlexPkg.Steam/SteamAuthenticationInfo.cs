using SteamKit2.Authentication;

namespace FlexPkg.Steam;

public record SteamAuthenticationInfo(string Username, string Password, Func<IServiceProvider, ISteamTokenStore> TokenStoreFactory, Func<IServiceProvider, IAuthenticator> AuthenticatorFactory);