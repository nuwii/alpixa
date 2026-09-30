using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class DashboardPage : AlpixaPage
{
    public DashboardPage(DashboardViewModel viewModel) : base(viewModel, "Dashboard")
    {
        InitializeComponent();
    }
}
