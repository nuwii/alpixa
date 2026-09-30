using System.Globalization;
using System.Text;
using CommunityToolkit.Maui.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Options;
using Alpixa.Core.Rules;
using Alpixa.Core.Settings;
using Alpixa.Infrastructure.Contacts;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Maintenance;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending.Engine;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class SettingsViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<SettingsViewModel> logger,
    SettingsStore settings,
    ISecretStore secrets,
    UnsubscribeSecretProvider unsubscribeSecret,
    ContactService contacts,
    MaintenanceService maintenance,
    BackgroundJobs background,
    IConsentProvider consent,
    IAppPaths paths,
    AlpixaOptions options,
    IPlatformService platform) : BaseViewModel(startup, dialogs, logger)
{
    private GeneralSettings _general = new();
    private SendingSettings _sending = new();
    private bool _loading;

    public IReadOnlyList<string> Languages { get; } = LanguagePreference.Names;

    [ObservableProperty] public partial int LanguageIndex { get; set; }
    [ObservableProperty] public partial string SeedAddresses { get; set; } = "";
    [ObservableProperty] public partial bool OpenTrackingDefault { get; set; }
    [ObservableProperty] public partial string? EndpointUrl { get; set; }
    [ObservableProperty] public partial string? EndpointApiKey { get; set; }
    [ObservableProperty] public partial bool ShowAdvanced { get; set; }
    [ObservableProperty] public partial int DelayMin { get; set; }
    [ObservableProperty] public partial int DelayMax { get; set; }
    [ObservableProperty] public partial int DefaultParallelism { get; set; }
    [ObservableProperty] public partial int MaxParallelism { get; set; }
    [ObservableProperty] public partial double ErrorThresholdPercent { get; set; }
    [ObservableProperty] public partial int MaxRetries { get; set; }
    [ObservableProperty] public partial int GmailPerMinute { get; set; }
    [ObservableProperty] public partial int MicrosoftPerMinute { get; set; }
    [ObservableProperty] public partial int YahooPerMinute { get; set; }
    [ObservableProperty] public partial int YandexPerMinute { get; set; }
    [ObservableProperty] public partial int OtherPerMinute { get; set; }
    [ObservableProperty] public partial int MaxAttachmentMb { get; set; }
    [ObservableProperty] public partial int MaxImageKb { get; set; }
    [ObservableProperty] public partial string WarmupPlanText { get; set; } = "";
    [ObservableProperty] public partial string? GdprEmail { get; set; }
    [ObservableProperty] public partial string ConsentProviderName { get; set; } = "";
    [ObservableProperty] public partial string? LastFeedbackCheck { get; set; }
    [ObservableProperty] public partial string DataFolder { get; set; } = "";
    [ObservableProperty] public partial string GoogleClientId { get; set; } = "";
    [ObservableProperty] public partial string GoogleClientSecret { get; set; } = "";
    [ObservableProperty] public partial string MicrosoftClientId { get; set; } = "";

    public string OAuthRedirectHint => "http://127.0.0.1";

    protected override async Task LoadAsync()
    {
        _loading = true;
        _general = await settings.GetGeneralAsync();
        _sending = await settings.GetSendingAsync();
        LanguageIndex = LanguagePreference.CurrentIndex;
        SeedAddresses = string.Join(", ", _general.SeedAddresses);
        OpenTrackingDefault = _general.OpenTrackingDefault;
        EndpointUrl = _general.EndpointBaseUrl;
        EndpointApiKey = await secrets.GetAsync(UnsubscribeSecretProvider.EndpointApiKey);
        ShowAdvanced = _general.ShowAdvanced;
        DelayMin = _sending.SequentialDelayMinSeconds;
        DelayMax = _sending.SequentialDelayMaxSeconds;
        DefaultParallelism = _sending.DefaultBulkParallelism;
        MaxParallelism = _sending.MaxBulkParallelism;
        ErrorThresholdPercent = _sending.ErrorRateThreshold * 100;
        MaxRetries = _sending.MaxRetries;
        GmailPerMinute = _sending.DomainGroupPerMinute.GetValueOrDefault(RecipientDomainGroups.Gmail, 20);
        MicrosoftPerMinute = _sending.DomainGroupPerMinute.GetValueOrDefault(RecipientDomainGroups.Microsoft, 20);
        YahooPerMinute = _sending.DomainGroupPerMinute.GetValueOrDefault(RecipientDomainGroups.Yahoo, 15);
        YandexPerMinute = _sending.DomainGroupPerMinute.GetValueOrDefault(RecipientDomainGroups.Yandex, 15);
        OtherPerMinute = _sending.DomainGroupPerMinute.GetValueOrDefault(RecipientDomainGroups.Other, 30);
        MaxAttachmentMb = _sending.MaxAttachmentMb;
        MaxImageKb = _sending.MaxImageKb;
        WarmupPlanText = string.Join(Environment.NewLine, _sending.Warmup.Steps.OrderBy(s => s.FromDay).Select(s => $"{s.FromDay}: {s.DailyLimit}"));
        ConsentProviderName = consent.Name;
        LastFeedbackCheck = background.LastFeedbackCheckUtc?.ToLocalTime().ToString("g");
        DataFolder = paths.DataDirectory;
        GoogleClientId = options.OAuth.GoogleClientId;
        GoogleClientSecret = options.OAuth.GoogleClientSecret;
        MicrosoftClientId = options.OAuth.MicrosoftClientId;
        _loading = false;
    }

    async partial void OnLanguageIndexChanged(int value)
    {
        if (_loading) return;
        LanguagePreference.Set(LanguagePreference.CodeAt(value));
        await Dialogs.AlertAsync(Loc.T("Settings_LanguageTitle"), Loc.T("Settings_LanguageRestart"));
    }

    async partial void OnOpenTrackingDefaultChanged(bool value)
    {
        if (_loading || !value) return;
        if (!await Dialogs.ConfirmAsync(Loc.T("Settings_TrackingTitle"), Loc.T("Settings_TrackingWarning"), Loc.T("Settings_TrackingEnable"), Loc.T("Common_Cancel")))
            OpenTrackingDefault = false;
    }

    [RelayCommand]
    private Task Save() => RunBusyAsync(async () =>
    {
        var seeds = SeedAddresses.Split([',', ';', ' ', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var bad = seeds.Where(s => !EmailAddressRules.IsValid(s)).ToList();
        if (bad.Count > 0) throw new InvalidOperationException(Loc.T("Settings_BadSeeds", string.Join(", ", bad)));
        if (!string.IsNullOrWhiteSpace(EndpointUrl) && (!Uri.TryCreate(EndpointUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException(Loc.T("Settings_EndpointHttps"));
        if (DelayMin < 0 || DelayMax < DelayMin) throw new InvalidOperationException(Loc.T("Settings_BadDelay"));
        if (ErrorThresholdPercent is <= 0 or > 100) throw new InvalidOperationException(Loc.T("Settings_BadThreshold"));

        _general.SeedAddresses = seeds;
        _general.OpenTrackingDefault = OpenTrackingDefault;
        _general.EndpointBaseUrl = string.IsNullOrWhiteSpace(EndpointUrl) ? null : EndpointUrl.Trim().TrimEnd('/');
        _general.ShowAdvanced = ShowAdvanced;
        await settings.SaveGeneralAsync(_general);
        if (string.IsNullOrWhiteSpace(EndpointApiKey)) await secrets.RemoveAsync(UnsubscribeSecretProvider.EndpointApiKey);
        else await secrets.SetAsync(UnsubscribeSecretProvider.EndpointApiKey, EndpointApiKey.Trim());

        _sending.SequentialDelayMinSeconds = DelayMin;
        _sending.SequentialDelayMaxSeconds = DelayMax;
        _sending.MaxBulkParallelism = Math.Clamp(MaxParallelism, 1, 10);
        _sending.DefaultBulkParallelism = Math.Clamp(DefaultParallelism, 1, _sending.MaxBulkParallelism);
        _sending.ErrorRateThreshold = ErrorThresholdPercent / 100;
        _sending.MaxRetries = Math.Clamp(MaxRetries, 1, 10);
        _sending.DomainGroupPerMinute[RecipientDomainGroups.Gmail] = Math.Max(1, GmailPerMinute);
        _sending.DomainGroupPerMinute[RecipientDomainGroups.Microsoft] = Math.Max(1, MicrosoftPerMinute);
        _sending.DomainGroupPerMinute[RecipientDomainGroups.Yahoo] = Math.Max(1, YahooPerMinute);
        _sending.DomainGroupPerMinute[RecipientDomainGroups.Yandex] = Math.Max(1, YandexPerMinute);
        _sending.DomainGroupPerMinute[RecipientDomainGroups.Other] = Math.Max(1, OtherPerMinute);
        _sending.MaxAttachmentMb = Math.Clamp(MaxAttachmentMb, 1, 25);
        _sending.MaxImageKb = Math.Clamp(MaxImageKb, 50, 5120);
        _sending.Warmup = ParseWarmup(WarmupPlanText);
        await settings.SaveSendingAsync(_sending);
        await SaveOAuthAsync();
        await Dialogs.ToastAsync(Loc.T("Common_Saved"));
    });

    private static WarmupPlan ParseWarmup(string text)
    {
        var plan = new WarmupPlan();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(':', StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[0], out var day) || !int.TryParse(parts[1].Replace(".", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var limit) || day < 1 || limit < 1)
                throw new InvalidOperationException(Loc.T("Settings_BadWarmup", line));
            plan.Steps.Add(new WarmupStep { FromDay = day, DailyLimit = limit });
        }
        if (plan.Steps.Count == 0) throw new InvalidOperationException(Loc.T("Settings_BadWarmup", ""));
        return plan;
    }

    [RelayCommand]
    private void ResetWarmup() => WarmupPlanText = string.Join(Environment.NewLine, WarmupPlan.Default().Steps.Select(s => $"{s.FromDay}: {s.DailyLimit}"));

    /// <summary>Applies OAuth client ids immediately and keeps them in the data-folder appsettings.json override.</summary>
    private async Task SaveOAuthAsync()
    {
        var googleId = GoogleClientId.Trim();
        if (googleId.Length > 0 && !googleId.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("Settings_BadGoogleClientId"));
        if (googleId == options.OAuth.GoogleClientId && GoogleClientSecret.Trim() == options.OAuth.GoogleClientSecret
            && MicrosoftClientId.Trim() == options.OAuth.MicrosoftClientId) return;

        options.OAuth.GoogleClientId = googleId;
        options.OAuth.GoogleClientSecret = GoogleClientSecret.Trim();
        options.OAuth.MicrosoftClientId = MicrosoftClientId.Trim();

        var file = Path.Combine(paths.DataDirectory, "appsettings.json");
        System.Text.Json.Nodes.JsonObject root;
        try
        {
            root = File.Exists(file)
                ? System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(file), documentOptions: new() { CommentHandling = System.Text.Json.JsonCommentHandling.Skip }) as System.Text.Json.Nodes.JsonObject ?? new()
                : new();
        }
        catch (System.Text.Json.JsonException)
        {
            root = new();
        }
        root["OAuth"] = System.Text.Json.JsonSerializer.SerializeToNode(options.OAuth);
        await File.WriteAllTextAsync(file, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [RelayCommand] private Task RestartSetup() => Shell.Current.GoToAsync(AppShell.SetupRoute);

    [RelayCommand]
    private async Task CopySecret()
    {
        await Clipboard.Default.SetTextAsync(await unsubscribeSecret.GetOrCreateAsync());
        await Dialogs.ToastAsync(Loc.T("Common_Copied"));
    }

    [RelayCommand]
    private Task CheckFeedbackNow() => RunBusyAsync(async () =>
    {
        var handled = await background.RunFeedbackOnceAsync(CancellationToken.None);
        LastFeedbackCheck = background.LastFeedbackCheckUtc?.ToLocalTime().ToString("g");
        await Dialogs.AlertAsync(Loc.T("Settings_FeedbackTitle"), Loc.T("Settings_FeedbackResult", handled));
    }, Loc.T("Settings_FeedbackWorking"));

    [RelayCommand]
    private Task ExportPersonalData() => RunBusyAsync(async () =>
    {
        if (!EmailAddressRules.IsValid(GdprEmail)) throw new InvalidOperationException(Loc.T("Settings_NeedEmail"));
        var json = await contacts.ExportPersonalDataAsync(GdprEmail!);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var result = await FileSaver.Default.SaveAsync($"kişisel-veri-{GdprEmail}.json", stream, CancellationToken.None);
        if (result.IsSuccessful) await Dialogs.ToastAsync(Loc.T("Reports_Saved", result.FilePath));
    });

    [RelayCommand]
    private async Task ForgetPerson()
    {
        if (!EmailAddressRules.IsValid(GdprEmail))
        {
            await Dialogs.AlertAsync(Loc.T("Common_Missing"), Loc.T("Settings_NeedEmail"));
            return;
        }
        if (!await Dialogs.ConfirmAsync(Loc.T("Settings_ForgetTitle"), Loc.T("Settings_ForgetConfirm", GdprEmail), Loc.T("Settings_ForgetYes"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            var removed = await contacts.ForgetAsync(GdprEmail!);
            await Dialogs.AlertAsync(Loc.T("Settings_ForgetTitle"), Loc.T("Settings_ForgetDone", removed));
        });
    }

    [RelayCommand]
    private Task Backup() => RunBusyAsync(async () =>
    {
        var temp = Path.Combine(FileSystem.CacheDirectory, "alpixa-yedek.zip");
        await maintenance.CreateBackupAsync(temp);
        await using var stream = File.OpenRead(temp);
        var result = await FileSaver.Default.SaveAsync($"alpixa-yedek-{DateTime.Now:yyyyMMdd-HHmm}.zip", stream, CancellationToken.None);
        if (result.IsSuccessful) await Dialogs.AlertAsync(Loc.T("Settings_BackupTitle"), Loc.T("Settings_BackupDone", result.FilePath));
    }, Loc.T("Settings_BackupWorking"));

    [RelayCommand]
    private async Task Restore()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("Settings_RestorePick") });
        if (file is null) return;
        if (!await Dialogs.ConfirmAsync(Loc.T("Settings_RestoreTitle"), Loc.T("Settings_RestoreConfirm"), Loc.T("Settings_RestoreYes"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            await maintenance.StageRestoreAsync(file.FullPath);
            await Dialogs.AlertAsync(Loc.T("Settings_RestoreTitle"), Loc.T("Settings_RestoreDone"));
        });
    }

    [RelayCommand] private void OpenLogFolder() => platform.OpenFolder(paths.LogDirectory);

    [RelayCommand] private void OpenDataFolder() => platform.OpenFolder(paths.DataDirectory);

    [RelayCommand]
    private Task SupportBundle() => RunBusyAsync(async () =>
    {
        var temp = Path.Combine(FileSystem.CacheDirectory, "alpixa-destek.zip");
        await maintenance.CreateSupportBundleAsync(temp);
        await using var stream = File.OpenRead(temp);
        var result = await FileSaver.Default.SaveAsync($"alpixa-destek-{DateTime.Now:yyyyMMdd-HHmm}.zip", stream, CancellationToken.None);
        if (result.IsSuccessful) await Dialogs.AlertAsync(Loc.T("Settings_SupportTitle"), Loc.T("Settings_SupportDone", result.FilePath));
    });
}
