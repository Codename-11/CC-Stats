using CCStats.Core.Models;

namespace CCStats.Tests;

public class RateLimitTierFromStringTests
{
    [Theory]
    [InlineData("default_claude_pro", RateLimitTier.Pro)]
    [InlineData("default_claude_max_5x", RateLimitTier.Max5x)]
    [InlineData("default_claude_max_20x", RateLimitTier.Max20x)]
    public void FromString_ExactApiValues_ReturnsCorrectTier(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData("claude_pro", RateLimitTier.Pro)]
    [InlineData("pro", RateLimitTier.Pro)]
    public void FromString_ProAliases_ReturnsPro(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData("claude_max_5x", RateLimitTier.Max5x)]
    [InlineData("max_5x", RateLimitTier.Max5x)]
    [InlineData("max5x", RateLimitTier.Max5x)]
    [InlineData("max 5x", RateLimitTier.Max5x)]
    public void FromString_Max5xAliases_ReturnsMax5x(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData("claude_max_20x", RateLimitTier.Max20x)]
    [InlineData("max_20x", RateLimitTier.Max20x)]
    [InlineData("max20x", RateLimitTier.Max20x)]
    [InlineData("max 20x", RateLimitTier.Max20x)]
    public void FromString_Max20xAliases_ReturnsMax20x(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData("claude_enterprise", RateLimitTier.Max20x)]
    [InlineData("enterprise", RateLimitTier.Max20x)]
    public void FromString_Enterprise_MapsToMax20x(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData("claude_team", RateLimitTier.Max5x)]
    [InlineData("team", RateLimitTier.Max5x)]
    public void FromString_Team_MapsToMax5x(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    [InlineData("garbage")]
    [InlineData("claude_max")]
    public void FromString_UnrecognizedOrNull_ReturnsUnknown(string? input)
    {
        Assert.Equal(RateLimitTier.Unknown, RateLimitTierExtensions.FromString(input));
    }

    [Theory]
    [InlineData("DEFAULT_CLAUDE_PRO", RateLimitTier.Pro)]
    [InlineData("Default_Claude_Max_5x", RateLimitTier.Max5x)]
    [InlineData("PRO", RateLimitTier.Pro)]
    [InlineData("ENTERPRISE", RateLimitTier.Max20x)]
    [InlineData("TEAM", RateLimitTier.Max5x)]
    public void FromString_CaseInsensitive(string input, RateLimitTier expected)
    {
        Assert.Equal(expected, RateLimitTierExtensions.FromString(input));
    }
}

public class RateLimitTierDisplayNameTests
{
    [Theory]
    [InlineData(RateLimitTier.Pro, "Pro ($20)")]
    [InlineData(RateLimitTier.Max5x, "Max 5x ($100)")]
    [InlineData(RateLimitTier.Max20x, "Max 20x ($200)")]
    [InlineData(RateLimitTier.Unknown, "Free")]
    public void DisplayName_ReturnsExpectedLabel(RateLimitTier tier, string expected)
    {
        Assert.Equal(expected, tier.DisplayName());
    }
}

public class RateLimitTierCreditLimitsTests
{
    [Fact]
    public void GetCreditLimits_Pro_ReturnsCorrectValues()
    {
        var limits = RateLimitTier.Pro.GetCreditLimits();

        Assert.Equal(550_000, limits.FiveHourCredits);
        Assert.Equal(5_000_000, limits.SevenDayCredits);
        Assert.Equal(20.0, limits.MonthlyPrice);
    }

    [Fact]
    public void GetCreditLimits_Max5x_ReturnsCorrectValues()
    {
        var limits = RateLimitTier.Max5x.GetCreditLimits();

        Assert.Equal(3_300_000, limits.FiveHourCredits);
        Assert.Equal(41_670_000, limits.SevenDayCredits);
        Assert.Equal(100.0, limits.MonthlyPrice);
    }

    [Fact]
    public void GetCreditLimits_Max20x_ReturnsCorrectValues()
    {
        var limits = RateLimitTier.Max20x.GetCreditLimits();

        Assert.Equal(11_000_000, limits.FiveHourCredits);
        Assert.Equal(83_330_000, limits.SevenDayCredits);
        Assert.Equal(200.0, limits.MonthlyPrice);
    }

    [Fact]
    public void GetCreditLimits_Unknown_ReturnsZerosAndNullPrice()
    {
        var limits = RateLimitTier.Unknown.GetCreditLimits();

        Assert.Equal(0, limits.FiveHourCredits);
        Assert.Equal(0, limits.SevenDayCredits);
        Assert.Null(limits.MonthlyPrice);
    }
}
