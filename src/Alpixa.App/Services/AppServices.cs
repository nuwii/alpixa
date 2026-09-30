using System.Globalization;
using Alpixa.Core.Abstractions;

namespace Alpixa.App.Services;

public sealed class MauiAppPaths : IAppPaths
{
    public MauiAppPaths() => MigrateFromMailPilot();

    public string DataDirectory { get; } = Path.Combine(FileSystem.AppDataDirectory, "Alpixa");
    public string DatabasePath => Path.Combine(DataDirectory, "alpixa.db");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    /// <summary>The app used to be called MailPilot; carry its data folder and database over once.</summary>
    private void MigrateFromMailPilot()
    {
        try
        {
            var legacy = Path.Combine(FileSystem.AppDataDirectory, "MailPilot");
            if (!Directory.Exists(legacy) || Directory.Exists(DataDirectory)) return;
            Directory.Move(legacy, DataDirectory);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var old = Path.Combine(DataDirectory, "mailpilot.db" + suffix);
                if (File.Exists(old)) File.Move(old, Path.Combine(DataDirectory, "alpixa.db" + suffix));
            }
        }
        catch (IOException)
        {
            // Keep going with a fresh data folder rather than failing to start.
        }
    }
}

public sealed class MauiSecretStore : ISecretStore
{
    private const string Prefix = "alpixa.";
    private const string LegacyPrefix = "mailpilot.";

    public async Task<string?> GetAsync(string key)
    {
        var value = await SecureStorage.Default.GetAsync(Prefix + key);
        if (value is not null) return value;
        value = await SecureStorage.Default.GetAsync(LegacyPrefix + key);
        if (value is not null) await SecureStorage.Default.SetAsync(Prefix + key, value);
        return value;
    }

    public Task SetAsync(string key, string value) => SecureStorage.Default.SetAsync(Prefix + key, value);

    public Task RemoveAsync(string key)
    {
        SecureStorage.Default.Remove(Prefix + key);
        return Task.CompletedTask;
    }
}

public static class LanguagePreference
{
    private const string Key = "language";

    /// <summary>Supported UI languages: code, name shown in the picker, culture.</summary>
    public static readonly IReadOnlyList<(string Code, string Name, string Culture)> Supported =
    [
        ("tr", "Türkçe", "tr-TR"),
        ("en", "English", "en-US"),
        ("de", "Deutsch", "de-DE"),
        ("fr", "Français", "fr-FR"),
        ("es", "Español", "es-ES"),
        ("ru", "Русский", "ru-RU")
    ];

    public static IReadOnlyList<string> Names => Supported.Select(l => l.Name).ToList();

    public static int CurrentIndex => Math.Max(0, Supported.ToList().FindIndex(l => l.Code == Current));

    public static string CodeAt(int index) => Supported[Math.Clamp(index, 0, Supported.Count - 1)].Code;

    public static string Current => Preferences.Default.Get(Key, "tr");

    public static void Set(string language)
    {
        Preferences.Default.Set(Key, language);
#if MACCATALYST || IOS
        // System controls (file dialogs, picker buttons) follow the app language after a restart.
        Foundation.NSUserDefaults.StandardUserDefaults.SetValueForKey(Foundation.NSArray.FromStrings(language), new Foundation.NSString("AppleLanguages"));
#endif
    }

    public static void Apply()
    {
        var culture = new CultureInfo(Supported.FirstOrDefault(l => l.Code == Current).Culture ?? "tr-TR");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        Resources.Strings.AppResources.Culture = culture;
    }
}

public class NullPlatformService : IPlatformService
{
    public virtual void SetKeepAwake(bool keepAwake) { }
    public virtual void ShowNotification(string title, string message) { }
    public virtual void SetProgress(double? fraction) { }
    public virtual void OpenFolder(string path) => _ = Launcher.Default.OpenAsync(new OpenFileRequest("", new ReadOnlyFile(path)));
    public virtual Task OpenBrowserAsync(Uri uri) => Browser.Default.OpenAsync(uri, BrowserLaunchMode.External);
    public virtual void AcceptDragOver(object platformDragOverArgs) { }
    public virtual Task<IReadOnlyList<string>> GetDroppedFilePathsAsync(object platformDropArgs) => Task.FromResult<IReadOnlyList<string>>([]);
}
