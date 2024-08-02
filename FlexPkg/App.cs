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
    
    private delegate Task SteamCheckTaskDelegate(CancellationToken ct = default);
    
    private static readonly object Cpp2IlLock = new();

    private SteamCheckTaskDelegate? steamCheckTaskDelegate;
    
    public async Task RunAsync(CancellationToken ct = default)
    {
        // Initialize the UI
        await userInterface.InitializeAsync([
            new UiCommand(
                "ping",
                "Ping",
                "Pings the bot.",
                [],
                (ui, interaction) => interaction.RespondAsync($"🏓 Pong! Latency: **{ui.NetworkLatency}ms**.")),
            new UiCommand(
                "check",
                "Check",
                "Forces the bot to check for a new app update.",
                [],
                async (_, interaction) =>
                {
                    if (steamCheckTaskDelegate is not null)
                    {
                        await interaction.RespondAsync("❌ An update check is already in progress.", error: true);
                        return;
                    }
                    
                    steamCheckTaskDelegate = ct => HandleSteam(options.AppId, options.DepotId, options.BranchName, ct: ct);
                    await interaction.RespondAsync("✅ An update check has been queued!");
                }),
            new UiCommand(
                "addmanifest",
                "Add Manifest",
                "Manually add a manifest to the database.",
                [
                    new UiCommandParameter(
                        "id",
                        "ID",
                        "The ID of the manifest.",
                        UiCommandParameterType.String)
                ],
                async (_, interaction) =>
                {
                    if (!ulong.TryParse(interaction.Arguments["id"] as string, out var id))
                    {
                        await interaction.RespondAsync("❌ Invalid manifest ID.", error: true);
                        return;
                    }
                    
                    if (steamCheckTaskDelegate is not null)
                    {
                        await interaction.RespondAsync("❌ An update check is already in progress.", error: true);
                        return;
                    }
                    
                    steamCheckTaskDelegate = ct => HandleSteam(options.AppId, options.DepotId, options.BranchName, id, ct);
                    await interaction.RespondAsync("✅ The manifest has been queued for handling!");
                })
        ]);
        
        await userInterface.AnnounceAsync("✅ FlexPkg started!");

        var steamQueueTask = ContinuouslyCheckForQueuedSteamTask(ct);
        var updateCheckTask = ContinuouslyCheckForGameUpdate(ct);
        
        await Task.WhenAll(steamQueueTask, updateCheckTask);
    }

    private async Task ContinuouslyCheckForQueuedSteamTask(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            // Wait until we have an actual task
            while (steamCheckTaskDelegate is null)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Delay(100, ct);
            }
            
            // Run the task
            try
            {
                await steamCheckTaskDelegate(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "An error occurred while handling Steam");
                await userInterface.AnnounceAsync("🚨 An error occurred while handling Steam. Please check the logs.");
            }
            
            // Reset the task
            steamCheckTaskDelegate = null;
        }
    }

    private async Task ContinuouslyCheckForGameUpdate(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            ct.ThrowIfCancellationRequested();
            
            var appVersion = await appSource.GetLatestAppVersionAsync(new SteamAppIdentifier(options.AppId, options.DepotId, options.BranchName));
            var steamAppVersion = (SteamAppVersion) appVersion;
            if (!await IsManifestIdInToDatabase(steamAppVersion.ManifestId))
            {
                await userInterface.AnnounceAsync(
                    $"A new game update is detected!\n" +
                    $"App ID: **{options.AppId}**\n" +
                    $"Depot ID: **{options.DepotId}**\n" +
                    $"Branch: **{options.BranchName}**\n" +
                    $"Manifest ID: **{steamAppVersion.ManifestId}**");
                // TODO: This will be a problem if the user doesn't respond
            }
            
            await Task.Delay(TimeSpan.FromMinutes(5.0f), ct);
        }
    }

    private async Task HandleSteam(uint appId, uint depotId, string branchName, ulong? manifestId = null, CancellationToken ct = default)
    {
        IAppVersion appVersion;
        if (!manifestId.HasValue)
        {
            logger.LogInformation("Checking for updates");
            await userInterface.AnnounceAsync("🔄 Checking for updates...");
            var appIdentifier = new SteamAppIdentifier(appId, depotId, branchName);
            appVersion = await appSource.GetLatestAppVersionAsync(appIdentifier);
        }
        else
        {
            appVersion = new SteamAppVersion(appId, depotId, manifestId.Value);
        }
        
        var steamAppVersion = (SteamAppVersion) appVersion;
        
        if (await IsManifestIdInToDatabase(steamAppVersion.ManifestId))
        {
            await userInterface.AnnounceAsync("🎉 The manifest is already in database! No action will be performed.");
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
            "Configure Manifest", 
            $"App ID: **{steamAppVersion.AppId}**\nDepot ID: **{steamAppVersion.DepotId}**\nManifest ID: **{steamAppVersion.ManifestId}**", new[]
        {
            new FormElement("version", "Version"),
            new FormElement("patch_notes", "Patch Notes", true),
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
        
        await PublishNuGetPackage(packageBuilder);
        
        await userInterface.AnnounceAsync("✅ All done. Thank you!");
    }

    private async Task PublishNuGetPackage(PackageBuilder packageBuilder)
    {
        logger.LogInformation("Pushing NuGet package");
        await userInterface.AnnounceAsync("⬆️ Pushing package to NuGet...");
        
#if DEBUG
        if (options.DebugSavePackageToDisk)
        {
            await userInterface.AnnounceAsync("⚠️ DEBUG: Package will be saved to disk instead of uploaded.");
            await using var fileStream = File.Open($"{options.PackageName}.{packageBuilder.Version}.nupkg", FileMode.Create);
            packageBuilder.Save(fileStream);
            return;
        }
#endif
        
        var path = Path.GetTempFileName();
        await using (var fileStream = File.Open(path, FileMode.Create))
            packageBuilder.Save(fileStream);
        
        var repository = Repository.Factory.GetCoreV3(options.NuGetSource);
        var resource = await repository.GetResourceAsync<PackageUpdateResource>();
        
        try
        {
            await resource.Push(
                packagePaths: [ path ],
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
            await userInterface.AnnounceAsync("🚨 An error occurred while pushing NuGet package. Please check the logs.");
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

    private Task<bool> IsManifestIdInToDatabase(ulong manifestId)
        => context.SteamAppManifests.AnyAsync(x => x.Id == manifestId);
}