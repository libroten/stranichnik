using Stranichnik.Opening;
using Xunit;

namespace Stranichnik.Tests;

public sealed class BookmarkMetadataParserTests
{
    private readonly BookmarkMetadataParser _parser = new();

    [Fact]
    public void Parse_prefers_open_graph_title()
    {
        const string html = """
            <html>
              <head>
                <title>Fallback title</title>
                <meta name="twitter:title" content="Twitter title">
                <meta property="og:title" content="Open Graph title">
              </head>
            </html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Equal("Open Graph title", metadata.Title);
    }

    [Fact]
    public void Parse_prefers_twitter_title_over_document_title()
    {
        const string html = """
            <html>
              <head>
                <title>Fallback title</title>
                <meta name="twitter:title" content="Twitter title">
              </head>
            </html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Equal("Twitter title", metadata.Title);
    }

    [Fact]
    public void Parse_uses_document_title_as_fallback()
    {
        const string html = """
            <html>
              <head>
                <title>Document title</title>
              </head>
            </html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Equal("Document title", metadata.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><head></head><body>No title</body></html>")]
    [InlineData("<html><head><title>   </title></head></html>")]
    [InlineData("<meta property=\"og:title\" content=\" \">")]
    public void Parse_returns_no_title_when_metadata_is_missing_or_empty(string html)
    {
        var metadata = _parser.Parse(html);

        Assert.Null(metadata.Title);
    }

    [Fact]
    public void Parse_decodes_entities_and_collapses_whitespace()
    {
        const string html = """
            <html>
              <head>
                <meta property="og:title" content=" Hello&#xA;  &amp;   welcome ">
              </head>
            </html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Equal("Hello & welcome", metadata.Title);
    }

    [Fact]
    public void Parse_handles_attribute_order_and_casing()
    {
        const string html = """
            <HTML>
              <HEAD>
                <META content="Case tolerant title" PROPERTY="og:title">
              </HEAD>
            </HTML>
            """;

        var metadata = _parser.Parse(html);

        Assert.Equal("Case tolerant title", metadata.Title);
    }

    [Fact]
    public void ParseTitleTagOnly_extracts_title_from_partial_html()
    {
        const string html = """
            <html>
              <head>
                <meta property="og:title" content="Ignored title">
                <title data-test="value"> Partial &amp; title </title>
              </head>
            """;

        var metadata = BookmarkMetadataParser.ParseTitleTagOnly(html);

        Assert.Equal("Partial & title", metadata.Title);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html><head><title>Missing closing tag")]
    [InlineData("<html><head><title>   </title></head>")]
    public void ParseTitleTagOnly_returns_no_title_when_title_tag_is_missing_or_empty(string html)
    {
        var metadata = BookmarkMetadataParser.ParseTitleTagOnly(html);

        Assert.Null(metadata.Title);
    }
}
