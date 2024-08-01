using FlexPkg.Common;

namespace FlexPkg.Steam;

public sealed class SteamAppIdentifier(uint appId, uint depotId, string branchName) : IAppIdentifier
{
    public uint AppId { get; } = appId;
    public uint DepotId { get; } = depotId;
    public string BranchName { get; } = branchName;
}