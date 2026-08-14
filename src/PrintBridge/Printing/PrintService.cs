using System.ComponentModel;
using System.Drawing.Printing;
using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using PDFtoImage;
using PDFtoImage.Exceptions;

namespace PrintBridge.Printing;

/// <summary>
/// The only class that talks to PDFium (via PDFtoImage) and to
/// <see cref="System.Drawing.Printing"/>. Renders each PDF page at the printer's own
/// resolution and hands it to the Windows spooler with no user interaction:
/// <see cref="StandardPrintController"/> is what makes the print silent — the default
/// controller would pop up a progress dialog.
/// </summary>
public sealed class PrintService
{
    /// <summary>Rasterization bounds. Below 150 dpi text looks ragged; above 600 the bitmaps get huge.</summary>
    private const int MinRenderDpi = 150;

    private const int MaxRenderDpi = 600;

    private const int FallbackRenderDpi = 300;

    private readonly ILogger _logger;

    public PrintService(ILogger logger)
    {
        _logger = logger;
    }

    /// <summary>Windows printers installed for the current user, in driver order.</summary>
    public static List<string> ListInstalledPrinters() =>
        PrinterSettings.InstalledPrinters.Cast<string>().ToList();

    /// <summary>
    /// Renders and spools the job. Completing means the spooler accepted the document —
    /// what happens after that (paper jams, an offline printer) is not visible here.
    /// </summary>
    public Task PrintAsync(PrintRequest request, IProgress<int> pageProgress, CancellationToken cancellationToken) =>
        // PrintDocument.Print() blocks until the whole document is spooled.
        Task.Run(() => Print(request, pageProgress, cancellationToken), cancellationToken);

    private void Print(PrintRequest request, IProgress<int> pageProgress, CancellationToken cancellationToken)
    {
        var pdfBytes = request.PdfBytes;
        IList<SizeF> pageSizes;
        try
        {
            // Doubles as the parse check: a document PDFium cannot open never gets here.
            pageSizes = Conversion.GetPageSizes(pdfBytes);
        }
        catch (Exception ex) when (IsPdfFailure(ex))
        {
            throw new PrintBridgeException(PrintErrorCodes.InvalidDocument,
                "The document could not be read as a PDF.", ex);
        }

        var pageCount = pageSizes.Count;
        if (pageCount == 0)
        {
            throw new PrintBridgeException(PrintErrorCodes.InvalidDocument, "The PDF contains no pages.");
        }

        using var document = new PrintDocument();
        try
        {
            document.PrinterSettings.PrinterName = request.PrinterName;
            if (!document.PrinterSettings.IsValid)
            {
                throw new PrintBridgeException(PrintErrorCodes.PrinterUnavailable,
                    $"The printer for queue '{request.Queue}' is not available.");
            }
        }
        catch (InvalidPrinterException ex)
        {
            throw new PrintBridgeException(PrintErrorCodes.PrinterUnavailable,
                $"The printer for queue '{request.Queue}' is not available.", ex);
        }

        document.DocumentName = $"PrintBridge — {request.Queue}";
        // Silent printing: the default controller shows a progress dialog.
        document.PrintController = new StandardPrintController();
        // Origin at the top-left of the printable area, which is what GetPrintableArea measures.
        document.OriginAtMargins = false;

        var renderDpi = ResolveRenderDpi(document.PrinterSettings);
        var driverCopies = ApplyCopies(document.PrinterSettings, request.Copies);
        var passes = request.Copies / driverCopies;

        var pageIndex = 0;
        var pagesRendered = 0;
        Exception? pageFailure = null;

        document.QueryPageSettings += (_, e) =>
        {
            if (pageIndex < pageSizes.Count)
            {
                // Per-page orientation, taken from the PDF page itself.
                e.PageSettings.Landscape = pageSizes[pageIndex].Width > pageSizes[pageIndex].Height;
            }
        };

        document.PrintPage += (_, e) =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var page = RenderPage(pdfBytes, pageIndex, renderDpi);
                if (e.Graphics is { } graphics)
                {
                    graphics.DrawImage(page.Image, FitCentered(pageSizes[pageIndex], GetPrintableArea(e, graphics)));
                }

                pageIndex++;
                pagesRendered++;
                pageProgress.Report(pagesRendered * driverCopies);
                e.HasMorePages = pageIndex < pageCount;
            }
            catch (Exception ex)
            {
                // Throwing out of the event handler would surface as a wrapped GDI+
                // error; stop the document and rethrow the real exception below.
                pageFailure = ex;
                e.Cancel = true;
                e.HasMorePages = false;
            }
        };

        _logger.LogInformation(
            "Printing {Pages} page(s) x{Copies} to queue {Queue} at {Dpi} dpi",
            pageCount, request.Copies, request.Queue, renderDpi);

        for (var pass = 0; pass < passes; pass++)
        {
            pageIndex = 0;
            try
            {
                document.Print();
            }
            catch (InvalidPrinterException ex)
            {
                throw new PrintBridgeException(PrintErrorCodes.PrinterUnavailable,
                    $"The printer for queue '{request.Queue}' is not available.", ex);
            }
            catch (Win32Exception ex)
            {
                throw new PrintBridgeException(PrintErrorCodes.PrintFailed,
                    $"Windows refused the print job: {ex.Message}", ex);
            }

            if (pageFailure is not null)
            {
                ExceptionDispatchInfo.Capture(pageFailure).Throw();
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        _logger.LogInformation("Print job for queue {Queue} spooled ({Pages} page(s))", request.Queue, pagesRendered * driverCopies);
    }

    /// <summary>
    /// Asks the driver for copies when it supports as many as requested, and reports how
    /// many copies one <see cref="PrintDocument.Print"/> pass produces; the caller loops
    /// the document for the rest.
    /// </summary>
    private static int ApplyCopies(PrinterSettings settings, int copies)
    {
        if (copies <= 1)
        {
            return 1;
        }

        var maximum = settings.MaximumCopies;
        if (maximum >= copies && copies <= short.MaxValue)
        {
            settings.Copies = (short)copies;
            return copies;
        }

        return 1;
    }

    private static int ResolveRenderDpi(PrinterSettings settings)
    {
        var dpi = FallbackRenderDpi;
        try
        {
            // Drivers report negative values (-1…-4) for the named quality levels
            // rather than a real resolution; only a positive number is usable.
            var resolution = settings.DefaultPageSettings.PrinterResolution;
            if (resolution is not null && resolution.X > 0)
            {
                dpi = resolution.X;
            }
        }
        catch (Exception ex) when (ex is InvalidPrinterException or Win32Exception)
        {
            // Fall back to the default; the printer check above already passed.
        }

        return Math.Clamp(dpi, MinRenderDpi, MaxRenderDpi);
    }

    /// <summary>
    /// The drawable area, in hundredths of an inch. This comes from the printer device
    /// context itself, so it is already expressed in the page's real orientation —
    /// unlike <see cref="PageSettings.PrintableArea"/>, whose landscape handling is a
    /// long-standing source of driver-dependent surprises.
    /// </summary>
    private static RectangleF GetPrintableArea(PrintPageEventArgs e, Graphics graphics)
    {
        var clip = graphics.VisibleClipBounds;
        if (clip.Width > 0 && clip.Height > 0)
        {
            return clip;
        }

        // Some drivers report nothing usable; the full sheet is a safe stand-in
        // (PageBounds is already swapped for a landscape page).
        var bounds = e.PageBounds;
        return new RectangleF(0, 0, bounds.Width, bounds.Height);
    }

    /// <summary>
    /// Places a PDF page (size in points, 1/72") centered inside the printable area,
    /// scaled down to fit but never enlarged. Both the area and the result are in
    /// hundredths of an inch, the units a printer <see cref="Graphics"/> draws in.
    /// </summary>
    public static RectangleF FitCentered(SizeF pageSizeInPoints, RectangleF printableArea)
    {
        var contentWidth = pageSizeInPoints.Width / 72f * 100f;
        var contentHeight = pageSizeInPoints.Height / 72f * 100f;
        if (contentWidth <= 0 || contentHeight <= 0)
        {
            return printableArea;
        }

        var scale = Math.Min(printableArea.Width / contentWidth, printableArea.Height / contentHeight);
        scale = Math.Min(scale, 1f);

        var width = contentWidth * scale;
        var height = contentHeight * scale;
        return new RectangleF(
            printableArea.X + ((printableArea.Width - width) / 2f),
            printableArea.Y + ((printableArea.Height - height) / 2f),
            width,
            height);
    }

    /// <summary>
    /// Rasterizes one page. Pages are rendered one at a time and released immediately —
    /// a letter page at 600 dpi is over 100 MB as a bitmap.
    /// </summary>
    private static RenderedPage RenderPage(byte[] pdfBytes, int pageIndex, int dpi)
    {
        var png = new MemoryStream();
        try
        {
            Conversion.SavePng(png, pdfBytes, pageIndex, null, new RenderOptions(
                Dpi: dpi,
                WithAnnotations: true,
                WithFormFill: true));
            png.Position = 0;
            return new RenderedPage(png);
        }
        catch (Exception ex) when (IsPdfFailure(ex))
        {
            png.Dispose();
            throw new PrintBridgeException(PrintErrorCodes.InvalidDocument,
                $"Page {pageIndex + 1} of the PDF could not be rendered.", ex);
        }
        catch
        {
            png.Dispose();
            throw;
        }
    }

    private static bool IsPdfFailure(Exception ex) =>
        ex is PdfException or ArgumentException or Win32Exception or FormatException;

    /// <summary>
    /// A rendered page plus the PNG stream behind it: GDI+ requires the stream an
    /// <see cref="Image"/> was loaded from to stay open for the image's lifetime.
    /// </summary>
    private sealed class RenderedPage : IDisposable
    {
        private readonly MemoryStream _png;

        public RenderedPage(MemoryStream png)
        {
            _png = png;
            Image = Image.FromStream(png);
        }

        public Image Image { get; }

        public void Dispose()
        {
            Image.Dispose();
            _png.Dispose();
        }
    }
}
