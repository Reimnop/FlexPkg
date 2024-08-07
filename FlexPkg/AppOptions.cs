namespace FlexPkg;

public class AppOptions
{
    public required string DbPath { get; set; }
    public required DiscordOptions Discord { get; set; }
    public required SteamOptions Steam { get; set; }
    public required PackageOptions Package { get; set; }
    public required NuGetOptions NuGet { get; set; }
    
#if DEBUG
    public required bool DebugSavePackageToDisk { get; set; }
#endif
    
    public class DiscordOptions
    {
        public required string Token { get; set; }
        public required ulong GuildId { get; set; }
        public required ulong ChannelId { get; set; }
        public string? WebhookUrl { get; set; }
        public string? WebhookName { get; set; }
        public string? WebhookAvatarUrl { get; set; }
    }

    public class SteamOptions
    {
        public required string UserName { get; set; }
        public required string Password { get; set; }
        public required uint AppId { get; set; }
        public required uint DepotId { get; set; }
        public required List<string> BranchNames { get; set; }
    }

    public class PackageOptions
    {
        public required string Name { get; set; }
        public required string Description { get; set; }
        public required List<string> Authors { get; set; }
        public string? ProjectUrl { get; set; }
        public string? IconPath { get; set; }
    }

    public class NuGetOptions
    {
        public required string Source { get; set; }
        public required string ApiKey { get; set; }
    }
}