using CommandLine;

namespace FlexPkg;

public sealed class CliOptions
{
    [Option('t', "discord-token", Required = true, HelpText = "Your Discord bot token.")]
    public string DiscordToken { get; set; } = string.Empty;
    
    [Option('g', "guild-id", Required = true, HelpText = "The Discord guild ID for announcements and interactions.")]
    public ulong GuildId { get; set; }
    
    [Option('c', "channel-id", Required = true, HelpText = "The Discord channel ID for announcements and interactions.")]
    public ulong ChannelId { get; set; }
    
    [Option('n', "username", Required = true, HelpText = "Your Steam username.")]
    public string Username { get; set; } = string.Empty;
    
    [Option('p', "password", Required = true, HelpText = "Your Steam password.")]
    public string Password { get; set; } = string.Empty;
    
    [Option('a', "app-id", Required = true, HelpText = "The Steam app ID.")]
    public uint AppId { get; set; }
    
    [Option('d', "depot-id", Required = true, HelpText = "The Steam depot ID.")]
    public uint DepotId { get; set; }
    
    [Option('b', "branches", Required = false, HelpText = "The Steam branches names.")]
    public IEnumerable<string> BranchNames { get; set; } = ["public"];
    
    [Option("package-name", Required = true, HelpText = "The name of the NuGet package.")]
    public string PackageName { get; set; } = string.Empty;
    
    [Option("package-description", Required = true, HelpText = "The description of the NuGet package.")]
    public string PackageDescription { get; set; } = string.Empty;

    [Option("package-authors", Required = true, HelpText = "The authors of the NuGet package.")]
    public IEnumerable<string> PackageAuthors { get; set; } = [];

    [Option("package-project-url", Required = false, HelpText = "The URL to the project. (e.g. source repository)")]
    public string PackageProjectUrl { get; set; } = string.Empty;
    
    [Option("nuget-source", Required = false, HelpText = "The NuGet package source.")]
    public string NuGetSource { get; set; } = "https://api.nuget.org/v3/index.json";
    
    [Option("nuget-api-key", Required = false, HelpText = "The API key for the NuGet package source.")]
    public string NuGetApiKey { get; set; } = string.Empty;
    
#if DEBUG
    [Option("debug-save-package-to-disk", Required = false, HelpText = "Saves the package to disk instead of upload.")]
    public bool DebugSavePackageToDisk { get; set; }
#endif
}