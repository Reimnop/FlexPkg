using FlexPkg.Common;

namespace FlexPkg.Steam;

public sealed class SteamAppIdentifier(uint appId, uint depotId, IEnumerable<string> branchNames) : IAppIdentifier
{
    public uint AppId { get; } = appId;
    public uint DepotId { get; } = depotId;
    public IEnumerable<string> BranchNames { get; } = branchNames;
}