using System.Diagnostics;
using System.Runtime.InteropServices;
using H.NotifyIcon;
using Microsoft.UI.Windowing;
using Windows.ApplicationModel.DataTransfer;
using WinDataPackageOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation;
using Windows.Storage;

namespace Alpixa.App.Services;

public sealed partial class PlatformService
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    private TaskbarIcon? _tray;

    public override void SetKeepAwake(bool keepAwake)
        => MainThread.BeginInvokeOnMainThread(() =>
            SetThreadExecutionState(keepAwake ? ExecutionState.Continuous | ExecutionState.SystemRequired : ExecutionState.Continuous));

    public override void ShowNotification(string title, string message)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            EnsureTray();
            _tray?.ShowNotification(title, message);
        });

    public override void SetProgress(double? fraction)
        => MainThread.BeginInvokeOnMainThread(() =>
        {
            EnsureTray();
            if (_tray is not null)
                _tray.ToolTipText = fraction is null ? "Alpixa" : $"Alpixa - %{fraction.Value * 100:0}";
        });

    public override void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    public override void AcceptDragOver(object platformDragOverArgs)
    {
        if (platformDragOverArgs is PlatformDragEventArgs { DragEventArgs: { } args })
            args.AcceptedOperation = WinDataPackageOperation.Copy;
    }

    public override async Task<IReadOnlyList<string>> GetDroppedFilePathsAsync(object platformDropArgs)
    {
        if (platformDropArgs is not PlatformDropEventArgs { DragEventArgs: { } args }) return [];
        if (!args.DataView.Contains(StandardDataFormats.StorageItems)) return [];
        var items = await args.DataView.GetStorageItemsAsync();
        return items.OfType<StorageFile>().Select(f => f.Path).ToList();
    }

    private void EnsureTray()
    {
        if (_tray is not null) return;
        _tray = new TaskbarIcon
        {
            ToolTipText = "Alpixa",
            NoLeftClickDelay = true,
            IconSource = new GeneratedIconSource
            {
                Text = "A",
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, 0x63, 0x66, 0xF1))
            },
            LeftClickCommand = new Command(RestoreMainWindow)
        };
        _tray.ForceCreate();
    }

    private static void RestoreMainWindow()
    {
        if (Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window window) return;
        if (window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
        window.Activate();
    }
}
