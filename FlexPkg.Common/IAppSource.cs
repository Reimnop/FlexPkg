namespace FlexPkg.Common;

public interface IAppSource
{
    Task<IEnumerable<IAppVersion>> GetLatestAppVersionsAsync(IAppIdentifier appIdentifier);
    Task DownloadAppAsync(string path, IAppVersion appVersion);
}