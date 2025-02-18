using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using FlexPkg.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.CDN;

namespace FlexPkg.Steam;

public sealed class SteamAppSource(string username, string password, Func<IServiceProvider, IAuthenticator> authenticatorFactory, IServiceProvider serviceProvider, ILogger<SteamAppSource> logger) : IAppSource
{
    public async Task<IEnumerable<IAppVersion>> GetLatestAppVersionsAsync(IAppIdentifier appIdentifier)
    {
        if (appIdentifier is not SteamAppIdentifier steamAppIdentifier)
            throw new InvalidCastException($"Invalid app identifier type, expected {typeof(SteamAppIdentifier)}");

        await using var steamHub = await GetSteamHubAsync();
        var steamApps = steamHub.Apps;
        var picsGetProductInfoResult = await steamApps.PICSGetProductInfo(new SteamApps.PICSRequest(steamAppIdentifier.AppId), null);
        if (picsGetProductInfoResult.Failed)
            throw new Exception("Failed to get product info");
        
        var productInfo = picsGetProductInfoResult.Results![0].Apps[steamAppIdentifier.AppId];
        var depotId = steamAppIdentifier.DepotId.ToString(CultureInfo.InvariantCulture);
        
        return productInfo.KeyValues["depots"][depotId]["manifests"].Children
            .Where(b => steamAppIdentifier.BranchNames.Contains(b.Name!))
            .Select(b => new SteamAppVersion(
                steamAppIdentifier.AppId,
                steamAppIdentifier.DepotId,
                b.Name!,
                ulong.Parse(b["gid"].AsString()!)));
    }

    public async Task DownloadAppAsync(string path, IAppVersion appVersion)
    {
        if (appVersion is not SteamAppVersion steamAppVersion)
            throw new InvalidCastException($"Invalid app version type, expected {typeof(SteamAppVersion)}");
        
        Directory.CreateDirectory(path);
        
        await using var steamHub = await GetSteamHubAsync();
        
        logger.LogInformation("Fetching manifest for Steam game");
        
        var steamClient = steamHub.Client;
        var steamContent = steamHub.Content;
        var steamApps = steamHub.Apps;
        
        var servers = (await steamContent.GetServersForSteamPipe(null, 12)).ToList();
        var cdnClient = new Client(steamClient);
        var code = await steamContent.GetManifestRequestCode(steamAppVersion.DepotId, steamAppVersion.AppId, steamAppVersion.ManifestId, steamAppVersion.BranchName);
        var key = await steamApps.GetDepotDecryptionKey(steamAppVersion.DepotId, steamAppVersion.AppId);
        var manifest = await DownloadManifestAsync(cdnClient, steamAppVersion.DepotId, steamAppVersion.ManifestId, code, servers, key.DepotKey);
        
        logger.LogInformation("Preserving storage for all files");
        
        // Initialize all files
        foreach (var file in manifest.Files!)
        {
            if (file.TotalSize == 0 || file.Chunks.Count == 0)
                continue;
            
            var filePath = Path.Combine(path, file.FileName);
            var directory = Path.GetDirectoryName(filePath)!;
            Directory.CreateDirectory(directory);
            
            await using var fs = File.Open(filePath, FileMode.OpenOrCreate);
            fs.SetLength((long) file.TotalSize);
        }
        
        // Filter out files that have no size or chunks
        var files = manifest.Files!.Where(file => file.TotalSize != 0 && file.Chunks.Count != 0);
        
        logger.LogInformation("Mapping all preserved files to memory");
        
        // Memory map all files
        var memoryMappedFiles = files
            .Select(file =>
            {
                var filePath = Path.Combine(path, file.FileName);
                return (file, MemoryMappedFile.CreateFromFile(filePath, FileMode.Open));
            })
            .ToList();
        
        logger.LogInformation("Downloading files");
        try
        {
            var chunks = memoryMappedFiles.SelectMany(mmf => mmf.file.Chunks.Select(chunk => (mmf.Item2, chunk, 0)));
            var chunkQueue = new ConcurrentQueue<(MemoryMappedFile, DepotManifest.ChunkData, int)>(chunks);

            const int maxRetries = 8;
        
            // Download all the files in parallel
            var tasks = new List<Task>(12);
            for (var i = 0; i < 12; i++)
            {
                tasks.Add(Task.Run(async () =>
                {
                    while (chunkQueue.TryDequeue(out var tuple))
                    {
                        var (mmf, chunk, retries) = tuple;
                    
                        try
                        {
                            // Get random server cause why not
                            var server = servers[Random.Shared.Next(servers.Count)];
                            var buffer = new byte[chunk.UncompressedLength];
                            await cdnClient.DownloadDepotChunkAsync(steamAppVersion.DepotId, chunk, server, buffer, key.DepotKey);
                            await using var view = mmf.CreateViewStream((long) chunk.Offset, chunk.UncompressedLength);
                            await view.WriteAsync(buffer);
                        }
                        catch (Exception ex)
                        {
                            if (retries >= maxRetries)
                            {
                                logger.LogError(ex, "Failed to download chunk after {MaxRetries} retries", maxRetries);
                                throw;
                            }

                            logger.LogError(ex, "Failed to download chunk, retrying ({Count}/{MaxCount})", retries, maxRetries);
                            chunkQueue.Enqueue((mmf, chunk, retries + 1));
                        }
                    }
                }));
            }
        
            await Task.WhenAll(tasks);
        
            logger.LogInformation("Finished!");
        }
        finally
        {
            // Make sure we don't leak memory mapped files
            foreach (var (_, mmf) in memoryMappedFiles)
                mmf.Dispose();
        }
    }
    
    private async Task<DepotManifest> DownloadManifestAsync(Client cdnClient, uint depotId, ulong manifestId, ulong manifestRequestCode, IEnumerable<Server> servers, byte[]? depotKey = null)
    {
        foreach (var server in servers)
        {
            try
            {
                return await cdnClient.DownloadManifestAsync(depotId, manifestId, manifestRequestCode, server, depotKey);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to download manifest from server {Server}, retrying", server.Host);
            }
        }
        
        throw new Exception("Failed to download manifest from all servers");
    }

    private async Task<SteamHub> GetSteamHubAsync()
    {
        var tokenStore = serviceProvider.GetRequiredService<ISteamTokenStore>();
        return await SteamHub.CreateAsync(username, password, tokenStore, () => authenticatorFactory(serviceProvider));
    }
}