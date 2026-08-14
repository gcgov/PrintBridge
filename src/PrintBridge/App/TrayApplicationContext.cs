using System.Diagnostics;
using PrintBridge.Hosting;
using PrintBridge.Printing;
using PrintBridge.Settings;
using Serilog;

namespace PrintBridge.App;

/// <summary>
/// The tray icon and its menu. PrintBridge has no main window — the tray icon is the app.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly PrintJobManager _jobManager;
    private readonly WebHostRunner _webHostRunner;
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _syncContext;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext(
        SettingsStore settingsStore,
        PrintJobManager jobManager,
        WebHostRunner webHostRunner,
        bool openSettingsOnStartup)
    {
        _settingsStore = settingsStore;
        _jobManager = jobManager;
        _webHostRunner = webHostRunner;
        _syncContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings…", null, (_, _) => ShowSettings());
        menu.Items.Add("Print test page", null, (_, _) => PrintTestPage());
        menu.Items.Add("Open log folder", null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "PrintBridge",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _jobManager.JobFinished += OnJobFinished;
        _webHostRunner.StatusChanged += OnListenerStatusChanged;

        if (_webHostRunner.LastError is { } startupError)
        {
            ShowBalloon("PrintBridge", startupError, ToolTipIcon.Warning);
        }

        if (openSettingsOnStartup)
        {
            // Let the message loop start before opening the window.
            _syncContext.Post(_ => ShowSettings(), null);
        }
    }

    private static Icon LoadIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "printbridge.ico");
            if (File.Exists(iconPath))
            {
                return new Icon(iconPath);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load the tray icon; falling back to the stock icon");
        }

        return SystemIcons.Application;
    }

    private void ShowSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settingsStore, _webHostRunner);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    /// <summary>
    /// Prints the bundled test page through the ordinary pipeline, so a successful test
    /// proves the same path a web app uses.
    /// </summary>
    private void PrintTestPage()
    {
        var settings = _settingsStore.Current;
        if (!settings.TryResolveQueue(null, out var queue, out _))
        {
            ShowBalloon("PrintBridge", "No print queue is configured yet — open Settings first.", ToolTipIcon.Warning);
            ShowSettings();
            return;
        }

        byte[] pdfBytes;
        var testPagePath = Path.Combine(AppContext.BaseDirectory, "Resources", "testpage.pdf");
        try
        {
            pdfBytes = File.ReadAllBytes(testPagePath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not read the bundled test page from {Path}", testPagePath);
            ShowBalloon("PrintBridge", $"Could not read the test page: {ex.Message}", ToolTipIcon.Error);
            return;
        }

        _jobManager.Enqueue(new PrintRequest(queue.Name, queue.PrinterName, Copies: 1, PdfBytes: pdfBytes));
        ShowBalloon("PrintBridge", $"Sent a test page to \"{queue.Name}\".", ToolTipIcon.Info);
    }

    private void OpenLogFolder()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintBridge", "logs");
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
    }

    private void OnJobFinished(PrintJob job)
    {
        if (job.Status == PrintJobStatus.Failed)
        {
            ShowBalloon("PrintBridge — print failed", job.ErrorMessage ?? "The print job failed.", ToolTipIcon.Error);
        }
    }

    private void OnListenerStatusChanged()
    {
        if (_webHostRunner.LastError is { } error)
        {
            ShowBalloon("PrintBridge", error, ToolTipIcon.Warning);
        }
    }

    private void ShowBalloon(string title, string message, ToolTipIcon icon)
    {
        // Events can fire on worker threads; NotifyIcon belongs to the UI thread.
        _syncContext.Post(_ => _notifyIcon.ShowBalloonTip(5000, title, message, icon), null);
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _jobManager.JobFinished -= OnJobFinished;
            _webHostRunner.StatusChanged -= OnListenerStatusChanged;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
