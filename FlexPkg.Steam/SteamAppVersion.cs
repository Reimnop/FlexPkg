using FlexPkg.Common;

namespace FlexPkg.Steam;

public sealed class SteamAppVersion(uint appId, uint depotId, string branchName, ulong manifestId) : IAppVersion
{
    public uint AppId { get; } = appId;
    public uint DepotId { get; } = depotId;
    public string BranchName { get; } = branchName;
    public ulong ManifestId { get; } = manifestId;
}