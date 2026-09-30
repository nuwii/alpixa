#if MACCATALYST
using Foundation;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace Alpixa.App.Controls;

/// <summary>
/// Shows <see cref="Picker"/> as a native pop-up menu on the Mac instead of MAUI's default dialog
/// (which has an untranslated "Done" button).
/// </summary>
public sealed class MenuPickerHandler : ViewHandler<IPicker, UIButton>
{
    public static readonly IPropertyMapper<IPicker, MenuPickerHandler> PickerMapper = new PropertyMapper<IPicker, MenuPickerHandler>(ViewMapper)
    {
        [nameof(IPicker.Items)] = (h, _) => h.Rebuild(),
        [nameof(IPicker.SelectedIndex)] = (h, _) => h.Rebuild(),
        [nameof(IPicker.Title)] = (h, _) => h.Rebuild(),
        [nameof(ITextStyle.TextColor)] = (h, _) => h.Rebuild(),
        [nameof(ITextStyle.Font)] = (h, _) => h.Rebuild(),
    };

    private static readonly CoreGraphics.CGColor Idle = new(0.5f, 0.52f, 0.6f, 0.28f);

    public MenuPickerHandler() : base(PickerMapper)
    {
    }

    protected override UIButton CreatePlatformView()
    {
        var button = new UIButton(UIButtonType.System)
        {
            ShowsMenuAsPrimaryAction = true,
            HorizontalAlignment = UIControlContentHorizontalAlignment.Leading,
            ClipsToBounds = true
        };
        button.Layer.CornerRadius = 10;
        button.Layer.BorderWidth = 1;
        button.Layer.BorderColor = Idle;
        return button;
    }

    private void Rebuild()
    {
        if (VirtualView is null || PlatformView is null) return;
        var count = VirtualView.GetCount();
        var selected = VirtualView.SelectedIndex;
        var actions = new UIMenuElement[count];
        for (var i = 0; i < count; i++)
        {
            var index = i;
            var action = UIAction.Create(VirtualView.GetItem(i) ?? "", null, null, _ => VirtualView.SelectedIndex = index);
            action.State = i == selected ? UIMenuElementState.On : UIMenuElementState.Off;
            actions[i] = action;
        }
        PlatformView.Menu = UIMenu.Create("", null, UIMenuIdentifier.None, UIMenuOptions.SingleSelection, actions);

        var hasSelection = selected >= 0 && selected < count;
        var text = hasSelection ? VirtualView.GetItem(selected) : VirtualView.Title;
        var color = (hasSelection ? VirtualView.TextColor : (VirtualView as Picker)?.TitleColor ?? VirtualView.TextColor)?.ToPlatform() ?? UIColor.Label;
        var size = VirtualView.Font.Size > 0 ? VirtualView.Font.Size : 14;

        var config = UIButtonConfiguration.PlainButtonConfiguration;
        config.AttributedTitle = new NSAttributedString(text ?? "", new UIStringAttributes
        {
            Font = UIFont.SystemFontOfSize((nfloat)size),
            ForegroundColor = color
        });
        config.Image = UIImage.GetSystemImage("chevron.up.chevron.down",
            UIImageSymbolConfiguration.Create((nfloat)11, UIImageSymbolWeight.Semibold));
        config.ImagePlacement = NSDirectionalRectEdge.Trailing;
        config.ImagePadding = 10;
        config.BaseForegroundColor = color;
        config.ContentInsets = new NSDirectionalEdgeInsets(9, 12, 9, 12);
        config.TitleLineBreakMode = UILineBreakMode.TailTruncation;
        PlatformView.Configuration = config;
    }
}
#endif
