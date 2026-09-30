using Alpixa.Core.Models;
using Alpixa.Core.Options;

namespace Alpixa.App.Services;

public interface IDialogService
{
    Task AlertAsync(string title, string message);
    Task<bool> ConfirmAsync(string title, string message, string accept, string cancel);
    Task<string?> PromptAsync(string title, string message, string? initial = null);
    Task ShowErrorAsync(UserFacingError error);
    Task ToastAsync(string message);
}

public sealed class DialogService : IDialogService
{
    private static Page? CurrentPage => Shell.Current?.CurrentPage ?? Application.Current?.Windows.FirstOrDefault()?.Page;

    public Task AlertAsync(string title, string message)
        => MainThread.InvokeOnMainThreadAsync(() => CurrentPage?.DisplayAlertAsync(title, message, Loc.T("Common_Ok")) ?? Task.CompletedTask);

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel)
        => MainThread.InvokeOnMainThreadAsync(() => CurrentPage?.DisplayAlertAsync(title, message, accept, cancel) ?? Task.FromResult(false));

    public Task<string?> PromptAsync(string title, string message, string? initial = null)
        => MainThread.InvokeOnMainThreadAsync(() => CurrentPage?.DisplayPromptAsync(title, message, Loc.T("Common_Ok"), Loc.T("Common_Cancel"), initialValue: initial ?? "")
                                                    ?? Task.FromResult<string?>(null));

    public async Task ShowErrorAsync(UserFacingError error)
    {
        var page = CurrentPage;
        if (page is null) return;
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (error.HelpUrl is null)
            {
                await page.DisplayAlertAsync(error.Message, error.WhatToDo, Loc.T("Common_Ok"));
                return;
            }
            var open = await page.DisplayAlertAsync(error.Message, error.WhatToDo, Loc.T("Common_HowTo"), Loc.T("Common_Close"));
            if (open) await Browser.Default.OpenAsync(error.HelpUrl, BrowserLaunchMode.External);
        });
    }

    public Task ToastAsync(string message)
        => MainThread.InvokeOnMainThreadAsync(() => CommunityToolkit.Maui.Alerts.Toast.Make(message).Show());
}

public sealed class HelpService(AlpixaOptions options, IDialogService dialogs)
{
    private static readonly Dictionary<string, string> Anchors = new()
    {
        ["Dashboard"] = "1-bu-uygulama-nedir",
        ["Profiles"] = "7-e-posta-hesabı-bağlama",
        ["ProfileEdit"] = "7-e-posta-hesabı-bağlama",
        ["DomainHealth"] = "6-dns-kayıtları-rehberi",
        ["Lists"] = "8-liste-hazırlama",
        ["Import"] = "8-liste-hazırlama",
        ["ListDetail"] = "8-liste-hazırlama",
        ["Templates"] = "9-ilk-kampanya",
        ["TemplateEdit"] = "11-spama-düşmemek-için-kontrol-listesi",
        ["Campaigns"] = "9-ilk-kampanya",
        ["CampaignWizard"] = "9-ilk-kampanya",
        ["CampaignDetail"] = "10-isınma-planı",
        ["Reports"] = "12-kurulum-sonrası-izleme",
        ["Settings"] = "14-sorun-giderme",
        ["Setup"] = "5-ilk-çalıştırma-ve-kurulum-sihirbazı"
    };

    public async Task ShowAsync(string key)
    {
        var open = await dialogs.ConfirmAsync(Loc.T("Help_Title"), Loc.T("Help_" + key), Loc.T("Help_OpenGuide"), Loc.T("Common_Close"));
        if (open && Anchors.TryGetValue(key, out var anchor))
            await Browser.Default.OpenAsync(options.HelpBaseUrl + anchor, BrowserLaunchMode.External);
    }
}
