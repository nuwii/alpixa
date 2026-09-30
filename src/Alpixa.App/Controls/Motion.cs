namespace Alpixa.App.Controls;

/// <summary>Screen transitions: staggered fade-up entrance for pages and a slide for wizard steps.</summary>
public static class Motion
{
    private const int MaxStaggered = 10;

    public static void PrepareEntrance(Page page)
    {
        foreach (var item in EntranceTargets(page))
        {
            item.AbortAnimation("entrance");
            item.Opacity = 0;
            item.TranslationY = 18;
        }
    }

    public static void PlayEntrance(Page page)
    {
        var index = 0;
        foreach (var item in EntranceTargets(page))
        {
            var delay = Math.Min(index++, MaxStaggered) * 45;
            var start = item.TranslationY;
            var animation = new Animation();
            animation.Add(0, 1, new Animation(v => item.Opacity = v, 0, 1, Easing.CubicOut));
            animation.Add(0, 1, new Animation(v => item.TranslationY = v, start, 0, Easing.CubicOut));
            item.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(delay),
                () => animation.Commit(item, "entrance", 16, 420, finished: (_, _) => { item.Opacity = 1; item.TranslationY = 0; }));
        }
    }

    /// <summary>Slides a wizard step in from the direction of travel.</summary>
    public static void SlideIn(VisualElement element, bool forward)
    {
        element.AbortAnimation("step");
        var from = forward ? 48 : -48;
        element.TranslationX = from;
        element.Opacity = 0;
        var animation = new Animation();
        animation.Add(0, 1, new Animation(v => element.TranslationX = v, from, 0, Easing.SpringOut));
        animation.Add(0, 0.7, new Animation(v => element.Opacity = v, 0, 1, Easing.CubicOut));
        animation.Commit(element, "step", 16, 460, finished: (_, _) => { element.TranslationX = 0; element.Opacity = 1; });
    }

    /// <summary>A short pop used to confirm an action (for example after a file is added).</summary>
    public static async Task PopAsync(VisualElement element)
    {
        await element.ScaleToAsync(1.04, 110, Easing.CubicOut);
        await element.ScaleToAsync(1, 180, Easing.SpringOut);
    }

    private static IEnumerable<VisualElement> EntranceTargets(Page page)
    {
        if (page is not ContentPage { Content: { } content }) return [];
        var root = content is ScrollView { Content: { } inner } ? inner : content;
        if (root is Layout layout && layout.Count > 1 && layout is not Grid { ColumnDefinitions.Count: > 1 })
            return layout.OfType<VisualElement>().Take(MaxStaggered + 4).ToList();
        if (root is Layout wide)
            return wide.OfType<VisualElement>().Take(MaxStaggered + 4).ToList();
        return [root];
    }
}
