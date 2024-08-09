using Microsoft.EntityFrameworkCore;

namespace FlexPkg.Database;

[PrimaryKey(nameof(Id), nameof(BranchName))]
public sealed class SteamAppManifest
{
    public required ulong Id { get; set; }
    public required string BranchName { get; set; }
    public string? Version { get; set; }
    public string? PatchNotes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Handled { get; set; } = false;
}