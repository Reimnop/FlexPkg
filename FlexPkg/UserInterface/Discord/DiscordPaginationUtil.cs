using Discord;

namespace FlexPkg.UserInterface.Discord;

public static class DiscordPaginationUtil
{
    public const string FirstPageButtonId = "first_page";
    public const string PreviousPageButtonId = "previous_page";
    public const string NextPageButtonId = "next_page";
    public const string LastPageButtonId = "last_page";
    
    public static Embed GetUiPageEmbed(UiPage page, int currentPageIndex, int pageCount)
    {
        var embedBuilder = new EmbedBuilder()
            .WithTitle(page.Title)
            .WithDescription(page.Content)
            .WithFooter(new EmbedFooterBuilder().WithText($"{currentPageIndex + 1} / {pageCount}"));

        if (page.Sections is not null)
            foreach (var section in page.Sections)
                embedBuilder.AddField(section.Title, section.Content);
        
        return embedBuilder.Build();
    }

    public static MessageComponent GetPaginationControls(int currentPageIndex, int pageCount)
    {
        var componentBuilder = new ComponentBuilder()
            .WithButton("⏮️", FirstPageButtonId, ButtonStyle.Secondary, disabled: currentPageIndex == 0)
            .WithButton("◀️", PreviousPageButtonId, ButtonStyle.Secondary, disabled: currentPageIndex == 0)
            .WithButton("▶️", NextPageButtonId, ButtonStyle.Secondary, disabled: currentPageIndex == pageCount - 1)
            .WithButton("⏭️", LastPageButtonId, ButtonStyle.Secondary, disabled: currentPageIndex == pageCount - 1);
        return componentBuilder.Build();
    }
}