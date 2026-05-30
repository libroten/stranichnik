using Stranichnik.Search.Internal;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class SearchTextNormalizerTests
{
    [Fact]
    public void NormalizeConvertsEnglishTextToLowercase()
    {
        string normalized = SearchTextNormalizer.Normalize("Avalonia DOCS");

        Assert.Equal("avalonia docs", normalized);
    }

    [Fact]
    public void NormalizeConvertsRussianTextToLowercase()
    {
        string normalized = SearchTextNormalizer.Normalize("РУССКИЙ ТЕКСТ");

        Assert.Equal("русский текст", normalized);
    }

    [Fact]
    public void NormalizeTreatsRussianYoAsYe()
    {
        string first = SearchTextNormalizer.Normalize("ёлка");
        string second = SearchTextNormalizer.Normalize("елка");

        Assert.Equal(second, first);
    }

    [Fact]
    public void NormalizeCollapsesRepeatedWhitespace()
    {
        string normalized = SearchTextNormalizer.Normalize("  Avalonia \t  Docs \n Search  ");

        Assert.Equal("avalonia docs search", normalized);
    }

    [Fact]
    public void NormalizeUsesCompatibilityNormalization()
    {
        string normalized = SearchTextNormalizer.Normalize("ＡＢＣ １２３");

        Assert.Equal("abc 123", normalized);
    }

    [Fact]
    public void NormalizeHandlesMixedEnglishAndRussianText()
    {
        string normalized = SearchTextNormalizer.Normalize("Avalonia Документация");

        Assert.Equal("avalonia документация", normalized);
    }

    [Fact]
    public void NormalizeReturnsEmptyStringForNullInput()
    {
        string normalized = SearchTextNormalizer.Normalize(null);

        Assert.Equal(string.Empty, normalized);
    }

    [Fact]
    public void NormalizeReturnsEmptyStringForWhitespaceInput()
    {
        string normalized = SearchTextNormalizer.Normalize(" \t\n ");

        Assert.Equal(string.Empty, normalized);
    }
}
