namespace FlexPkg.Steam;

public interface ISteamTokenStore
{
    Task<string?> GetTokenAsync(string username);
    Task SaveTokenAsync(string username, string token);
}