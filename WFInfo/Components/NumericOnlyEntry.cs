using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WFInfo.Components;

public static class NumericOnlyEntry
{
    private static string GetText(TextBox textBox)
    {
        return (string)textBox.GetValue(RegexFilterProperty);
    }

    public static void SetRegexFilter(TextBox textBox, string value)
    {
        textBox.SetValue(RegexFilterProperty, value);
    }

    public static readonly DependencyProperty RegexFilterProperty =
        DependencyProperty.RegisterAttached(
            "RegexFilter",
            typeof(string),
            typeof(NumericOnlyEntry),
            new UIPropertyMetadata(null, OnRegexFilterChanged));

    private static void OnRegexFilterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        if (e.NewValue is string)
            element.PreviewTextInput += PreviewTextInputHandler;
        else
            element.PreviewTextInput -= PreviewTextInputHandler;
    }

    static void PreviewTextInputHandler(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        string text;
        if (textBox.Text.Length < textBox.CaretIndex)
            text = textBox.Text;
        //  Remaining text after removing selected text.
        else
        {
            text = TreatSelectedText(textBox, out var remainingTextAfterRemoveSelection)
                ? remainingTextAfterRemoveSelection.Insert(textBox.SelectionStart, e.Text)
                : textBox.Text.Insert(textBox.CaretIndex, e.Text);
        }

        e.Handled = !ValidateText(GetText(textBox), text);
    }

    /// <summary>
    ///     Handle text selection
    /// </summary>
    /// <returns>true if the character was successfully removed; otherwise, false. </returns>
    private static bool TreatSelectedText(TextBox textBox, [NotNullWhen(true)] out string? text)
    {
        text = default;
        if (textBox.SelectionLength <= 0)
            return false;

        text = string.Empty;

        var length = textBox.Text.Length;
        if (textBox.SelectionStart >= length)
            return true;

        if (textBox.SelectionStart + textBox.SelectionLength >= length)
            textBox.SelectionLength = length - textBox.SelectionStart;

        text = textBox.Text.Remove(textBox.SelectionStart, textBox.SelectionLength);
        return true;
    }

    private const int MaxLength = 10;

    /// <summary>
    ///     Validate certain text by our regular expression and text length conditions
    /// </summary>
    /// <param name="text"> Text for validation </param>
    /// <returns> True - valid, False - invalid </returns>
    private static bool ValidateText(string regex, string text)
    {
        return new Regex(regex, RegexOptions.IgnoreCase).IsMatch(text) &&
               (MaxLength == int.MinValue || text.Length <= MaxLength);
    }
}
