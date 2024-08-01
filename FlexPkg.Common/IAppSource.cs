namespace FlexPkg.Common;

public interface IAppSource
{
    Task<IAppVersion> GetLatestAppVersionAsync(IAppIdentifier appIdentifier);
    Task DownloadAppAsync(string path, IAppVersion appVersion);
}