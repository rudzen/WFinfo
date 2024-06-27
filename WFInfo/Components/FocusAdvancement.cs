using System.Windows;
using System.Windows.Input;

namespace WFInfo.Components;

public static class FocusAdvancement
{
    public static bool GetAdvancesByEnterKey(DependencyObject obj)
    {
        return (bool)obj.GetValue(AdvancesByEnterKeyProperty);
    }

    public static void SetAdvancesByEnterKey(DependencyObject obj, bool value)
    {
        obj.SetValue(AdvancesByEnterKeyProperty, value);
    }

    public static readonly DependencyProperty AdvancesByEnterKeyProperty =
        DependencyProperty.RegisterAttached("AdvancesByEnterKey", typeof(bool), typeof(FocusAdvancement),
            new UIPropertyMetadata(OnAdvancesByEnterKeyPropertyChanged));

    public static readonly DependencyProperty FocusUIElementProperty =
        DependencyProperty.RegisterAttached("FocusUIElement", typeof(UIElement), typeof(FocusAdvancement),
            new UIPropertyMetadata(null));

    static void OnAdvancesByEnterKeyPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        if (e.NewValue is true)
            element.KeyDown += Keydown;
        else
            element.KeyDown -= Keydown;
    }

    static void Keydown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        Keyboard.ClearFocus();
    }
}
