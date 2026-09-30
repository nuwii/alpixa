namespace Alpixa.App.Controls;

/// <summary>Gives native text inputs rounded corners, inner padding and a focus ring.</summary>
public static class InputChrome
{
    public static void Configure()
    {
#if MACCATALYST || IOS
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("AlpixaChrome", (handler, view) =>
        {
            Style(handler.PlatformView, view);
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
            handler.PlatformView.LeftView = new UIKit.UIView(new CoreGraphics.CGRect(0, 0, 12, 1));
            handler.PlatformView.LeftViewMode = UIKit.UITextFieldViewMode.Always;
            handler.PlatformView.RightView = new UIKit.UIView(new CoreGraphics.CGRect(0, 0, 12, 1));
            handler.PlatformView.RightViewMode = UIKit.UITextFieldViewMode.Always;
        });
        Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("AlpixaChrome", (handler, view) =>
        {
            Style(handler.PlatformView, view);
            handler.PlatformView.BorderStyle = UIKit.UITextBorderStyle.None;
            handler.PlatformView.LeftView = new UIKit.UIView(new CoreGraphics.CGRect(0, 0, 12, 1));
            handler.PlatformView.LeftViewMode = UIKit.UITextFieldViewMode.Always;
        });
        Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("AlpixaChrome", (handler, view) =>
        {
            Style(handler.PlatformView, view);
            handler.PlatformView.TextContainerInset = new UIKit.UIEdgeInsets(10, 8, 10, 8);
        });
#endif
    }

#if MACCATALYST || IOS
    private static readonly CoreGraphics.CGColor Idle = new(0.5f, 0.52f, 0.6f, 0.28f);
    private static readonly CoreGraphics.CGColor Focus = new(0.39f, 0.4f, 0.945f, 0.9f);

    private static void Style(UIKit.UIView platform, IView view)
    {
        platform.Layer.CornerRadius = 10;
        platform.Layer.BorderWidth = 1;
        platform.Layer.BorderColor = Idle;
        platform.ClipsToBounds = true;
        if (view is not VisualElement element) return;
        element.Focused -= OnFocused;
        element.Unfocused -= OnUnfocused;
        element.Focused += OnFocused;
        element.Unfocused += OnUnfocused;
    }

    private static void OnFocused(object? sender, FocusEventArgs e) => SetBorder(sender, Focus, 1.5f);
    private static void OnUnfocused(object? sender, FocusEventArgs e) => SetBorder(sender, Idle, 1);

    private static void SetBorder(object? sender, CoreGraphics.CGColor color, float width)
    {
        if (sender is VisualElement { Handler.PlatformView: UIKit.UIView platform })
        {
            platform.Layer.BorderColor = color;
            platform.Layer.BorderWidth = width;
        }
    }
#endif
}
