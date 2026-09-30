using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class ListsPage : AlpixaPage
{
    private readonly ListsViewModel _viewModel;
    private Brush? _dropStroke;

    public ListsPage(ListsViewModel viewModel) : base(viewModel, "Lists")
    {
        _viewModel = viewModel;
        InitializeComponent();
        _dropStroke = DropZone.Stroke;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        _viewModel.OnDragOver(e.PlatformArgs);
        DropZone.Stroke = (Color)Application.Current!.Resources["Accent"];
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
        DropZone.Stroke = _dropStroke;
    }

    private async void OnDrop(object? sender, DropEventArgs e)
    {
        DropZone.Stroke = _dropStroke;
        e.Handled = true;
        await _viewModel.OnDropAsync(e.PlatformArgs);
    }
}
