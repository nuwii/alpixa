using Alpixa.App.Views;
using Microsoft.Maui.Controls.Shapes;
using Path = Microsoft.Maui.Controls.Shapes.Path;

namespace Alpixa.App.Controls;

/// <summary>Page title block: gradient icon, eyebrow, title, subtitle, actions, back and help buttons.</summary>
public sealed class PageHeader : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(PageHeader), null,
        propertyChanged: (b, _, n) => ((PageHeader)b)._title.Text = n as string);

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(PageHeader), null,
        propertyChanged: (b, _, n) => ((PageHeader)b).SetText(((PageHeader)b)._subtitle, n as string));

    public static readonly BindableProperty EyebrowProperty = BindableProperty.Create(nameof(Eyebrow), typeof(string), typeof(PageHeader), null,
        propertyChanged: (b, _, n) => ((PageHeader)b).SetText(((PageHeader)b)._eyebrow, (n as string)?.ToUpper(System.Globalization.CultureInfo.CurrentUICulture)));

    public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(string), typeof(PageHeader), null,
        propertyChanged: (b, _, n) => ((PageHeader)b).UpdateIcon(n as string));

    public static readonly BindableProperty ActionsProperty = BindableProperty.Create(nameof(Actions), typeof(View), typeof(PageHeader), null,
        propertyChanged: (b, o, n) => ((PageHeader)b).UpdateActions(o as View, n as View));

    private readonly Label _eyebrow = new() { IsVisible = false };
    private readonly Label _title = new();
    private readonly Label _subtitle = new() { IsVisible = false };
    private readonly Border _badge;
    private readonly Path _iconPath;
    private readonly Button _back;
    private readonly HorizontalStackLayout _actions = new() { Spacing = 10, VerticalOptions = LayoutOptions.Center };

    public PageHeader()
    {
        var resources = Application.Current!.Resources;
        _eyebrow.Style = (Style)resources["Eyebrow"];
        _title.Style = (Style)resources["PageTitle"];
        _subtitle.Style = (Style)resources["MutedLabel"];
        _subtitle.FontSize = 14;

        _iconPath = new Path
        {
            Stroke = Colors.White, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Aspect = Stretch.Uniform
        };
        _badge = new Border
        {
            Style = (Style)resources["IconBadge"], WidthRequest = 52, HeightRequest = 52, Padding = 13, IsVisible = false,
            VerticalOptions = LayoutOptions.Center, Content = _iconPath,
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            Shadow = new Shadow { Brush = Color.FromArgb("#7C3AED"), Opacity = 0.35f, Radius = 18, Offset = new Point(0, 8) }
        };

        _back = new Button
        {
            Style = (Style)resources["IconButton"], Text = "←", IsVisible = false, VerticalOptions = LayoutOptions.Center,
            Command = new Command(async () => await Shell.Current.GoToAsync(".."))
        };
        var help = new Button
        {
            Style = (Style)resources["IconButton"], Text = "?", VerticalOptions = LayoutOptions.Center,
            Command = new Command(async () =>
            {
                if (FindPage() is AlpixaPage page) await page.ShowHelpAsync();
            })
        };
        SemanticProperties.SetDescription(help, "Yardım");
        _actions.Add(help);

        var texts = new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { _eyebrow, _title, _subtitle } };
        var grid = new Grid
        {
            ColumnDefinitions = { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 16,
            Margin = new Thickness(0, 0, 0, 6)
        };
        grid.Add(_back, 0);
        grid.Add(_badge, 1);
        grid.Add(texts, 2);
        grid.Add(_actions, 3);
        Content = grid;

        Loaded += (_, _) => _back.IsVisible = FindPage() is { Navigation.NavigationStack.Count: > 1 };
    }

    public string? Title { get => (string?)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => (string?)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public string? Eyebrow { get => (string?)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public View? Actions { get => (View?)GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }

    private void SetText(Label label, string? text)
    {
        label.Text = text;
        label.IsVisible = !string.IsNullOrWhiteSpace(text);
    }

    private void UpdateIcon(string? name)
    {
        var data = name is null ? null : typeof(Icons).GetField(name)?.GetValue(null) as string;
        _badge.IsVisible = data is not null;
        if (data is not null) _iconPath.Data = Icons.Parse(data);
    }

    private void UpdateActions(View? oldView, View? newView)
    {
        if (oldView is not null) _actions.Remove(oldView);
        if (newView is not null) _actions.Insert(0, newView);
    }

    private Page? FindPage()
    {
        Element? e = this;
        while (e is not null and not Page) e = e.Parent;
        return e as Page;
    }
}

/// <summary>Help button for pages that do not use <see cref="PageHeader"/>.</summary>
public sealed class HelpButton : Button
{
    public HelpButton()
    {
        Style = (Style)Application.Current!.Resources["IconButton"];
        Text = "?";
        Command = new Command(async () =>
        {
            Element? e = this;
            while (e is not null and not AlpixaPage) e = e.Parent;
            if (e is AlpixaPage page) await page.ShowHelpAsync();
        });
    }
}
