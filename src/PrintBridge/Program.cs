using Microsoft.Extensions.Logging;
using PrintBridge.App;
using PrintBridge.Hosting;
using PrintBridge.Printing;
using PrintBridge.Settings;
using Serilog;
using Serilog.Extensions.Logging;

namespace PrintBridge;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstanceMutex = new Mutex(initiallyOwned: true, @"Global\PrintBridge.SingleInstance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            // Another PrintBridge is already running (and owns the port); just exit quietly.
            return;
        }

        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintBridge", "logs");
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(logDirectory, "printbridge-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        try
        {
            Log.Information("PrintBridge starting");

            var settingsStore = new SettingsStore();
            settingsStore.Load();

            try
            {
                StartupRegistration.Apply(settingsStore.Current.RunAtLogin);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not update the run-at-login registration");
            }

            using var loggerFactory = new SerilogLoggerFactory(Log.Logger);
            var printService = new PrintService(loggerFactory.CreateLogger("PrintBridge.Printing"));
            using var jobManager = new PrintJobManager(printService.PrintAsync);
            var webHostRunner = new WebHostRunner(settingsStore, jobManager, Log.Logger);

            settingsStore.Changed += settings =>
            {
                try
                {
                    StartupRegistration.Apply(settings.RunAtLogin);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Could not update the run-at-login registration");
                }

                // Port or origins may have changed; rebuild the listener.
                _ = webHostRunner.StartAsync();
            };

            // No message loop yet, so blocking here cannot deadlock the UI.
            webHostRunner.StartAsync().GetAwaiter().GetResult();

            var openSettingsOnStartup = settingsStore.IsFirstRun && !args.Contains("--minimized");

            ApplicationConfiguration.Initialize();
            using var trayContext = new TrayApplicationContext(
                settingsStore, jobManager, webHostRunner, openSettingsOnStartup);
            Application.Run(trayContext);

            webHostRunner.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Log.Information("PrintBridge exited cleanly");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "PrintBridge crashed");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
