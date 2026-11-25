using System.Diagnostics;
using PrintBridge.Hosting;
using PrintBridge.Localization;
using PrintBridge.Printing;
using PrintBridge.Settings;
using Serilog;

namespace PrintBridge.App;

/// <summary>
/// The tray icon and its menu. PrintBridge has no main window — the tray icon is the app.
/// </summary>
/// <remarks>
/// Every text is built on the UI thread, in the desktop language. Events from print jobs
/// and from the web host arrive on other threads, so the handlers go through
/// <see cref="PostToUi"/>, which also drops the caller's culture (a web request can carry
/// another language in its Accept-Language header).
/// </remarks>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SettingsStore _settingsStore;
    private readonly PrintJobManager _jobManager;
    private readonly WebHostRunner _webHostRunner;
    private readonly NotifyIcon _notifyIcon;
    private readonly SynchronizationContext _syncContext;
    private SettingsForm? _settingsForm;
    private string _language;

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
        _language = settingsStore.Current.Language;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "PrintBridge",
            ContextMenuStrip = BuildMenu(),
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _jobManager.JobFinished += OnJobFinished;
        _webHostRunner.StatusChanged += OnListenerStatusChanged;
        _settingsStore.Changed += OnSettingsChanged;

        if (_webHostRunner.LastError is { } startupError)
        {
            ShowBalloon("PrintBridge", startupError.ToString(), ToolTipIcon.Warning);
        }

        if (openSettingsOnStartup)
        {
            // Let the message loop start before opening the window.
            PostToUi(ShowSettings);
        }
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(Strings.TrayMenuSettings, null, (_, _) => ShowSettings());
        menu.Items.Add(Strings.TrayMenuTestPage, null, (_, _) => PrintTestPage());
        menu.Items.Add(Strings.OpenLogFolder, null, (_, _) => OpenLogFolder());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Strings.TrayMenuExit, null, (_, _) => ExitApplication());
        return menu;
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
            ShowBalloon("PrintBridge", Strings.TrayNoQueueConfigured, ToolTipIcon.Warning);
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
            ShowBalloon("PrintBridge", Strings.TrayTestPageUnreadable(ex.Message), ToolTipIcon.Error);
            return;
        }

        _jobManager.Enqueue(new PrintRequest(queue.Name, queue.PrinterName, Copies: 1, PdfBytes: pdfBytes));
        ShowBalloon("PrintBridge", Strings.TrayTestPageSent(queue.Name), ToolTipIcon.Info);
    }

    private void OpenLogFolder()
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintBridge", "logs");
        Directory.CreateDirectory(logDirectory);
        Process.Start(new ProcessStartInfo(logDirectory) { UseShellExecute = true });
    }

    private void OnJobFinished(PrintJob job) => PostToUi(() =>
    {
        if (job.Status == PrintJobStatus.Failed)
        {
            ShowBalloon(Strings.TrayPrintFailedTitle, job.ErrorMessage?.ToString() ?? string.Empty, ToolTipIcon.Error);
        }
    });

    private void OnListenerStatusChanged() => PostToUi(() =>
    {
        if (_webHostRunner.LastError is { } error)
        {
            ShowBalloon("PrintBridge", error.ToString(), ToolTipIcon.Warning);
        }
    });

    /// <summary>Rebuilds the menu when the language changes. The settings window applies it the next time it opens.</summary>
    private void OnSettingsChanged(AppSettings settings) => PostToUi(() =>
    {
        if (settings.Language == _language)
        {
            return;
        }

        _language = settings.Language;
        var oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = BuildMenu();
        oldMenu?.Dispose();
    });

    private void ShowBalloon(string title, string message, ToolTipIcon icon) =>
        PostToUi(() => _notifyIcon.ShowBalloonTip(5000, title, message, icon));

    /// <summary>
    /// Runs <paramref name="action"/> on the UI thread, in the UI thread's own execution
    /// context. Suppressing the flow keeps the culture of a web request out of the tray.
    /// </summary>
    private void PostToUi(Action action)
    {
        if (ExecutionContext.IsFlowSuppressed())
        {
            _syncContext.Post(_ => action(), null);
            return;
        }

        using (ExecutionContext.SuppressFlow())
        {
            _syncContext.Post(_ => action(), null);
        }
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
            _settingsStore.Changed -= OnSettingsChanged;
            _notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
