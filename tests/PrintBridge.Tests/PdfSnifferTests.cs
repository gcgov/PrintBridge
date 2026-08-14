using System.Text;
using PrintBridge.Printing;
using Xunit;

namespace PrintBridge.Tests;

public class PdfSnifferTests
{
    [Fact]
    public void PlainPdfHeaderIsAccepted()
    {
        Assert.True(PdfSniffer.LooksLikePdf("%PDF-1.7\n1 0 obj\n"u8));
    }

    [Fact]
    public void HeaderLaterInTheFirstKilobyteIsAccepted()
    {
        var content = new byte[600];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(content, 500);
        Assert.True(PdfSniffer.LooksLikePdf(content));
    }

    [Fact]
    public void HeaderPastTheFirstKilobyteIsRejected()
    {
        var content = new byte[4096];
        Encoding.ASCII.GetBytes("%PDF-1.4").CopyTo(content, 2000);
        Assert.False(PdfSniffer.LooksLikePdf(content));
    }

    [Fact]
    public void EmptyBodyIsRejected()
    {
        Assert.False(PdfSniffer.LooksLikePdf([]));
    }

    [Fact]
    public void OtherFormatsAreRejected()
    {
        Assert.False(PdfSniffer.LooksLikePdf("{\"queue\":\"labels\"}"u8));
        Assert.False(PdfSniffer.LooksLikePdf([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.False(PdfSniffer.LooksLikePdf("%PDF"u8));
    }

    [Fact]
    public void TheBundledTestPageIsRecognized()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "testpage.pdf");
        Assert.True(File.Exists(path), $"test page missing at {path}");
        Assert.True(PdfSniffer.LooksLikePdf(File.ReadAllBytes(path)));
    }
}
