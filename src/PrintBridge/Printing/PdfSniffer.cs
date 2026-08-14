namespace PrintBridge.Printing;

/// <summary>
/// Cheap content check for uploaded bodies. The API does not require a
/// <c>Content-Type</c> of <c>application/pdf</c>; it looks for the PDF header instead,
/// which the spec allows to be preceded by junk (some producers emit a BOM or blank
/// lines), so the marker is searched for in the first 1024 bytes.
/// </summary>
public static class PdfSniffer
{
    private const int HeaderSearchWindow = 1024;

    private static ReadOnlySpan<byte> Marker => "%PDF-"u8;

    public static bool LooksLikePdf(ReadOnlySpan<byte> content)
    {
        if (content.Length < Marker.Length)
        {
            return false;
        }

        var window = content.Length > HeaderSearchWindow ? content[..HeaderSearchWindow] : content;
        return window.IndexOf(Marker) >= 0;
    }
}
