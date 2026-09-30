using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class ListDetailPage : AlpixaPage
{
    public ListDetailPage(ListDetailViewModel viewModel) : base(viewModel, "ListDetail")
    {
        InitializeComponent();
    }
}
