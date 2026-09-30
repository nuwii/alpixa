namespace Alpixa.App.Controls;

/// <summary>Adds a subtle lift effect when the pointer is over a card.</summary>
public static class Hover
{
    public static readonly BindableProperty LiftProperty =
        BindableProperty.CreateAttached("Lift", typeof(bool), typeof(Hover), false, propertyChanged: OnLiftChanged);

    private static readonly BindableProperty RecognizerProperty =
        BindableProperty.CreateAttached("Recognizer", typeof(PointerGestureRecognizer), typeof(Hover), null);

    public static bool GetLift(BindableObject view) => (bool)view.GetValue(LiftProperty);
    public static void SetLift(BindableObject view, bool value) => view.SetValue(LiftProperty, value);

    private static void OnLiftChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not View view) return;
        if (view.GetValue(RecognizerProperty) is PointerGestureRecognizer existing)
        {
            view.GestureRecognizers.Remove(existing);
            view.ClearValue(RecognizerProperty);
        }
        if (newValue is not true) return;

        var recognizer = new PointerGestureRecognizer();
        recognizer.PointerEntered += (_, _) =>
        {
            view.AbortAnimation("hover");
            view.Animate("hover", new Animation(v => view.TranslationY = v, view.TranslationY, -3), 16, 180, Easing.CubicOut);
        };
        recognizer.PointerExited += (_, _) =>
        {
            view.AbortAnimation("hover");
            view.Animate("hover", new Animation(v => view.TranslationY = v, view.TranslationY, 0), 16, 220, Easing.CubicOut);
        };
        view.GestureRecognizers.Add(recognizer);
        view.SetValue(RecognizerProperty, recognizer);
    }
}
