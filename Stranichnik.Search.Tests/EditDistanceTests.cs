using Stranichnik.Search.Internal;
using Xunit;

namespace Stranichnik.Search.Tests;

public sealed class EditDistanceTests
{
    [Fact]
    public void CalculateBoundedReturnsZeroForEqualStrings()
    {
        int distance = EditDistance.CalculateBounded("avalonia", "avalonia", maximumDistance: 2);

        Assert.Equal(0, distance);
    }

    [Fact]
    public void CalculateBoundedReturnsTwoForTransposedCharacters()
    {
        int distance = EditDistance.CalculateBounded("avlaonia", "avalonia", maximumDistance: 2);

        Assert.Equal(2, distance);
    }

    [Fact]
    public void CalculateBoundedStopsAboveMaximumDistance()
    {
        int distance = EditDistance.CalculateBounded("abc", "xyz", maximumDistance: 1);

        Assert.True(distance > 1);
    }

    [Theory]
    [InlineData("ab", 0)]
    [InlineData("abc", 1)]
    [InlineData("abcde", 1)]
    [InlineData("abcdef", 2)]
    public void GetMaximumDistanceUsesTokenLength(string token, int expectedDistance)
    {
        int distance = EditDistance.GetMaximumDistance(token);

        Assert.Equal(expectedDistance, distance);
    }
}
