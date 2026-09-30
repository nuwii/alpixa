using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Content;
using Alpixa.Core.Models;
using Contact = Alpixa.Core.Models.Contact;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Settings;
using Alpixa.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed partial class TemplatesViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<TemplatesViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    AssetStore assets) : BaseViewModel(startup, dialogs, logger)
{
    public ObservableCollection<EmailTemplate> Templates { get; } = new();

    protected override async Task LoadAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var items = await db.Templates.AsNoTracking().OrderByDescending(t => t.IsBuiltIn).ThenBy(t => t.Name).ToListAsync();
        Templates.Clear();
        foreach (var t in items) Templates.Add(t);
    }

    [RelayCommand] private Task Add() => Shell.Current.GoToAsync(AppShell.TemplateEditRoute);

    [RelayCommand]
    private Task Edit(EmailTemplate template)
        => Shell.Current.GoToAsync(AppShell.TemplateEditRoute, new Dictionary<string, object> { ["id"] = template.Id });

    [RelayCommand]
    private Task Duplicate(EmailTemplate template) => RunBusyAsync(async () =>
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var copy = new EmailTemplate
        {
            Name = Loc.T("Templates_CopyName", template.Name),
            Subject = template.Subject,
            HtmlBody = template.HtmlBody,
            TextBody = template.TextBody
        };
        db.Templates.Add(copy);
        await db.SaveChangesAsync();
        await assets.CopyAttachmentsAsync(template.Id, copy.Id);
        Templates.Add(copy);
    });

    [RelayCommand]
    private async Task Delete(EmailTemplate template)
    {
        if (!await Dialogs.ConfirmAsync(Loc.T("Common_Delete"), Loc.T("Templates_DeleteConfirm", template.Name), Loc.T("Common_Delete"), Loc.T("Common_Cancel")))
            return;
        await RunBusyAsync(async () =>
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            await db.Templates.Where(t => t.Id == template.Id).ExecuteDeleteAsync();
            Templates.Remove(template);
        });
    }
}

public sealed record AttachmentItem(int Id, string FileName, string SizeText, string Extension);

public sealed partial class TemplateEditViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<TemplateEditViewModel> logger,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    ITemplateRenderer renderer,
    AssetStore assets,
    SettingsStore settings,
    IPlatformService platform) : BaseViewModel(startup, dialogs, logger), IQueryAttributable
{
    private int _id;
    private bool _loaded;
    private CancellationTokenSource? _previewDebounce;

    public ObservableCollection<LintIssue> Issues { get; } = new();
    public ObservableCollection<AttachmentItem> Attachments { get; } = new();

    /// <summary>Set by the page so inserted images land where the user's cursor is.</summary>
    public Func<int>? CursorProvider { get; set; }

    /// <summary>Raised after an image is inserted, with the new cursor position.</summary>
    public event Action<int>? ImageInserted;

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Subject { get; set; } = "";
    [ObservableProperty] public partial string HtmlBody { get; set; } = "";
    [ObservableProperty] public partial string? TextBody { get; set; }
    [ObservableProperty] public partial string PreviewHtml { get; set; } = "";
    [ObservableProperty] public partial string PreviewSubject { get; set; } = "";
    [ObservableProperty] public partial double PreviewWidth { get; set; } = 640;
    [ObservableProperty] public partial bool IsMobilePreview { get; set; }
    [ObservableProperty] public partial bool ShowTextVersion { get; set; }
    [ObservableProperty] public partial string? TemplateError { get; set; }
    [ObservableProperty] public partial bool IsDragOver { get; set; }
    [ObservableProperty] public partial string AttachmentSummary { get; set; } = "";
    [ObservableProperty] public partial double AttachmentUsage { get; set; }
    [ObservableProperty] public partial bool AttachmentNearLimit { get; set; }
    [ObservableProperty] public partial string LimitsHint { get; set; } = "";

    public bool HasAttachments => Attachments.Count > 0;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => _id = query.TryGetValue("id", out var id) ? Convert.ToInt32(id) : 0;

    protected override async Task LoadAsync()
    {
        if (_loaded) return;
        _loaded = true;
        if (_id != 0)
        {
            await using var db = await dbFactory.CreateDbContextAsync();
            var t = await db.Templates.AsNoTracking().FirstAsync(x => x.Id == _id);
            Name = t.Name;
            Subject = t.Subject;
            HtmlBody = t.HtmlBody;
            TextBody = t.TextBody;
        }
        else
        {
            var starter = BuiltInTemplates.Create().Last();
            Name = Loc.T("Templates_NewName");
            Subject = starter.Subject;
            HtmlBody = starter.HtmlBody;
        }
        await ReloadAttachmentsAsync();
        RefreshPreview();
    }

    partial void OnHtmlBodyChanged(string value) => SchedulePreview();
    partial void OnSubjectChanged(string value) => SchedulePreview();

    private void SchedulePreview()
    {
        if (!_loaded) return;
        _previewDebounce?.Cancel();
        var cts = _previewDebounce = new CancellationTokenSource();
        _ = Task.Delay(700, cts.Token).ContinueWith(t =>
        {
            if (!t.IsCanceled) MainThread.BeginInvokeOnMainThread(RefreshPreview);
        }, TaskScheduler.Default);
    }

    [RelayCommand]
    private void RefreshPreview()
    {
        var sample = new Contact { Email = "ayse@ornek.com", EmailNormalized = "ayse@ornek.com", FirstName = "Ayşe", LastName = "Yılmaz", Company = "ABC Ltd" };
        var profile = new SenderProfile { FromName = Loc.T("Templates_SampleSender"), FromAddress = "bulten@ornek.com", PostalAddress = Loc.T("Templates_SampleAddress") };
        var model = MessageComposer.BuildModel(new Campaign { Name = Name }, profile, sample, "https://ornek.com/abonelik-iptal");
        try
        {
            PreviewSubject = renderer.Render(Subject, model);
            model["konu"] = PreviewSubject;
            PreviewHtml = assets.ToPreviewHtml(renderer.Render(HtmlBody, model, htmlEncodeValues: true));
            TemplateError = null;
        }
        catch (TemplateException ex)
        {
            TemplateError = Loc.T("Templates_Error", ex.Message);
        }

        Issues.Clear();
        foreach (var issue in ContentLinter.Lint(Subject, HtmlBody, Attachments.Count > 0)) Issues.Add(issue);
    }

    [RelayCommand]
    private void DesktopPreview()
    {
        PreviewWidth = 640;
        IsMobilePreview = false;
    }

    [RelayCommand]
    private void MobilePreview()
    {
        PreviewWidth = 375;
        IsMobilePreview = true;
    }

    [RelayCommand]
    private async Task PickImage()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("TemplateEdit_PickImage"), FileTypes = FilePickerFileType.Images });
        if (file is not null) await AddFilesAsync([file.FullPath]);
    }

    [RelayCommand]
    private async Task PickAttachment()
    {
        var files = await FilePicker.Default.PickMultipleAsync(new PickOptions { PickerTitle = Loc.T("TemplateEdit_PickAttachment") });
        var paths = files?.Where(f => f is not null).Select(f => f!.FullPath).ToList() ?? [];
        if (paths.Count > 0) await AddFilesAsync(paths, forceAttachment: true);
    }

    public void OnDragOver(object? platformArgs)
    {
        IsDragOver = true;
        if (platformArgs is not null) platform.AcceptDragOver(platformArgs);
    }

    public async Task OnDropAsync(object? platformArgs)
    {
        IsDragOver = false;
        if (platformArgs is null) return;
        var files = await platform.GetDroppedFilePathsAsync(platformArgs);
        if (files.Count > 0) await AddFilesAsync(files);
    }

    /// <summary>Images go into the email body; every other file becomes an attachment.</summary>
    public Task AddFilesAsync(IReadOnlyList<string> paths, bool forceAttachment = false) => RunBusyAsync(async () =>
    {
        var added = new List<string>();
        foreach (var path in paths)
        {
            if (!forceAttachment && AssetStore.IsImage(path))
            {
                var stored = await assets.ImportImageAsync(path);
                var (html, cursor) = AssetStore.InsertAt(HtmlBody, assets.ImageTag(stored, Path.GetFileNameWithoutExtension(path)), CursorProvider?.Invoke() ?? -1);
                HtmlBody = html;
                ImageInserted?.Invoke(cursor);
                added.Add(Path.GetFileName(path));
            }
            else
            {
                if (_id == 0) await PersistAsync();
                await assets.AddAttachmentAsync(_id, path);
                added.Add(Path.GetFileName(path));
            }
        }
        await ReloadAttachmentsAsync();
        RefreshPreview();
        if (added.Count > 0) await Dialogs.ToastAsync(Loc.T("TemplateEdit_Added", string.Join(", ", added)));
    }, Loc.T("TemplateEdit_Adding"));

    [RelayCommand]
    private Task RemoveAttachment(AttachmentItem item) => RunBusyAsync(async () =>
    {
        await assets.RemoveAttachmentAsync(item.Id);
        await ReloadAttachmentsAsync();
        RefreshPreview();
    });

    private async Task ReloadAttachmentsAsync()
    {
        var limits = await settings.GetSendingAsync();
        var items = _id == 0 ? [] : await assets.ListAsync(_id);
        Attachments.Clear();
        foreach (var a in items)
            Attachments.Add(new AttachmentItem(a.Id, a.FileName, AssetStore.FormatSize(a.SizeBytes),
                Path.GetExtension(a.FileName).TrimStart('.').ToUpperInvariant() is { Length: > 0 } ext ? ext[..Math.Min(4, ext.Length)] : "?"));
        var total = items.Sum(a => a.SizeBytes);
        var limit = limits.MaxAttachmentMb * 1024L * 1024L;
        AttachmentUsage = limit == 0 ? 0 : Math.Min(1, (double)total / limit);
        AttachmentNearLimit = AttachmentUsage >= 0.8;
        AttachmentSummary = Loc.T("TemplateEdit_AttachmentSummary", items.Count, AssetStore.FormatSize(total), limits.MaxAttachmentMb);
        LimitsHint = Loc.T("TemplateEdit_DropHint", AssetStore.FormatSize(limits.MaxImageKb * 1024L), limits.MaxAttachmentMb);
        OnPropertyChanged(nameof(HasAttachments));
    }

    [RelayCommand]
    private Task Save() => RunBusyAsync(async () =>
    {
        await PersistAsync();
        await Shell.Current.GoToAsync("..");
    });

    private async Task PersistAsync()
    {
        var errors = renderer.Validate(Subject).Concat(renderer.Validate(HtmlBody)).ToList();
        if (errors.Count > 0) throw new InvalidOperationException(Loc.T("Templates_Error", string.Join("\n", errors)));
        if (string.IsNullOrWhiteSpace(Name)) throw new InvalidOperationException(Loc.T("Templates_NeedName"));

        await using var db = await dbFactory.CreateDbContextAsync();
        EmailTemplate template;
        if (_id == 0)
        {
            template = new EmailTemplate();
            db.Templates.Add(template);
        }
        else
        {
            template = await db.Templates.FirstAsync(t => t.Id == _id);
        }
        template.Name = Name.Trim();
        template.Subject = Subject;
        template.HtmlBody = HtmlBody;
        template.TextBody = string.IsNullOrWhiteSpace(TextBody) ? null : TextBody;
        template.UpdatedUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        _id = template.Id;
    }

    public override void OnDisappearing() => _previewDebounce?.Cancel();
}
