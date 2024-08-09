using FlexPkg.Database;
using FlexPkg.Steam;

namespace FlexPkg;

public sealed class SteamTokenStore(FlexPkgContext context) : ISteamTokenStore
{
    public async Task<string?> GetTokenAsync(string username)
    {
        var account = await context.SteamAccounts.FindAsync(username);
        return account?.Token;
    }

    public async Task SaveTokenAsync(string username, string token)
    {
        var account = await context.SteamAccounts.FindAsync(username);
        if (account is null)
        {
            account = new SteamAccount
            {
                Username = username, 
                Token = token
            };
            await context.SteamAccounts.AddAsync(account);
        }
        else
        {
            account.Token = token;
        }

        await context.SaveChangesAsync();
    }
}