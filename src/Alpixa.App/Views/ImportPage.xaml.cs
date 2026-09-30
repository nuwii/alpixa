using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class ImportPage : AlpixaPage
{
    public ImportPage(ImportViewModel viewModel) : base(viewModel, "Import")
    {
        InitializeComponent();
        viewModel.PropertyChanged += async (_, e) =>
        {
            if (e.PropertyName != nameof(ImportViewModel.ResultText) || viewModel.ResultText is null) return;
            await Task.Delay(100);
            await PageScroll.ScrollToAsync(ResultCard, ScrollToPosition.End, true);
        };
    }
}
