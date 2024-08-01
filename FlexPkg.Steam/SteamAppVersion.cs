using FlexPkg.Common;

namespace FlexPkg.Steam;

public sealed class SteamAppVersion(uint appId, uint depotId, ulong manifestId) : IAppVersion
{
    public uint AppId { get; } = appId;
    public uint DepotId { get; } = depotId;
    public ulong ManifestId { get; } = manifestId;
}