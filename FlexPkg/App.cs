using System.IO.Compression;
using Cpp2IL.Core;
using Discord;
using FlexPkg.Common;
using FlexPkg.Data;
using Microsoft.Extensions.Logging;
using FlexPkg.Steam;
using FlexPkg.UserInterface;
using Il2CppInterop.Common;
using Il2CppInterop.Generator;
using Il2CppInterop.Generator.Runners;
using Microsoft.EntityFrameworkCore;
using Mono.Cecil;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NullLogger = NuGet.Common.NullLogger;

namespace FlexPkg;

public sealed class App(CliOptions options, FlexPkgContext context, IAppSource appSource, IUserInterface userInterface, ILogger<App> logger)
{
    private const string DownloadPath = "output";
    private const string Cpp2IlOutputPath = "cpp2il_output";
    private const string UnityBaseLibsPath = "unity_base_libs";
    
    private static readonly object Cpp2IlLock = new();
    
    public async Task RunAsync(CancellationToken ct = default)
    {
        await userInterface.AnnounceAsync("✅ FlexPkg started!");

        while (!ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            
            try
            {
                var appId = options.AppId;
                var depotId = options.DepotId;
                var branchName = options.BranchName;
                logger.LogInformation("Checking for updates");
                await userInterface.AnnounceAsync("🔄 Checking for updates...");
                await HandleSteam(appId, depotId, branchName, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while handling Steam");
                await userInterface.AnnounceAsync("❌ An error occurred while handling Steam. Please check the logs.");
            }
            
            await userInterface.AnnounceAsync($"⏰ Waiting for next check, estimated time: {TimestampTag.FromDateTime(DateTime.UtcNow.AddHours(1.0f))}");
            await Task.Delay(TimeSpan.FromHours(1.0f), ct);
        }
    }

    private async Task HandleSteam(uint appId, uint depotId, string branchName, CancellationToken ct = default)
    {
        var appIdentifier = new SteamAppIdentifier(appId, depotId, branchName);
        var appVersion = await appSource.GetLatestAppVersionAsync(appIdentifier);
        var steamAppVersion = (SteamAppVersion) appVersion;
        
        if (await IsLocalManifestLatestAsync(steamAppVersion.ManifestId))
        {
            logger.LogInformation("The local manifest is up-to-date");
            await userInterface.AnnounceAsync("🎉 The local manifest is up-to-date! No action will be performed.");
            return;
        }

        await userInterface.AnnounceAsync("📦 An update is detected! Downloading...");
        
        logger.LogInformation("Cleaning up old files");
        
        if (Directory.Exists(DownloadPath))
            Directory.Delete(DownloadPath, true);
        
        if (Directory.Exists(Cpp2IlOutputPath))
            Directory.Delete(Cpp2IlOutputPath, true);
        
        logger.LogInformation("Downloading the latest app version");
        await appSource.DownloadAppAsync("output", appVersion);
        
        logger.LogInformation("Storing the latest manifest to our database");
        await context.SteamAppManifests.AddAsync(new SteamAppManifest
        {
            Id = steamAppVersion.ManifestId,
        }, ct);
        await context.SaveChangesAsync(ct);
        
        await userInterface.AnnounceAsync("🎉 The latest app version has been downloaded and stored!");
        
        // Open form
        var form = new Form(
            "Configure manifest", 
            $"App ID: **{steamAppVersion.AppId}**\nDepot ID: **{steamAppVersion.DepotId}**\nManifest ID: **{steamAppVersion.ManifestId}**", new[]
        {
            new FormElement("version", "Version"),
            new FormElement("patch_notes", "Patch Notes"),
        });
        
        var response = await userInterface.PromptFormAsync(form);
        if (response is null)
            return;
        
        // Save the manifest
        logger.LogInformation("Updating the manifest with the new version");
        var manifest = await context.SteamAppManifests.FindAsync(steamAppVersion.ManifestId);
        if (manifest is null)
        {
            logger.LogWarning("The manifest is not found in the database");
            return;
        }
        
        manifest.Version = response.Values["version"];
        manifest.PatchNotes = response.Values["patch_notes"];
        await context.SaveChangesAsync(ct);
        
        // Run Cpp2IL on it
        logger.LogInformation("Running Cpp2IL on the latest app version");
        await userInterface.AnnounceAsync("🔧 Running Cpp2IL on the latest app version...");

        // Since Cpp2Il is a singleton, we need to lock it
        int[]? unityVersion;
        List<AssemblyDefinition> dummyDlls;
        lock (Cpp2IlLock)
        {
            logger.LogInformation("Cpp2IL instance has been locked");
            
            var unityDataDirPath = Directory.GetDirectories(DownloadPath, "*_Data", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (unityDataDirPath is null)
            {
                logger.LogWarning("Unity data directory is not found");
                return;
            }

            var unityGameExePath = $"{unityDataDirPath[..^5]}.exe";
            if (!File.Exists(unityGameExePath))
            {
                logger.LogWarning("Unity game executable is not found");
                return;
            }
            
            unityVersion = Cpp2IlApi.DetermineUnityVersion(unityGameExePath, unityDataDirPath);
            if (unityVersion is null)
            {
                logger.LogWarning("Could not determine the Unity version");
                return;
            }

            Cpp2IlApi.InitializeLibCpp2Il(
                Path.Combine(DownloadPath, "GameAssembly.dll"),
                Path.Combine(unityDataDirPath, "il2cpp_data/Metadata/global-metadata.dat"),
                unityVersion,
                false);

            dummyDlls = Cpp2IlApi.MakeDummyDLLs();
        }
        // Don't need Cpp2IL anymore, unlock it
        
        Directory.CreateDirectory(Cpp2IlOutputPath);
            
        logger.LogInformation("Running Il2CppInterop");
        Il2CppInteropGenerator
            .Create(
                new GeneratorOptions
                {
                    Source = dummyDlls,
                    OutputDir = Cpp2IlOutputPath,
                    UnityBaseLibsDir = await GetUnityBaseLibsPath(unityVersion),
                })
            .AddInteropAssemblyGenerator()
            .AddLogger(logger)
            .Run();
        
        await userInterface.AnnounceAsync("🎉 Game has been successfully decompiled!");
        
        logger.LogInformation("Building NuGet package");
        await userInterface.AnnounceAsync("📦 Building NuGet package...");
        var packageBuilder = new PackageBuilder
        {
            Id = options.PackageName,
            Description = options.PackageDescription,
            Version = new NuGetVersion(manifest.Version),
        };
        packageBuilder.Authors.AddRange(options.PackageAuthors.Split(';'));
        packageBuilder.DependencyGroups.Add(new PackageDependencyGroup(
            targetFramework: NuGetFramework.Parse("netstandard2.0"),
            packages: []));
        
        foreach (var file in Directory.GetFiles(Cpp2IlOutputPath))
        {
            packageBuilder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = file,
                TargetPath = $"lib/netstandard2.0/{Path.GetFileName(file)}",
            });
        }
        
        await PushNuGetPackage(packageBuilder);
        
        await userInterface.AnnounceAsync("✅ All done. Thank you!");
    }

    private async Task PushNuGetPackage(PackageBuilder packageBuilder)
    {
        logger.LogInformation("Pushing NuGet package");
        await userInterface.AnnounceAsync("⬆️ Pushing package to NuGet...");
        
        var path = Path.GetTempFileName();
        await using (var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            packageBuilder.Save(fileStream);
        
        var repository = Repository.Factory.GetCoreV3(options.NuGetSource);
        var resource = await repository.GetResourceAsync<PackageUpdateResource>();
        
        try
        {
            await resource.Push(
                packagePaths: new List<string> { path },
                symbolSource: null,
                timeoutInSecond: 5 * 60,
                disableBuffering: false,
                getApiKey: _ => options.NuGetApiKey,
                getSymbolApiKey: _ => null,
                noServiceEndpoint: false,
                skipDuplicate: false,
                symbolPackageUpdateResource: null,
                allowInsecureConnections: false,
                log: NullLogger.Instance);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while pushing NuGet package");
            await userInterface.AnnounceAsync(
                "❌ An error occurred while pushing NuGet package. Please check the logs.");
        }
    }

    private async Task<string?> GetUnityBaseLibsPath(int[] unityVersion)
    {
        // Check if we already have it
        var unityBaseLibsVersion = string.Join('.', unityVersion);
        var unityBaseLibsPath = Path.Combine(UnityBaseLibsPath, $"{unityBaseLibsVersion}");
        
        if (Directory.Exists(unityBaseLibsPath))
            return unityBaseLibsPath;
        
        // Download it
        var unityBaseLibsUrl = $"https://unity.bepinex.dev/libraries/{unityBaseLibsVersion}.zip";
        await userInterface.AnnounceAsync($"📦 Downloading Unity base libraries for version {unityBaseLibsVersion}...");
        
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync(unityBaseLibsUrl);
        if (!response.IsSuccessStatusCode)
        {
            await userInterface.AnnounceAsync($"❌ Failed to download Unity base libraries for version {unityBaseLibsVersion}");
            return null;
        }
        
        Directory.CreateDirectory(unityBaseLibsPath);
        using var zipArchive = new ZipArchive(await response.Content.ReadAsStreamAsync());
        
        foreach (var entry in zipArchive.Entries)
        {
            var entryPath = Path.Combine(unityBaseLibsPath, entry.FullName);
            entry.ExtractToFile(entryPath, true);
        }
        
        return unityBaseLibsPath;
    }

    private async Task<bool> IsLocalManifestLatestAsync(ulong latestManifestId)
    {
        var latestManifest = await context.SteamAppManifests
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();

        if (latestManifest is null)
            return false;
        
        return latestManifest.Id == latestManifestId;
    }
}