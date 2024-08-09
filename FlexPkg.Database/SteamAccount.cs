using Microsoft.EntityFrameworkCore;

namespace FlexPkg.Database;

[PrimaryKey(nameof(Username))]
public sealed class SteamAccount
{
    public required string Username { get; set; }
    public required string Token { get; set; }
}