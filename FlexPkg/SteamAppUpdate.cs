using FlexPkg.Data;
using FlexPkg.Steam;

namespace FlexPkg;

public class SteamAppUpdate
{
    public required SteamAppVersion Version { get; set; }
    public required List<SteamAppManifest> ExistingBranches { get; set; }
}