using Alpixa.App.Services;

namespace Alpixa.App;

public partial class App : Application
{
    private const double MinWidth = 1100;
    private const double MinHeight = 700;

    private readonly IServiceProvider _services;
    private readonly AppStartup _startup;

    public App(IServiceProvider services, AppStartup startup)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Unspecified;
        _services = services;
        _startup = startup;
        _startup.Start();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        AppShell shell;
        try
        {
            shell = _services.GetRequiredService<AppShell>();
        }
        catch (Exception ex)
        {
            Serilog.Log.Fatal(ex, "Could not build the main window");
            Serilog.Log.CloseAndFlush();
            throw;
        }
        var window = new Window(shell)
        {
            Title = "Alpixa",
            MinimumWidth = MinWidth,
            MinimumHeight = MinHeight,
            Width = Math.Max(MinWidth, Preferences.Default.Get("window.width", 1280d)),
            Height = Math.Max(MinHeight, Preferences.Default.Get("window.height", 820d))
        };

        var x = Preferences.Default.Get("window.x", double.NaN);
        var y = Preferences.Default.Get("window.y", double.NaN);
        if (!double.IsNaN(x) && !double.IsNaN(y) && x >= 0 && y >= 0)
        {
            window.X = x;
            window.Y = y;
        }

        window.SizeChanged += (_, _) => SaveWindowState(window);
        window.Destroying += async (_, _) =>
        {
            SaveWindowState(window);
            await _startup.StopAsync();
            Serilog.Log.CloseAndFlush();
        };
        var sized = false;
        window.Activated += (_, _) =>
        {
            if (sized) return;
            sized = true;
            MainThread.BeginInvokeOnMainThread(() => ApplyWindowSize(window));
        };
        window.Created += async (_, _) =>
        {
            if (await _startup.NeedsSetupAsync())
                await MainThread.InvokeOnMainThreadAsync(() => Shell.Current.GoToAsync(AppShell.SetupRoute));
        };
        return window;
    }

    private static void ApplyWindowSize(Window window)
    {
        var width = Math.Max(MinWidth, Preferences.Default.Get("window.width", 1280d));
        var height = Math.Max(MinHeight, Preferences.Default.Get("window.height", 820d));
        window.MinimumWidth = MinWidth;
        window.MinimumHeight = MinHeight;
        window.Width = width;
        window.Height = height;
#if MACCATALYST
        if (window.Handler?.PlatformView is not UIKit.UIWindow { WindowScene: { } scene }) return;

        if (scene.SizeRestrictions is { } restrictions)
            restrictions.MinimumSize = new CoreGraphics.CGSize(MinWidth, MinHeight);
        var frame = scene.CoordinateSpace.Bounds;
        var size = new CoreGraphics.CGSize(width, height);
        if (frame.Width < size.Width || frame.Height < size.Height)
            scene.RequestGeometryUpdate(
                new UIKit.UIWindowSceneGeometryPreferencesMac(new CoreGraphics.CGRect(new CoreGraphics.CGPoint(window.X is > 0 ? window.X : 80, window.Y is > 0 ? window.Y : 60), size)),
                _ => { });
#endif
    }

    private static void SaveWindowState(Window window)
    {
        if (window.Width >= MinWidth) Preferences.Default.Set("window.width", window.Width);
        if (window.Height >= MinHeight) Preferences.Default.Set("window.height", window.Height);
        if (!double.IsNaN(window.X)) Preferences.Default.Set("window.x", window.X);
        if (!double.IsNaN(window.Y)) Preferences.Default.Set("window.y", window.Y);
    }
}
