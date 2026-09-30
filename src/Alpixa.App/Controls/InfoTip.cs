using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Maui.Views;
using Alpixa.App.Services;
using Microsoft.Maui.Controls.Shapes;

namespace Alpixa.App.Controls;

/// <summary>
/// A small "i" badge. Tapping it opens a card that explains, in plain language, what a field or
/// button is for and where to find the value. Texts come from Info_{Key}_What / Info_{Key}_How.
/// </summary>
public sealed class InfoTip : ContentView
{
    public static readonly BindableProperty KeyProperty = BindableProperty.Create(nameof(Key), typeof(string), typeof(InfoTip));
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(InfoTip));

    private readonly Border _badge;

    public InfoTip()
    {
        var resources = Application.Current!.Resources;
        _badge = new Border
        {
            WidthRequest = 18,
            HeightRequest = 18,
            StrokeThickness = 1,
            Stroke = (Color)resources["Primary"],
            StrokeShape = new RoundRectangle { CornerRadius = 9 },
            BackgroundColor = Application.Current.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#1E1F3A") : Color.FromArgb("#EEF0FF"),
            Padding = 0,
            Content = new Label
            {
                Text = "i",
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                FontFamily = "Georgia",
                TextColor = (Color)resources["Primary"],
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.NoWrap
            }
        };
        Content = _badge;
        VerticalOptions = LayoutOptions.Center;
        HorizontalOptions = LayoutOptions.Start;
        SemanticProperties.SetDescription(this, "Bilgi");

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) => await ShowAsync();
        GestureRecognizers.Add(tap);

        // Hidden until the pointer is over the field or button it belongs to (see Reveal/Conceal).
        Opacity = 0;
        Scale = 0.4;
        Rotation = -90;

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) =>
        {
            Reveal();
            _ = _badge.ScaleToAsync(1.2, 120, Easing.CubicOut);
        };
        pointer.PointerExited += (_, _) =>
        {
            _ = _badge.ScaleToAsync(1, 140, Easing.CubicOut);
            Conceal();
        };
        GestureRecognizers.Add(pointer);
    }

    private int _hoverCount;
    private bool _shown;

    /// <summary>Pops the badge in with a small spin and spring.</summary>
    public void Reveal()
    {
        _hoverCount++;
        if (_shown) return;
        _shown = true;
        this.AbortAnimation("tip");
        var anim = new Animation
        {
            { 0, 0.6, new Animation(v => Opacity = v, Opacity, 1, Easing.CubicOut) },
            { 0, 1, new Animation(v => Scale = v, Scale, 1, Easing.SpringOut) },
            { 0, 1, new Animation(v => Rotation = v, Rotation, 0, Easing.CubicOut) }
        };
        anim.Commit(this, "tip", 16, 380);
    }

    /// <summary>Fades the badge out shortly after the pointer leaves (so it can still be reached).</summary>
    public void Conceal()
    {
        var token = --_hoverCount;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(280), () =>
        {
            if (_hoverCount > 0 || !_shown || token != _hoverCount) return;
            _hoverCount = 0;
            _shown = false;
            this.AbortAnimation("tip");
            var anim = new Animation
            {
                { 0, 1, new Animation(v => Opacity = v, Opacity, 0, Easing.CubicIn) },
                { 0, 1, new Animation(v => Scale = v, Scale, 0.4, Easing.CubicIn) },
                { 0, 1, new Animation(v => Rotation = v, Rotation, 90, Easing.CubicIn) }
            };
            anim.Commit(this, "tip", 16, 220, finished: (_, _) => Rotation = -90);
        });
    }

    /// <summary>Makes hovering <paramref name="view"/> reveal this tip.</summary>
    public void RevealOnHover(View view)
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => Reveal();
        pointer.PointerExited += (_, _) => Conceal();
        view.GestureRecognizers.Add(pointer);
    }

    public string? Key { get => (string?)GetValue(KeyProperty); set => SetValue(KeyProperty, value); }
    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    private async Task ShowAsync()
    {
        if (string.IsNullOrWhiteSpace(Key) || Shell.Current?.CurrentPage is not { } page) return;
        var what = Loc.TryGet($"Info_{Key}_What") ?? Loc.TryGet($"Info_{Fallback(Key)}_What");
        if (what is null) return;
        var how = Loc.TryGet($"Info_{Key}_How") ?? Loc.TryGet($"Info_{Fallback(Key)}_How");
        await page.ShowPopupAsync(new InfoCard(Title ?? "", what, how), new PopupOptions
        {
            PageOverlayColor = Color.FromRgba(0, 0, 0, 0.45),
            Shape = new RoundRectangle { CornerRadius = 20, StrokeThickness = 0 },
            Shadow = new Shadow { Brush = Colors.Black, Opacity = 0.35f, Radius = 30, Offset = new Point(0, 12) }
        });
    }

    // "Profiles_Common_Delete" falls back to the generic "Common_Delete" text.
    private static string Fallback(string key) => key.Contains("_Common_") ? key[(key.IndexOf("_Common_", StringComparison.Ordinal) + 1)..] : key;
}

/// <summary>The explanation card shown by <see cref="InfoTip"/>.</summary>
public sealed class InfoCard : Popup
{
    public InfoCard(string title, string what, string? how)
    {
        var r = Application.Current!.Resources;
        var dark = Application.Current.RequestedTheme == AppTheme.Dark;
        BackgroundColor = dark ? (Color)r["CardDark"] : (Color)r["CardLight"];
        Padding = 0;
        Margin = new Thickness(24);

        var badge = new Border
        {
            Style = (Style)r["IconBadge"], WidthRequest = 40, HeightRequest = 40, Padding = 0,
            Content = new Label
            {
                Text = "i", FontSize = 20, FontAttributes = FontAttributes.Bold, FontFamily = "Georgia", TextColor = Colors.White,
                HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center
            }
        };
        var header = new Grid { ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Star) }, ColumnSpacing = 14 };
        header.Add(badge, 0);
        header.Add(new Label { Text = title, Style = (Style)r["SectionTitle"], FontSize = 18, VerticalOptions = LayoutOptions.Center }, 1);

        var body = new VerticalStackLayout { Spacing = 14, Padding = new Thickness(24, 22, 24, 20), WidthRequest = 460 };
        body.Add(header);
        body.Add(Section(Loc.T("Info_WhatTitle"), what, r, inset: false));
        if (!string.IsNullOrWhiteSpace(how))
            body.Add(Section(Loc.T("Info_HowTitle"), how!, r, inset: true));

        var ok = new Button { Text = Loc.T("Info_Ok"), HorizontalOptions = LayoutOptions.End, Margin = new Thickness(0, 4, 0, 0) };
        ok.Clicked += async (_, _) => await CloseAsync();
        body.Add(ok);

        Content = new ScrollView { Content = body, MaximumHeightRequest = 620 };
        body.Opacity = 0;
        body.Scale = 0.94;
        body.Loaded += async (_, _) =>
        {
            await Task.WhenAll(body.FadeToAsync(1, 180, Easing.CubicOut), body.ScaleToAsync(1, 240, Easing.SpringOut));
        };
    }

    private static View Section(string heading, string text, ResourceDictionary r, bool inset)
    {
        var stack = new VerticalStackLayout { Spacing = 4 };
        stack.Add(new Label { Text = heading.ToUpper(System.Globalization.CultureInfo.CurrentUICulture), Style = (Style)r["Eyebrow"] });
        stack.Add(new Label { Text = text, FontSize = 14, LineHeight = 1.3 });
        if (!inset) return stack;
        return new Border { Style = (Style)r["InsetBox"], Padding = new Thickness(14, 12), Content = stack };
    }
}

/// <summary>A label followed by an <see cref="InfoTip"/>. Wrap=true lets long text wrap and puts the tip on the right.</summary>
public sealed class InfoLabel : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(nameof(Text), typeof(string), typeof(InfoLabel),
        propertyChanged: (b, _, n) => ((InfoLabel)b).Update());
    public static readonly BindableProperty InfoProperty = BindableProperty.Create(nameof(Info), typeof(string), typeof(InfoLabel),
        propertyChanged: (b, _, n) => ((InfoLabel)b).Update());
    public static readonly BindableProperty LabelStyleProperty = BindableProperty.Create(nameof(LabelStyle), typeof(Style), typeof(InfoLabel),
        propertyChanged: (b, _, n) => ((InfoLabel)b)._label.Style = (Style?)n);
    public static readonly BindableProperty WrapProperty = BindableProperty.Create(nameof(Wrap), typeof(bool), typeof(InfoLabel), false,
        propertyChanged: (b, _, _) => ((InfoLabel)b).Build());

    private readonly Label _label = new() { VerticalOptions = LayoutOptions.Center };
    private readonly InfoTip _tip = new();

    public InfoLabel()
    {
        Build();
        _tip.RevealOnHover(this);
        Loaded += (_, _) => HookSibling();
    }

    private bool _hooked;

    // Hovering the input right below a field label also reveals its tip.
    private void HookSibling()
    {
        if (_hooked || Parent is not Layout layout) return;
        _hooked = true;
        var index = layout.IndexOf(this);
        if (index >= 0 && index + 1 < layout.Count && layout[index + 1] is View next and (InputView or Picker or Editor))
            _tip.RevealOnHover(next);
        if (Parent is Layout { Count: <= 3 } small && small.Parent is View)
            _tip.RevealOnHover(small);
    }

    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string? Info { get => (string?)GetValue(InfoProperty); set => SetValue(InfoProperty, value); }
    public Style? LabelStyle { get => (Style?)GetValue(LabelStyleProperty); set => SetValue(LabelStyleProperty, value); }
    public bool Wrap { get => (bool)GetValue(WrapProperty); set => SetValue(WrapProperty, value); }

    /// <summary>Forwards to the inner label so existing XAML attributes keep working.</summary>
    public FontAttributes FontAttributes { get => _label.FontAttributes; set => _label.FontAttributes = value; }
    public double FontSize { get => _label.FontSize; set => _label.FontSize = value; }
    public Color TextColor { get => _label.TextColor; set => _label.TextColor = value; }

    private void Update()
    {
        _label.Text = Text;
        _tip.Key = Info;
        _tip.Title = Text;
    }

    private void Build()
    {
        if (_label.Parent is Layout old) old.Remove(_label);
        if (_tip.Parent is Layout oldTip) oldTip.Remove(_tip);
        if (Wrap)
        {
            // FlexLayout lets long text wrap while keeping the tip right after it.
            var flex = new FlexLayout { Direction = Microsoft.Maui.Layouts.FlexDirection.Row, Wrap = Microsoft.Maui.Layouts.FlexWrap.NoWrap, AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center };
            FlexLayout.SetShrink(_label, 1);
            FlexLayout.SetShrink(_tip, 0);
            _tip.Margin = new Thickness(7, 0, 0, 0);
            flex.Add(_label);
            flex.Add(_tip);
            Content = flex;
        }
        else
        {
            Content = new HorizontalStackLayout { Spacing = 7, Children = { _label, _tip } };
        }
    }
}

/// <summary>Puts a small <see cref="InfoTip"/> on the top-right corner of a button.</summary>
[ContentProperty(nameof(Child))]
public sealed class WithInfo : ContentView
{
    public static readonly BindableProperty InfoProperty = BindableProperty.Create(nameof(Info), typeof(string), typeof(WithInfo),
        propertyChanged: (b, _, _) => ((WithInfo)b).Update());

    private readonly Grid _grid = new();
    private readonly InfoTip _tip = new() { HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Start, TranslationX = 7, TranslationY = -7, ZIndex = 10 };
    private View? _child;

    public WithInfo()
    {
        _grid.Add(_tip);
        Content = _grid;
        _tip.RevealOnHover(_grid);
    }

    public string? Info { get => (string?)GetValue(InfoProperty); set => SetValue(InfoProperty, value); }

    public View? Child
    {
        get => _child;
        set
        {
            if (_child is not null) _grid.Remove(_child);
            _child = value;
            if (value is not null) _grid.Insert(0, value);
            Update();
        }
    }

    private void Update()
    {
        _tip.Key = Info;
        _tip.Title = (_child as Button)?.Text;
        if (_child is Button b)
        {
            b.PropertyChanged -= OnChildChanged;
            b.PropertyChanged += OnChildChanged;
        }
    }

    private void OnChildChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Button.Text)) _tip.Title = (_child as Button)?.Text;
    }
}
