using System.IO.Compression;
using System.Text;
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

public sealed class App(
    CliOptions options,
    FlexPkgContext context,
    IAppSource appSource,
    IUserInterface userInterface,
    ILogger<App> logger)
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
                    await interaction.DelayResponseAsync();

                    var updates = await CheckForAppUpdates();
                    if (updates.Count == 0)
                    {
                        await interaction.RespondAsync(GetUpdatesMessage([]));
                        return;
                    }

                    await AddUpdatesToDatabase(updates, ct);
                    await interaction.RespondAsync(GetUpdatesMessage(updates));

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
                        UiCommandParameterType.String),
                    new UiCommandParameter(
                        "branch",
                        "Branch",
                        "The branch of the manifest.",
                        UiCommandParameterType.String)
                ],
                async (_, interaction) =>
                {
                    if (!ulong.TryParse(interaction.Arguments["id"] as string, out var id))
                    {
                        await interaction.RespondAsync("❌ Invalid manifest ID.", error: true);
                        return;
                    }

                    if (interaction.Arguments["branch"] is not string branch || !options.BranchNames.Contains(branch))
                    {
                        var branches = string.Join(", ", options.BranchNames.Select(b => $"`{b}`"));
                        await interaction.RespondAsync($"❌ Invalid branch name. ({branches})", error: true);
                        return;
                    }
                    
                    if (steamCheckTaskDelegate is not null)
                    {
                        await interaction.RespondAsync("❌ A Steam handler job is already in progress.", error: true);
                        return;
                    }
                    
                    steamCheckTaskDelegate = ct =>
                        HandleSteam(new SteamAppVersion(options.AppId, options.DepotId, branch, id), ct);
                    await interaction.RespondAsync("✅ The manifest has been queued for handling!");
                }),
            new UiCommand(
                "listmanifests",
                "List Manifests",
                "Lists the manifests in the database.",
                [],
                async (_, interaction) =>
                {
                    var manifests = await context.SteamAppManifests
                        .AsNoTracking()
                        .OrderByDescending(m => m.CreatedAt)
                        .ToListAsync(ct);

                    var pages = await manifests.Chunk(3).ToAsyncEnumerable().SelectAwait(async c => new UiPage(
                        "Manifests",
                        string.Empty,
                        await c.ToAsyncEnumerable().SelectAwait(async m =>
                        {
                            var previousBranches = await context.SteamAppManifests
                                .AsNoTracking()
                                .Where(pm =>
                                    pm.Id == m.Id &&
                                    pm.BranchName != m.BranchName &&
                                    pm.CreatedAt < m.CreatedAt)
                                .OrderByDescending(pm => pm.CreatedAt)
                                .ToListAsync(ct);
                            
                            var titleMarker = m.Handled? "🟩" : previousBranches.Count > 0 ? "🟨" : "🟥";
                            var title = $"{titleMarker} {m.Id} (`{m.BranchName}`)";
                            
                            var builder = new StringBuilder();
                            
                            if (m.Handled)
                                builder.Append($"▫️ Version: **{m.Version}**\n");
                            else
                                builder.Append("▫️ Version: *(not handled)*\n");

                            builder.Append($"▫️ Created At: {
                                TimestampTag.FromDateTime(m.CreatedAt, TimestampTagStyles.ShortDateTime)
                            }\n");

                            if (previousBranches.Count > 0)
                                builder.Append($"▫️ Previous Branches: {string.Join(", ", previousBranches.Select(
                                    b => $"`{b.BranchName}` ({
                                        TimestampTag.FromDateTime(b.CreatedAt, TimestampTagStyles.ShortDate)
                                    })"))}\n");
                            
                            if (m.Handled)
                                if (string.IsNullOrWhiteSpace(m.PatchNotes))
                                    builder.Append("▫️ Patch Notes: *(none)*");
                                else
                                    builder.Append($"▫️ Patch Notes:\n`{TruncateString(RegexUtils.StripLines()
                                        .Replace(m.PatchNotes, "\n"), 180)}`");

                            return new UiPageSection(title, builder.ToString());
                        }).ToListAsync(ct))
                    ).ToListAsync(ct);

                    await interaction.RespondPaginatedAsync(string.Empty, pages);
                }),
            new UiCommand(
                "steamdb",
                "SteamDB",
                "Open the SteamDB page for the configured depot.",
                [],
                async (_, interaction) =>
                {
                    await interaction.RespondAsync(
                        $"🔗 SteamDB: <https://steamdb.info/depot/{options.DepotId}/>", 
                        error: true);
                })
        ]);
        
        await userInterface.AnnounceAsync("✅ FlexPkg started!");

        var steamQueueTask = ContinuouslyCheckForQueuedSteamTask(ct);
        var updateCheckTask = ContinuouslyCheckForGameUpdate(ct);
        
        await Task.WhenAll(steamQueueTask, updateCheckTask);
    }

    private static string TruncateString(string value, int maxLength) =>
        value.Length <= maxLength
            ? value
            : value[..maxLength] + "...";

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
            
            var updates = await CheckForAppUpdates();
            if (updates.Count > 0)
            {
                await AddUpdatesToDatabase(updates, ct);
                await userInterface.AnnounceAsync(GetUpdatesMessage(updates));
            }
            
            await Task.Delay(TimeSpan.FromMinutes(5.0f), ct);
        }
    }

    private async Task<List<SteamAppUpdate>> CheckForAppUpdates()
    {
        var appVersions =
            await appSource.GetLatestAppVersionsAsync(
                new SteamAppIdentifier(options.AppId, options.DepotId, options.BranchNames));

        return (await appVersions
            .Cast<SteamAppVersion>()
            .ToAsyncEnumerable()
            .SelectAwait(async v => await GetUpdateFromVersion(v))
            .Where(v => v is not null)
            .ToListAsync())!;
    }

    private async Task<SteamAppUpdate?> GetUpdateFromVersion(SteamAppVersion version)
    {
        var existingManifest = await context.SteamAppManifests.FindAsync(version.ManifestId, version.BranchName);
        if (existingManifest is not null)
            return null;

        var existingBranches = await context.SteamAppManifests
            .AsNoTracking()
            .Where(b => b.Id == version.ManifestId && b.BranchName != version.BranchName)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();
        
        return new SteamAppUpdate
        {
            Version = version,
            ExistingBranches = existingBranches
        };
    }

    private async Task AddUpdatesToDatabase(List<SteamAppUpdate> updates, CancellationToken ct)
    {
        await context.SteamAppManifests.AddRangeAsync(updates.Select(u => new SteamAppManifest
        {
            Id = u.Version.ManifestId,
            BranchName = u.Version.BranchName,
            Handled = false
        }), ct);
        await context.SaveChangesAsync(ct);
    }
    
    private static string GetUpdatesMessage(List<SteamAppUpdate> updates)
    {
        if (updates.Count == 0)
            return "✅ The latest app versions are already fetched!";
        
        var builder = new StringBuilder("🔔 New app updates are detected!\n\n");
        foreach (var update in updates)
            if (update.ExistingBranches.Count == 0)
                builder.Append(
                    $"🟩 Manifest ID: **{update.Version.ManifestId}**\n" +
                    $"▫️ Branch: `{update.Version.BranchName}`\n\n");
            else
                builder.Append(
                    $"🟨 Manifest ID: **{update.Version.ManifestId}**\n" +
                    $"▫️ Branch: **`{update.Version.BranchName}`**\n" +
                    $"▫️ Existing Branches: {string.Join(", ",
                        update.ExistingBranches.Select(b => $"`{b.BranchName}` ({
                            TimestampTag.FromDateTime(b.CreatedAt, TimestampTagStyles.ShortDate)
                        })"))}\n\n");

        return builder.ToString();
    }

    private async Task HandleSteam(IAppVersion appVersion, CancellationToken ct = default)
    {
        var steamAppVersion = (SteamAppVersion)appVersion;
        
        var existingManifest = await TryGetManifestFromDatabase(steamAppVersion.ManifestId, steamAppVersion.BranchName);
        if (existingManifest is not null && existingManifest.Handled)
        {
            await userInterface.AnnounceAsync("🎉 The manifest is already handled! No action will be performed.");
            return;
        }

        if (existingManifest is null)
        {
            // Add it
            logger.LogInformation(
                "Adding manifest {ManifestId} ({BranchName}) to the database",
                steamAppVersion.ManifestId,
                steamAppVersion.BranchName);
            
            await context.SteamAppManifests.AddAsync(new SteamAppManifest
            {
                Id = steamAppVersion.ManifestId,
                BranchName = steamAppVersion.BranchName,
                Handled = false
            }, ct);
            
            await context.SaveChangesAsync(ct);
        }
        
        // Open form
        var form = new Form(
            "Configure Manifest", // TODO: remove app and depot ids, they're already known
            $"App ID: **{steamAppVersion.AppId}**\n" +
            $"Depot ID: **{steamAppVersion.DepotId}**\n" +
            $"Manifest ID: **{steamAppVersion.ManifestId}**\n" +
            $"Branch: `{steamAppVersion.BranchName}`", new[]
            {
                new FormElement("version", "Version"),
                new FormElement("patch_notes", "Patch Notes", true),
            });
        
        var response = await userInterface.PromptFormAsync(form);
        if (response is null)
            return;
        
        // Save the manifest
        logger.LogInformation("Updating the manifest with the new version");
        var manifest =
            await context.SteamAppManifests.FindAsync([steamAppVersion.ManifestId, steamAppVersion.BranchName], ct);
        if (manifest is null)
        {
            logger.LogError("The manifest is not found in the database");
            return;
        }
        
        manifest.Version = response.Values["version"];
        manifest.PatchNotes = response.Values["patch_notes"];
        await context.SaveChangesAsync(ct);

        await userInterface.AnnounceAsync("📦 Downloading manifest...");
        
        logger.LogInformation("Cleaning up old files");
        if (Directory.Exists(DownloadPath))
            Directory.Delete(DownloadPath, true);
        if (Directory.Exists(Cpp2IlOutputPath))
            Directory.Delete(Cpp2IlOutputPath, true);
        
        logger.LogInformation("Downloading manifest {ManifestId}", steamAppVersion.ManifestId);
        await appSource.DownloadAppAsync("output", appVersion);
        
        await userInterface.AnnounceAsync("🎉 The manifest has been downloaded and stored!");
        
        // Run Cpp2IL on it
        logger.LogInformation("Running Cpp2IL on manifest {ManifestId}", steamAppVersion.ManifestId);
        await userInterface.AnnounceAsync("🔧 Running Cpp2IL on the manifest...");

        // Since Cpp2Il is a singleton, we need to lock it
        int[]? unityVersion;
        List<AssemblyDefinition> dummyDlls;
        lock (Cpp2IlLock)
        {
            logger.LogInformation("Cpp2IL instance has been locked");
            
            var unityDataDirPath = Directory.GetDirectories(DownloadPath, "*_Data", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (unityDataDirPath is null)
            {
                logger.LogError("Unity data directory is not found");
                return;
            }

            var unityGameExePath = $"{unityDataDirPath[..^5]}.exe";
            if (!File.Exists(unityGameExePath))
            {
                logger.LogError("Unity game executable is not found");
                return;
            }
            
            unityVersion = Cpp2IlApi.DetermineUnityVersion(unityGameExePath, unityDataDirPath);
            if (unityVersion is null)
            {
                logger.LogError("Could not determine the Unity version");
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
        
        await userInterface.AnnounceAsync("🎉 App has been successfully decompiled!");
        
        logger.LogInformation("Building NuGet package");
        await userInterface.AnnounceAsync("📦 Building NuGet package...");
        var packageBuilder = new PackageBuilder
        {
            Id = options.PackageName,
            Description = options.PackageDescription,
            Version = new NuGetVersion(manifest.Version),
        };

        packageBuilder.Authors.AddRange(options.PackageAuthors);
        packageBuilder.ReleaseNotes = manifest.PatchNotes;
        packageBuilder.DependencyGroups.Add(
            new PackageDependencyGroup(targetFramework: NuGetFramework.Parse("netstandard2.0"), packages: []));
        
        if (!string.IsNullOrWhiteSpace(options.PackageProjectUrl))
            packageBuilder.ProjectUrl = new Uri(options.PackageProjectUrl);

        if (!string.IsNullOrWhiteSpace(options.PackageIcon))
        {
            packageBuilder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = options.PackageIcon,
                TargetPath = "icon.png",
            });
            packageBuilder.Icon = "icon.png";
        }

        foreach (var file in Directory.GetFiles(Cpp2IlOutputPath))
        {
            packageBuilder.Files.Add(new PhysicalPackageFile
            {
                SourcePath = file,
                TargetPath = $"lib/netstandard2.0/{Path.GetFileName(file)}",
            });
        }
        
        await PublishNuGetPackage(packageBuilder);
        
        // Flag manifest as handled
        manifest.Handled = true;
        await context.SaveChangesAsync(ct);
        
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
                packagePaths: [path],
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
                "🚨 An error occurred while pushing NuGet package. Please check the logs.");
        }
        
        File.Delete(path);
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

    private ValueTask<SteamAppManifest?> TryGetManifestFromDatabase(ulong manifestId, string branchName)
        => context.SteamAppManifests.FindAsync(manifestId, branchName);
}