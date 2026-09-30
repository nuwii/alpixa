using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class ReportsPage : AlpixaPage
{
    public ReportsPage(ReportsViewModel viewModel) : base(viewModel, "Reports")
    {
        InitializeComponent();
    }
}
