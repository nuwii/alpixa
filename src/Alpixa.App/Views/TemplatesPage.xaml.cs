using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class TemplatesPage : AlpixaPage
{
    public TemplatesPage(TemplatesViewModel viewModel) : base(viewModel, "Templates")
    {
        InitializeComponent();
    }
}
