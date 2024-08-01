using FlexPkg.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FlexPkg.Steam;

public static class Extension
{
    public static void AddSteam(this IServiceCollection services, SteamAuthenticationInfo authenticationInfo)
    {
        services.AddTransient(authenticationInfo.TokenStoreFactory);
        services.AddSingleton<IAppSource, SteamAppSource>(serviceProvider => new SteamAppSource(
            authenticationInfo.Username, 
            authenticationInfo.Password, 
            authenticationInfo.AuthenticatorFactory,
            serviceProvider, 
            serviceProvider.GetRequiredService<ILogger<SteamAppSource>>()));
    }
}