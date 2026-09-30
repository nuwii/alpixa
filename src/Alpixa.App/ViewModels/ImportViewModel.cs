using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Alpixa.App.Services;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Contacts;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.ViewModels;

public sealed record ColumnChoice(int Index, string Name)
{
    public override string ToString() => Name;
}

public sealed record PreviewRow(string Text);

public sealed partial class ImportViewModel(
    AppStartup startup,
    IDialogService dialogs,
    ILogger<ImportViewModel> logger,
    ContactService contacts,
    ContactImportService importer,
    IPlatformService platform) : BaseViewModel(startup, dialogs, logger), IQueryAttributable
{
    private string? _pendingFile;
    private int? _pendingListId;

    public ObservableCollection<ContactListSummary> ExistingLists { get; } = new();
    public ObservableCollection<ColumnChoice> Columns { get; } = new();
    public ObservableCollection<PreviewRow> PreviewRows { get; } = new();

    [ObservableProperty] public partial string? FilePath { get; set; }
    [ObservableProperty] public partial string? PastedText { get; set; }
    [ObservableProperty] public partial bool CreateNewList { get; set; } = true;
    [ObservableProperty] public partial string NewListName { get; set; } = "";
    [ObservableProperty] public partial ContactListSummary? SelectedList { get; set; }
    [ObservableProperty] public partial bool ConsentConfirmed { get; set; }
    [ObservableProperty] public partial bool CheckMx { get; set; } = true;
    [ObservableProperty] public partial bool HasPreview { get; set; }
    [ObservableProperty] public partial ColumnChoice? EmailColumn { get; set; }
    [ObservableProperty] public partial ColumnChoice? FirstNameColumn { get; set; }
    [ObservableProperty] public partial ColumnChoice? LastNameColumn { get; set; }
    [ObservableProperty] public partial ColumnChoice? CompanyColumn { get; set; }
    [ObservableProperty] public partial ColumnChoice? ConsentSourceColumn { get; set; }
    [ObservableProperty] public partial ColumnChoice? ConsentDateColumn { get; set; }
    [ObservableProperty] public partial string? ProgressText { get; set; }
    [ObservableProperty] public partial ImportSummary? Result { get; set; }
    [ObservableProperty] public partial string? ResultText { get; set; }

    private ColumnMapping _guessed = new();

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("file", out var f)) _pendingFile = f as string;
        if (query.TryGetValue("listId", out var l)) _pendingListId = Convert.ToInt32(l);
    }

    protected override async Task LoadAsync()
    {
        ExistingLists.Clear();
        foreach (var l in await contacts.GetListSummariesAsync()) ExistingLists.Add(l);
        if (_pendingListId is { } listId)
        {
            SelectedList = ExistingLists.FirstOrDefault(l => l.Id == listId);
            CreateNewList = SelectedList is null;
            _pendingListId = null;
        }
        if (_pendingFile is not null)
        {
            var file = _pendingFile;
            _pendingFile = null;
            await LoadFileAsync(file);
        }
    }

    [RelayCommand]
    private async Task PickFile()
    {
        var types = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            [DevicePlatform.WinUI] = [".csv", ".xlsx", ".txt"],
            [DevicePlatform.MacCatalyst] = ["public.comma-separated-values-text", "org.openxmlformats.spreadsheetml.sheet", "public.plain-text"]
        });
        var file = await FilePicker.Default.PickAsync(new PickOptions { PickerTitle = Loc.T("Import_PickTitle"), FileTypes = types });
        if (file is not null) await LoadFileAsync(file.FullPath);
    }

    [RelayCommand]
    private Task PreviewPasted() => RunBusyAsync(() =>
    {
        FilePath = null;
        BuildPreview(TabularReader.ReadText(PastedText ?? ""));
        if (string.IsNullOrWhiteSpace(NewListName)) NewListName = Loc.T("Import_PastedListName", DateTime.Now.ToString("g"));
        return Task.CompletedTask;
    });

    private Task LoadFileAsync(string path) => RunBusyAsync(() =>
    {
        FilePath = path;
        PastedText = null;
        BuildPreview(TabularReader.ReadFile(path));
        if (string.IsNullOrWhiteSpace(NewListName)) NewListName = Path.GetFileNameWithoutExtension(path);
        return Task.CompletedTask;
    });

    private void BuildPreview(IEnumerable<string[]> rows)
    {
        var preview = importer.Preview(rows);
        _guessed = preview.Mapping;
        Columns.Clear();
        Columns.Add(new ColumnChoice(-1, Loc.T("Import_ColumnNone")));
        for (var i = 0; i < preview.Headers.Count; i++) Columns.Add(new ColumnChoice(i, preview.Headers[i]));

        ColumnChoice Pick(int index) => Columns.First(c => c.Index == index);
        EmailColumn = Pick(preview.Mapping.EmailColumn);
        FirstNameColumn = Pick(preview.Mapping.FirstNameColumn);
        LastNameColumn = Pick(preview.Mapping.LastNameColumn);
        CompanyColumn = Pick(preview.Mapping.CompanyColumn);
        ConsentSourceColumn = Pick(preview.Mapping.ConsentSourceColumn);
        ConsentDateColumn = Pick(preview.Mapping.ConsentDateColumn);

        PreviewRows.Clear();
        PreviewRows.Add(new PreviewRow(string.Join("  |  ", preview.Headers)));
        foreach (var row in preview.SampleRows.Take(10)) PreviewRows.Add(new PreviewRow(string.Join("  |  ", row)));
        HasPreview = preview.Headers.Count > 0;
        Result = null;
        ResultText = null;
    }

    [RelayCommand]
    private Task Import() => RunBusyAsync(async () =>
    {
        if (!HasPreview) throw new InvalidOperationException(Loc.T("Import_NeedFile"));
        if (EmailColumn is null || EmailColumn.Index < 0) throw new InvalidOperationException(Loc.T("Import_NeedEmailColumn"));
        if (!ConsentConfirmed) throw new InvalidOperationException(Loc.T("Import_NeedConsent"));
        if (CreateNewList && string.IsNullOrWhiteSpace(NewListName)) throw new InvalidOperationException(Loc.T("Import_NeedListName"));
        if (!CreateNewList && SelectedList is null) throw new InvalidOperationException(Loc.T("Import_NeedList"));

        var mapping = new ColumnMapping
        {
            EmailColumn = EmailColumn.Index,
            FirstNameColumn = FirstNameColumn?.Index ?? -1,
            LastNameColumn = LastNameColumn?.Index ?? -1,
            CompanyColumn = CompanyColumn?.Index ?? -1,
            ConsentSourceColumn = ConsentSourceColumn?.Index ?? -1,
            ConsentDateColumn = ConsentDateColumn?.Index ?? -1
        };
        var used = new HashSet<int> { mapping.EmailColumn, mapping.FirstNameColumn, mapping.LastNameColumn, mapping.CompanyColumn, mapping.ConsentSourceColumn, mapping.ConsentDateColumn };
        foreach (var column in Columns.Where(c => c.Index >= 0 && !used.Contains(c.Index)))
        {
            var key = _guessed.CustomColumns.TryGetValue(column.Index, out var k) ? k : ColumnGuesser.ToFieldKey(column.Name);
            if (key.Length > 0) mapping.CustomColumns[column.Index] = key;
        }

        var listId = CreateNewList ? (await contacts.CreateListAsync(NewListName, ConsentConfirmed)).Id : SelectedList!.Id;
        var rows = FilePath is not null ? TabularReader.ReadFile(FilePath) : TabularReader.ReadText(PastedText ?? "");
        var progress = new Progress<int>(n => ProgressText = Loc.T("Import_Progress", n));

        var summary = await Task.Run(() => importer.ImportAsync(listId, rows, mapping, CheckMx, progress, CancellationToken.None));
        Result = summary;
        ResultText = Loc.T("Import_Result", summary.TotalRows, summary.Imported, summary.Duplicates, summary.InvalidSyntax,
            summary.Disposable, summary.RoleAddresses, summary.NoMx, summary.Suppressed, summary.MissingEmail);
        ProgressText = null;
        platform.ShowNotification(Loc.T("Import_DoneTitle"), Loc.T("Import_DoneBody", summary.Imported));
    }, Loc.T("Import_Working"));

    [RelayCommand]
    private Task Done() => Shell.Current.GoToAsync("//lists");
}
