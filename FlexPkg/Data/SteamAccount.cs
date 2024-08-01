using Microsoft.EntityFrameworkCore;

namespace FlexPkg.Data;

[PrimaryKey(nameof(Username))]
public sealed class SteamAccount
{
    public required string Username { get; set; }
    public required string Token { get; set; }
}