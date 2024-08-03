namespace FlexPkg.UserInterface.Discord;

public sealed class DiscordPaginatedMessage
{
    public required ulong Id { get; init; }
    public required string Content { get; init; }
    public required IReadOnlyList<UiPage> Pages { get; init; }
    public int CurrentPage { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}