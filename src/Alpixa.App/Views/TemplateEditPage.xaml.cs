using Alpixa.App.Controls;
using Alpixa.App.ViewModels;

namespace Alpixa.App.Views;

public partial class TemplateEditPage : AlpixaPage
{
    private readonly TemplateEditViewModel _viewModel;
    private Brush? _dropStroke;

    public TemplateEditPage(TemplateEditViewModel viewModel) : base(viewModel, "TemplateEdit")
    {
        _viewModel = viewModel;
        InitializeComponent();
        _dropStroke = DropZone.Stroke;
        viewModel.CursorProvider = () => HtmlEditor.IsFocused || HtmlEditor.CursorPosition > 0 ? HtmlEditor.CursorPosition : -1;
        viewModel.ImageInserted += cursor => Dispatcher.Dispatch(async () =>
        {
            HtmlEditor.CursorPosition = Math.Min(cursor, HtmlEditor.Text?.Length ?? 0);
            await Motion.PopAsync(PreviewFrame);
        });
        viewModel.Attachments.CollectionChanged += async (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add) await Motion.PopAsync(AttachmentsCard);
        };
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.Copy;
        _viewModel.OnDragOver(e.PlatformArgs);
        DropZone.Stroke = (Color)Application.Current!.Resources["Accent"];
        _ = DropZone.ScaleToAsync(1.015, 120, Easing.CubicOut);
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        _viewModel.IsDragOver = false;
        ResetDropZone();
    }

    private async void OnDrop(object? sender, DropEventArgs e)
    {
        e.Handled = true;
        ResetDropZone();
        await _viewModel.OnDropAsync(e.PlatformArgs);
    }

    private void ResetDropZone()
    {
        DropZone.Stroke = _dropStroke;
        _ = DropZone.ScaleToAsync(1, 160, Easing.CubicOut);
    }
}
