using PrintBridge.Settings;
using Xunit;

namespace PrintBridge.Tests;

public class OriginAllowlistTests
{
    [Theory]
    [InlineData("https://apps.example.gov", "https://apps.example.gov")]
    [InlineData("https://Apps.Example.GOV", "https://apps.example.gov")]
    [InlineData("https://apps.example.gov/", "https://apps.example.gov")]
    [InlineData("  https://apps.example.gov  ", "https://apps.example.gov")]
    [InlineData("https://localhost:8097", "https://localhost:8097")]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("https://apps.example.gov:443", "https://apps.example.gov")]
    [InlineData("http://apps.example.gov:80", "http://apps.example.gov")]
    public void ValidOriginsNormalize(string input, string expected)
    {
        Assert.True(AppSettings.TryNormalizeOrigin(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("apps.example.gov")]
    [InlineData("ftp://apps.example.gov")]
    [InlineData("https://apps.example.gov/lab/app")]
    [InlineData("https://apps.example.gov?x=1")]
    [InlineData("https://user:pass@apps.example.gov")]
    [InlineData("not a url")]
    public void InvalidOriginsAreRejected(string? input)
    {
        Assert.False(AppSettings.TryNormalizeOrigin(input, out _));
    }
}
