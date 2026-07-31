using System.Windows;
using System.Windows.Controls;

namespace ClaudeTray;

/// <summary>
/// A settings "row" in the Fluent <c>SettingsCard</c> / list-row pattern: a leading title and an
/// optional wrapping description on the left, with the trailing control — the
/// <see cref="ContentControl.Content"/> (a switch, slider, text field…) — right-aligned on the
/// right. Lets the settings pages read as scannable label↔control pairs instead of stacked blocks.
///
/// It is a lookless control: the visual tree lives in the implicit Style for this type in
/// <c>SettingsWindow.xaml</c>. The trailing control is just this control's XAML child.
/// </summary>
internal sealed class SettingsRow : ContentControl
{
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(SettingsRow),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingsRow),
            new PropertyMetadata(string.Empty));

    /// <summary>The bold title shown on the left.</summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>Secondary wrapping caption under the title; the row hides it when empty.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }
}
