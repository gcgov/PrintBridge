using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace PrintBridge.Localization;

/// <summary>
/// Every user-visible text of PrintBridge. Each member name is its key in
/// <c>Strings.resx</c> (English, the neutral language) and in each translation,
/// <c>Strings.{culture}.resx</c>. A lookup uses the UI culture of the calling thread and
/// falls back to English when a translation is missing.
/// </summary>
/// <remarks>
/// Log messages and API error codes are not here: they stay in English so that support
/// staff and client code can rely on them whatever the user's language.
/// </remarks>
public static class Strings
{
    public static ResourceManager ResourceManager { get; } =
        new("PrintBridge.Localization.Strings", typeof(Strings).Assembly);

    /// <summary>The text for <paramref name="key"/>, or the key itself if no resource has it.</summary>
    public static string Get(string key, CultureInfo? culture = null) =>
        ResourceManager.GetString(key, culture ?? CultureInfo.CurrentUICulture) ?? key;

    /// <summary>
    /// The text for <paramref name="key"/> with its placeholders filled in. A null
    /// <paramref name="culture"/> uses the UI culture for the text and the current culture
    /// for numbers in the arguments.
    /// </summary>
    public static string Format(string key, CultureInfo? culture, params object?[] args) =>
        string.Format(culture ?? CultureInfo.CurrentCulture, Get(key, culture), args);

    private static string S([CallerMemberName] string key = "") => Get(key);

    private static string F(object?[] args, [CallerMemberName] string key = "") => Format(key, null, args);

    // ---- Shared ----
    public static string NotListening => S();
    public static string OpenLogFolder => S();

    // ---- Tray ----
    public static string TrayMenuSettings => S();
    public static string TrayMenuTestPage => S();
    public static string TrayMenuExit => S();
    public static string TrayNoQueueConfigured => S();
    public static string TrayTestPageUnreadable(string detail) => F([detail]);
    public static string TrayTestPageSent(string queue) => F([queue]);
    public static string TrayPrintFailedTitle => S();

    // ---- Settings window ----
    public static string SettingsTitle => S();
    public static string SettingsQueuesGroup => S();
    public static string SettingsQueuesHint => S();
    public static string SettingsQueueColumn => S();
    public static string SettingsPrinterColumn => S();
    public static string SettingsDefaultColumn => S();
    public static string SettingsDefaultYes => S();
    public static string SettingsAddQueue => S();
    public static string SettingsEditQueue => S();
    public static string SettingsRemoveQueue => S();
    public static string SettingsSetDefaultQueue => S();
    public static string SettingsDuplicateQueueTitle => S();
    public static string SettingsDuplicateQueueMessage(string queue) => F([queue]);
    public static string SettingsServerGroup => S();
    public static string SettingsPortLabel => S();
    public static string SettingsListening(int port) => F([port]);
    public static string SettingsOriginsLabel => S();
    public static string SettingsAddOrigin => S();
    public static string SettingsRemoveOrigin => S();
    public static string SettingsInvalidOriginTitle => S();
    public static string SettingsInvalidOriginMessage => S();
    public static string SettingsRunAtLogin => S();
    public static string SettingsLanguageLabel => S();
    public static string SettingsLanguageAutomatic => S();
    public static string SettingsSave => S();
    public static string SettingsCancel => S();

    // ---- Queue dialog ----
    public static string QueueDialogAddTitle => S();
    public static string QueueDialogEditTitle => S();
    public static string QueueDialogNameLabel => S();
    public static string QueueDialogPrinterLabel => S();
    public static string QueueDialogHint => S();
    public static string QueueDialogOk => S();
    public static string QueueDialogPrinterListFailed(string detail) => F([detail]);
    public static string QueueDialogPrinterMissing(string printer) => F([printer]);
    public static string QueueDialogNoPrinters => S();
    public static string QueueDialogInvalidNameTitle => S();
    public static string QueueDialogInvalidNameMessage => S();
    public static string QueueDialogNoPrinterTitle => S();
    public static string QueueDialogNoPrinterMessage => S();

    // ---- Local server and API errors ----
    public static string ServerPortInUse(int port) => F([port]);
    public static string ServerStartFailed(string detail) => F([detail]);
    public static string ApiInvalidCopies(int minimum, int maximum) => F([minimum, maximum]);
    public static string ApiUnknownQueue(string queue) => F([queue]);
    public static string ApiNoQueueConfigured => S();
    public static string ApiNotAPdf => S();
    public static string ApiDocumentTooLarge(long megabytes) => F([megabytes]);
    public static string ApiPrintJobNotFound => S();
    public static string ApiInvalidHost => S();
    public static string ApiOriginNotAllowed(string origin) => F([origin]);

    // ---- Print errors (also used as LocalizedMessage keys) ----
    public static string PrintCanceled => S();
    public static string PrintUnreadablePdf => S();
    public static string PrintEmptyPdf => S();
    public static string PrintPageRenderFailed(int page) => F([page]);
    public static string PrintPrinterUnavailable(string queue) => F([queue]);
    public static string PrintWindowsRefused(string detail) => F([detail]);
    public static string PrintFailed(string detail) => F([detail]);
}
