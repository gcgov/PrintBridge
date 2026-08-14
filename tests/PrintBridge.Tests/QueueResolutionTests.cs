using PrintBridge.Settings;
using Xunit;

namespace PrintBridge.Tests;

public class QueueResolutionTests
{
    private static AppSettings Configured() => new()
    {
        Queues =
        [
            new PrintQueueDefinition { Name = "labels", PrinterName = "ZDesigner GK420d" },
            new PrintQueueDefinition { Name = "front-desk", PrinterName = "HP LaserJet M404" },
        ],
        DefaultQueue = "front-desk",
    };

    [Fact]
    public void EmptyRequestUsesTheDefaultQueue()
    {
        Assert.True(Configured().TryResolveQueue(null, out var queue, out _));
        Assert.Equal("front-desk", queue.Name);
        Assert.Equal("HP LaserJet M404", queue.PrinterName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankRequestUsesTheDefaultQueue(string requested)
    {
        Assert.True(Configured().TryResolveQueue(requested, out var queue, out _));
        Assert.Equal("front-desk", queue.Name);
    }

    [Theory]
    [InlineData("labels")]
    [InlineData("LABELS")]
    [InlineData("  labels  ")]
    public void NamedQueuesMatchCaseInsensitivelyAndTrimmed(string requested)
    {
        Assert.True(Configured().TryResolveQueue(requested, out var queue, out _));
        Assert.Equal("labels", queue.Name);
        Assert.Equal("ZDesigner GK420d", queue.PrinterName);
    }

    [Fact]
    public void UnknownQueueIsReported()
    {
        Assert.False(Configured().TryResolveQueue("nope", out _, out var errorCode));
        Assert.Equal(AppSettings.UnknownQueueCode, errorCode);
    }

    [Fact]
    public void NoQueuesConfiguredIsReported()
    {
        var settings = new AppSettings();
        Assert.False(settings.TryResolveQueue(null, out _, out var errorCode));
        Assert.Equal(AppSettings.NoQueueConfiguredCode, errorCode);
    }

    [Fact]
    public void QueuesWithoutADefaultCannotResolveAnEmptyRequest()
    {
        var settings = Configured();
        settings.DefaultQueue = null;

        Assert.False(settings.TryResolveQueue(null, out _, out var errorCode));
        Assert.Equal(AppSettings.NoQueueConfiguredCode, errorCode);

        // Naming a queue explicitly still works.
        Assert.True(settings.TryResolveQueue("labels", out var queue, out _));
        Assert.Equal("labels", queue.Name);
    }

    [Fact]
    public void ADefaultPointingAtNothingIsAConfigurationProblem()
    {
        var settings = Configured();
        settings.DefaultQueue = "removed-queue";

        Assert.False(settings.TryResolveQueue(null, out _, out var errorCode));
        Assert.Equal(AppSettings.NoQueueConfiguredCode, errorCode);
    }

    [Theory]
    [InlineData("labels", "labels")]
    [InlineData("  labels  ", "labels")]
    [InlineData("Front-Desk", "Front-Desk")]
    [InlineData("queue.1_a-b", "queue.1_a-b")]
    [InlineData("9lives", "9lives")]
    public void ValidQueueNamesNormalize(string input, string expected)
    {
        Assert.True(AppSettings.TryNormalizeQueueName(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-leading-hyphen")]
    [InlineData(".leading-dot")]
    [InlineData("_leading-underscore")]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has:colon")]
    [InlineData("café")]
    public void InvalidQueueNamesAreRejected(string? input)
    {
        Assert.False(AppSettings.TryNormalizeQueueName(input, out _));
    }

    [Fact]
    public void QueueNamesLongerThan64CharactersAreRejected()
    {
        Assert.True(AppSettings.TryNormalizeQueueName(new string('a', 64), out _));
        Assert.False(AppSettings.TryNormalizeQueueName(new string('a', 65), out _));
    }
}
