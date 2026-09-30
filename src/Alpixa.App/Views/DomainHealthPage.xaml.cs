using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class DomainHealthPage : AlpixaPage
{
    public DomainHealthPage(DomainHealthViewModel viewModel) : base(viewModel, "DomainHealth")
    {
        InitializeComponent();
    }
}
