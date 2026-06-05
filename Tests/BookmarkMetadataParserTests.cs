using System;
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
    public void Parse_extracts_icon_candidates_and_resolves_relative_urls()
    {
        const string html = """
            <html>
              <head>
                <title>Title</title>
                <link rel="icon" href="/favicon-32.png" type="image/png" sizes="32x32">
                <link rel="shortcut icon" href="https://cdn.example.com/favicon.ico">
                <link rel="apple-touch-icon" href="touch.png" sizes="180x180">
                <link rel="stylesheet" href="/site.css">
              </head>
            </html>
            """;

        var metadata = _parser.Parse(html, new Uri("https://example.com/articles/page"));

        Assert.Collection(
            metadata.IconCandidates,
            candidate =>
            {
                Assert.Equal(new Uri("https://example.com/favicon-32.png"), candidate.Uri);
                Assert.Equal("icon", candidate.Rel);
                Assert.Equal("image/png", candidate.Type);
                Assert.Equal("32x32", candidate.Sizes);
            },
            candidate =>
            {
                Assert.Equal(new Uri("https://cdn.example.com/favicon.ico"), candidate.Uri);
                Assert.Equal("shortcut icon", candidate.Rel);
                Assert.Null(candidate.Type);
                Assert.Null(candidate.Sizes);
            },
            candidate =>
            {
                Assert.Equal(new Uri("https://example.com/articles/touch.png"), candidate.Uri);
                Assert.Equal("apple-touch-icon", candidate.Rel);
                Assert.Null(candidate.Type);
                Assert.Equal("180x180", candidate.Sizes);
            },
            candidate =>
            {
                Assert.Equal(new Uri("https://example.com/favicon.ico"), candidate.Uri);
                Assert.Equal("fallback", candidate.Rel);
                Assert.Null(candidate.Type);
                Assert.Null(candidate.Sizes);
            });
    }

    [Fact]
    public void Parse_adds_only_fallback_icon_candidate_when_page_uri_is_available()
    {
        const string html = "<html><head><title>Title</title></head></html>";

        var metadata = _parser.Parse(html, new Uri("https://example.com/articles/page"));

        var candidate = Assert.Single(metadata.IconCandidates);
        Assert.Equal(new Uri("https://example.com/favicon.ico"), candidate.Uri);
        Assert.Equal("fallback", candidate.Rel);
    }

    [Fact]
    public void Parse_does_not_add_icon_candidates_without_page_uri()
    {
        const string html = """
            <html>
              <head>
                <title>Title</title>
                <link rel="icon" href="/favicon.png">
              </head>
            </html>
            """;

        var metadata = _parser.Parse(html);

        Assert.Empty(metadata.IconCandidates);
    }

    [Fact]
    public void ParseIconCandidatesFromPartialHtml_extracts_icon_candidates_without_full_html_parse()
    {
        const string html = """
            <html>
              <head>
                <link href="/early-icon.png" sizes="64x64" rel="icon" type="image/png">
                <link rel='apple-touch-icon-precomposed' href='touch.png'>
                <link rel="stylesheet" href="/site.css">
            """;

        var candidates = BookmarkMetadataParser.ParseIconCandidatesFromPartialHtml(
            html,
            new Uri("https://example.com/articles/page"));

        Assert.Collection(
            candidates,
            candidate =>
            {
                Assert.Equal(new Uri("https://example.com/early-icon.png"), candidate.Uri);
                Assert.Equal("icon", candidate.Rel);
                Assert.Equal("image/png", candidate.Type);
                Assert.Equal("64x64", candidate.Sizes);
            },
            candidate =>
            {
                Assert.Equal(new Uri("https://example.com/articles/touch.png"), candidate.Uri);
                Assert.Equal("apple-touch-icon-precomposed", candidate.Rel);
            },
            candidate =>
            {
                Assert.Equal(new Uri("https://example.com/favicon.ico"), candidate.Uri);
                Assert.Equal("fallback", candidate.Rel);
            });
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
