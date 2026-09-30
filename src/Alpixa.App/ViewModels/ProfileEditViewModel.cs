using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class ProfileEditViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<ProfileEditViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    ISecretStore secrets,
    IConnectionTester tester,
    IOAuthTokenProvider oauth) : BaseViewModel(startup, dialogs, logger), IQueryAttributable
{
    private int _id;
    private bool _loaded;

    public IReadOnlyList<ProviderPreset> Presets { get; } = ProviderPresets.All;
    public IReadOnlyList<ConnectionSecurity> SecurityOptions { get; } = Enum.GetValues<ConnectionSecurity>();

    [ObservableProperty] public partial SenderProfile Profile { get; set; } = new();
    [ObservableProperty] public partial ProviderPreset? SelectedPreset { get; set; }
    [ObservableProperty] public partial string Password { get; set; } = "";
    [ObservableProperty] public partial string ImapPassword { get; set; } = "";
    [ObservableProperty] public partial bool UseAppPassword { get; set; }
    [ObservableProperty] public partial bool ShowAdvanced { get; set; }
    [ObservableProperty] public partial bool IsOAuthProvider { get; set; }
    [ObservableProperty] public partial bool ShowPassword { get; set; } = true;
    [ObservableProperty] public partial bool IsNoAuth { get; set; }
    [ObservableProperty] public partial string? SignInStatus { get; set; }
    [ObservableProperty] public partial string Title { get; set; } = "";
    [ObservableProperty] public partial bool ImapEnabled { get; set; }
    [ObservableProperty] public partial bool DkimEnabled { get; set; }

    // SenderProfile is a plain entity, so toggles that show/hide sections are mirrored here.
    partial void OnProfileChanged(SenderProfile value)
    {
        ImapEnabled = value.ImapEnabled;
        DkimEnabled = value.DkimEnabled;
    }

    partial void OnImapEnabledChanged(bool value) => Profile.ImapEnabled = value;
    partial void OnDkimEnabledChanged(bool value) => Profile.DkimEnabled = value;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        _id = query.TryGetValue("id", out var id) ? Convert.ToInt32(id) : 0;
    }

    protected override async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        if (_id == 0)
        {
            Title = Loc.T("ProfileEdit_NewTitle");
            var profile = new SenderProfile { Name = Loc.T("ProfileEdit_DefaultName") };
            ProviderPresets.Apply(ProviderPresets.For(TransportKind.AmazonSes), profile);
            Profile = profile;
            SelectedPreset = Presets.First(p => p.Kind == TransportKind.AmazonSes);
            return;
        }

        await using var db = await dbFactory.CreateDbContextAsync();
        Profile = await db.SenderProfiles.AsNoTracking().FirstAsync(p => p.Id == _id);
        Title = Profile.Name;
        SelectedPreset = Presets.First(p => p.Kind == Profile.Kind);
        UseAppPassword = Profile.Auth == AuthMethod.Password && Profile.Kind is TransportKind.Gmail or TransportKind.Microsoft365;
        Password = await secrets.GetAsync(Profile.SmtpSecretKey) ?? "";
        ImapPassword = await secrets.GetAsync(Profile.ImapSecretKey) ?? "";
        if (Profile.Auth is AuthMethod.OAuthGoogle or AuthMethod.OAuthMicrosoft && await secrets.GetAsync(Profile.OAuthSecretKey) is not null)
            SignInStatus = Loc.T("ProfileEdit_SignedIn", Profile.Username);
        UpdateAuthState();
    }

    partial void OnSelectedPresetChanged(ProviderPreset? value)
    {
        if (value is null || !_loaded) return;
        if (Profile.Kind != value.Kind)
        {
            ProviderPresets.Apply(value, Profile);
            UseAppPassword = false;
            OnPropertyChanged(nameof(Profile));
            Profile = Clone(Profile);
        }
        UpdateAuthState();
    }

    partial void OnUseAppPasswordChanged(bool value)
    {
        if (!_loaded) return;
        if (Profile.Kind is TransportKind.Gmail or TransportKind.Microsoft365)
            Profile.Auth = value ? AuthMethod.Password : Profile.Kind == TransportKind.Gmail ? AuthMethod.OAuthGoogle : AuthMethod.OAuthMicrosoft;
        UpdateAuthState();
    }

    private void UpdateAuthState()
    {
        IsOAuthProvider = Profile.Kind is TransportKind.Gmail or TransportKind.Microsoft365;
        ShowPassword = Profile.Auth == AuthMethod.Password;
        IsNoAuth = Profile.Auth == AuthMethod.None;
    }

    [RelayCommand]
    private Task SignIn() => RunBusyAsync(async () =>
    {
        if (!oauth.IsConfigured(Profile.Auth))
        {
            await Dialogs.AlertAsync(Loc.T("ProfileEdit_OAuthMissingTitle"), Loc.T("ProfileEdit_OAuthMissing"));
            return;
        }
        if (Profile.Id == 0) await SaveInternalAsync();
        var account = await oauth.SignInAsync(Profile, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(account))
        {
            Profile.Username = account!;
            if (string.IsNullOrWhiteSpace(Profile.FromAddress)) Profile.FromAddress = account!;
            Profile = Clone(Profile);
            await SaveInternalAsync();
        }
        SignInStatus = Loc.T("ProfileEdit_SignedIn", account ?? Profile.Username);
    }, Loc.T("ProfileEdit_WaitingBrowser"));

    [RelayCommand]
    private async Task PickDkimKey()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("ProfileEdit_DkimKeyPick") });
        if (file is null) return;
        Profile.DkimPrivateKeyPath = file.FullPath;
        Profile = Clone(Profile);
    }

    [RelayCommand]
    private Task Save() => RunBusyAsync(async () =>
    {
        if (!Validate(out var problem))
        {
            await Dialogs.AlertAsync(Loc.T("Common_Missing"), problem);
            return;
        }
        await SaveInternalAsync();
        await Shell.Current.GoToAsync("..");
    });

    [RelayCommand]
    private Task TestSmtp() => RunBusyAsync(async () =>
    {
        if (!Validate(out var problem))
        {
            await Dialogs.AlertAsync(Loc.T("Common_Missing"), problem);
            return;
        }
        await SaveInternalAsync();
        var (ok, error) = await tester.TestSmtpAsync(Profile, CancellationToken.None);
        if (ok) await Dialogs.AlertAsync(Loc.T("Profiles_TestOkTitle"), Loc.T("Profiles_TestOk", Profile.SmtpHost));
        else await Dialogs.ShowErrorAsync(error!);
    }, Loc.T("Profiles_Testing"));

    [RelayCommand]
    private Task TestImap() => RunBusyAsync(async () =>
    {
        await SaveInternalAsync();
        var (ok, error) = await tester.TestImapAsync(Profile, CancellationToken.None);
        if (ok) await Dialogs.AlertAsync(Loc.T("Profiles_TestOkTitle"), Loc.T("Profiles_TestOk", Profile.ImapHost ?? ""));
        else await Dialogs.ShowErrorAsync(error!);
    }, Loc.T("Profiles_Testing"));

    public async Task SaveInternalAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        if (Profile.Id == 0) db.SenderProfiles.Add(Profile);
        else db.SenderProfiles.Update(Profile);
        await db.SaveChangesAsync();
        _id = Profile.Id;

        if (Profile.Auth == AuthMethod.Password && !string.IsNullOrEmpty(Password))
            await secrets.SetAsync(Profile.SmtpSecretKey, Password);
        if (!string.IsNullOrEmpty(ImapPassword))
            await secrets.SetAsync(Profile.ImapSecretKey, ImapPassword);
    }

    private bool Validate(out string problem)
    {
        FillFromAccount();
        problem = "";
        if (string.IsNullOrWhiteSpace(Profile.Name)) problem = Loc.T("ProfileEdit_NeedName");
        else if (!EmailAddressRules.IsValid(Profile.FromAddress)) problem = Loc.T("ProfileEdit_NeedFrom");
        else if (string.IsNullOrWhiteSpace(Profile.FromName)) problem = Loc.T("ProfileEdit_NeedFromName");
        else if (string.IsNullOrWhiteSpace(Profile.SmtpHost)) problem = Loc.T("ProfileEdit_NeedHost");
        else if (!string.IsNullOrWhiteSpace(Profile.ReplyTo) && !EmailAddressRules.IsValid(Profile.ReplyTo)) problem = Loc.T("ProfileEdit_BadReplyTo");
        else if (Profile.Auth == AuthMethod.Password && string.IsNullOrWhiteSpace(Profile.Username)) problem = Loc.T("ProfileEdit_NeedUser");
        else if (Profile.DailyLimit <= 0 || Profile.HourlyLimit <= 0 || Profile.PerMinuteLimit <= 0) problem = Loc.T("ProfileEdit_BadLimits");
        return problem.Length == 0;
    }

    /// <summary>Gmail/Outlook send from the signed-in account, so an empty sender address defaults to the user name.</summary>
    private void FillFromAccount()
    {
        // Google shows app passwords in groups of four ("abcd efgh ijkl mnop").
        if (Profile.Kind == TransportKind.Gmail && Password.Contains(' ')) Password = Password.Replace(" ", "");
        if (!string.IsNullOrWhiteSpace(Profile.FromAddress) || !EmailAddressRules.IsValid(Profile.Username?.Trim() ?? "")) return;
        var copy = Clone(Profile);
        copy.FromAddress = copy.Username!.Trim();
        Profile = copy;
    }

    private static SenderProfile Clone(SenderProfile p)
        => System.Text.Json.JsonSerializer.Deserialize<SenderProfile>(System.Text.Json.JsonSerializer.Serialize(p))!;
}
