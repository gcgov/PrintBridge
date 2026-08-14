using PrintBridge.Printing;
using Xunit;

namespace PrintBridge.Tests;

/// <summary>
/// Placement math for one page: PDF page size in points against a printable area in
/// hundredths of an inch. Scaling down is allowed, enlarging is not, and the result
/// is always centered.
/// </summary>
public class PageFittingTests
{
    /// <summary>US Letter: 612 x 792 pt, which is 850 x 1100 hundredths of an inch.</summary>
    private static readonly SizeF Letter = new(612f, 792f);

    /// <summary>Printable area of a typical laser printer on Letter (about 0.17" hardware margins).</summary>
    private static readonly RectangleF LetterPrintable = new(0f, 0f, 816f, 1058f);

    [Fact]
    public void APageSmallerThanTheAreaIsNotEnlarged()
    {
        var placed = PrintService.FitCentered(Letter, new RectangleF(0f, 0f, 1200f, 1500f));

        Assert.Equal(850f, placed.Width, 0.5f);
        Assert.Equal(1100f, placed.Height, 0.5f);
        Assert.Equal(175f, placed.X, 0.5f);
        Assert.Equal(200f, placed.Y, 0.5f);
    }

    [Fact]
    public void AnOversizePageIsScaledDownKeepingItsAspectRatio()
    {
        // A4 (595 x 842 pt) is taller than the Letter printable area, so it shrinks to fit.
        var placed = PrintService.FitCentered(new SizeF(595f, 842f), LetterPrintable);

        Assert.True(placed.Width <= LetterPrintable.Width + 0.5f, "the page is wider than the printable area");
        Assert.True(placed.Height <= LetterPrintable.Height + 0.5f, "the page is taller than the printable area");
        Assert.Equal(1058f, placed.Height, 0.5f);
        Assert.Equal(595f / 842f, placed.Width / placed.Height, 0.001f);
    }

    [Fact]
    public void ThePageIsCenteredInThePrintableArea()
    {
        var area = new RectangleF(0f, 0f, 1000f, 1000f);
        var placed = PrintService.FitCentered(Letter, area);

        Assert.Equal(area.Right - placed.Right, placed.Left - area.Left, 0.5f);
        Assert.Equal(area.Bottom - placed.Bottom, placed.Top - area.Top, 0.5f);
    }

    [Fact]
    public void APrintableAreaThatDoesNotStartAtTheOriginIsRespected()
    {
        var area = new RectangleF(17f, 17f, 1200f, 1500f);
        var placed = PrintService.FitCentered(Letter, area);

        Assert.Equal(17f + 175f, placed.X, 0.5f);
        Assert.Equal(17f + 200f, placed.Y, 0.5f);
        Assert.Equal(850f, placed.Width, 0.5f);
    }

    [Fact]
    public void ALandscapePageFillsALandscapeArea()
    {
        // Landscape Letter (1100 x 850) against the printable area for a landscape page.
        var placed = PrintService.FitCentered(new SizeF(792f, 612f), new RectangleF(0f, 0f, 1058f, 816f));

        Assert.Equal(1056f, placed.Width, 0.5f);
        Assert.Equal(816f, placed.Height, 0.5f);
        Assert.Equal(1f, placed.X, 0.5f);
        Assert.Equal(0f, placed.Y, 0.5f);
    }

    [Fact]
    public void ADegeneratePageSizeFallsBackToTheWholeArea()
    {
        var placed = PrintService.FitCentered(new SizeF(0f, 0f), LetterPrintable);

        Assert.Equal(LetterPrintable, placed);
    }
}
