namespace Alpixa.App.Controls;

/// <summary>A segmented progress indicator for wizards.</summary>
public sealed class StepBar : ContentView
{
    public static readonly BindableProperty StepProperty =
        BindableProperty.Create(nameof(Step), typeof(int), typeof(StepBar), 1, propertyChanged: (b, _, _) => ((StepBar)b).Update(true));

    public static readonly BindableProperty CountProperty =
        BindableProperty.Create(nameof(Count), typeof(int), typeof(StepBar), 1, propertyChanged: (b, _, _) => ((StepBar)b).Rebuild());

    private readonly Grid _grid = new() { ColumnSpacing = 6, HeightRequest = 6 };

    public StepBar()
    {
        Content = _grid;
        Rebuild();
    }

    public int Step { get => (int)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }

    private void Rebuild()
    {
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        for (var i = 0; i < Count; i++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var track = new Grid { BackgroundColor = Track };
            track.Add(new BoxView { Background = Brand, AnchorX = 0, ScaleX = 0 });
            _grid.Add(new Border
            {
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 3 },
                Content = track
            }, i);
        }
        Update(false);
    }

    private void Update(bool animate)
    {
        for (var i = 0; i < _grid.Children.Count; i++)
        {
            if (_grid.Children[i] is not Border { Content: Grid { Children: [BoxView fill] } }) continue;
            var target = i < Step ? 1d : 0d;
            fill.AbortAnimation("fill");
            if (!animate || fill.ScaleX == target)
            {
                fill.ScaleX = target;
                continue;
            }
            var delay = Math.Abs(i - (Step - 1)) * 60;
            var from = fill.ScaleX;
            fill.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delay), () =>
                fill.Animate("fill", new Animation(v => fill.ScaleX = v, from, target, Easing.CubicInOut), 16, 380));
        }
    }

    private static Brush Brand => (Brush)Application.Current!.Resources["BrandGradient"];

    private static Color Track => Application.Current!.RequestedTheme == AppTheme.Dark ? Color.FromArgb("#222838") : Color.FromArgb("#E5E7F0");
}
