using Shapers.Content.Application;

namespace Shapers.Content.Tests;

public sealed class WordPressConversionTests
{
    [Fact]
    public void Scripts_players_and_comments_are_removed_and_the_rest_becomes_markdown()
    {
        const string html = """
            <h2>Impossible is a lie</h2>
            <p>God is <strong>able</strong>. Read <a href="https://www.bible.com/bible/1/LUK.1.37">Luke 1:37</a>.</p>
            <!-- wp:paragraph -->
            <script>alert('x')</script>
            <div class="powerpress_player"><audio class="wp-audio-shortcode" preload="none"><source src="x.mp3"></audio></div>
            <p><img src="http://shaperschurch.com/wp-content/uploads/2020/07/cover.jpg" alt="Cover"></p>
            <ul><li>Believe</li><li>Act</li></ul>
            """;

        var markdown = WordPressImporter.ToMarkdown(html);

        Assert.Contains("## Impossible is a lie", markdown, StringComparison.Ordinal);
        Assert.Contains("**able**", markdown, StringComparison.Ordinal);
        Assert.Contains("[Luke 1:37](https://www.bible.com/bible/1/LUK.1.37)", markdown, StringComparison.Ordinal);
        Assert.Contains("https://shaperschurch.com/wp-content/uploads/2020/07/cover.jpg", markdown, StringComparison.Ordinal);
        Assert.Contains("- Believe", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("audio", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("wp:paragraph", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Page_builder_codes_become_headings_and_images_and_the_rest_disappears()
    {
        // As the old giving page arrives from WordPress: builder codes with encoded, curled quotes.
        const string html = "[vc_row][vc_column width=&#8221;1/2&#8243;][vc_custom_heading text=&#8221;SNAPSCAN&#8221; font_container=&#8221;tag:h3|text_align:center&#8221;]"
            + "[vc_single_image image=&#8221;148&#8243; img_size=&#8221;medium&#8221;][vc_column_text]Scan to give.[/vc_column_text][/vc_column][/vc_row]";

        Assert.Equal([148], WordPressImporter.ImageIds(html));
        var markdown = WordPressImporter.ToMarkdown(html, new Dictionary<int, string> { [148] = "https://shaperschurch.com/wp-content/uploads/2020/10/snapscan.png" });

        Assert.Contains("### SNAPSCAN", markdown, StringComparison.Ordinal);
        Assert.Contains("![](https://shaperschurch.com/wp-content/uploads/2020/10/snapscan.png)", markdown, StringComparison.Ordinal);
        Assert.Contains("Scan to give.", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("vc_", markdown, StringComparison.Ordinal);
    }
}
