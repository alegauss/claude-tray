using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

// Both UI stacks are referenced (see AGENTS.md), so these bare names are ambiguous with their
// System.Windows.Forms twins. Nothing WinForms belongs in a WPF row.
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;

namespace ClaudeTray;

/// <summary>
/// A settings "row" in the Fluent <c>SettingsCard</c> / list-row pattern: a leading title and an
/// optional wrapping description on the left, with the trailing control — the
/// <see cref="ContentControl.Content"/> (a switch, slider, text field…) — right-aligned on the
/// right. Lets the settings pages read as scannable label↔control pairs instead of stacked blocks.
///
/// It is a lookless control: the visual tree lives in the implicit Style for this type in
/// <c>SettingsPage.xaml</c>. The trailing control is just this control's XAML child.
///
/// Because the label is a *neighbour* rather than the control's own content, the automation tree has
/// no way to associate the two — so a screen reader reads "combo box", "slider", "edit" down the
/// whole page. That is the shape T175 found on the Statistics page's picker, and this row is where it
/// repeats thirty-odd times, so the row fixes it for every one of them: see
/// <see cref="NameTrailingControls"/>.
/// </summary>
internal sealed class SettingsRow : ContentControl
{
    public SettingsRow() => Loaded += (_, _) => NameTrailingControls();

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

    /// <summary>
    /// Gives the trailing control this row's <see cref="Header"/> as its accessible name, and only
    /// where it would otherwise announce nothing at all: a <c>ComboBox</c>, <c>Slider</c>,
    /// <c>TextBox</c> or glyphless <c>ToggleButton</c> has no content a name can be derived from,
    /// while a <c>Button</c> carrying text already reads and keeps its own label — which is what makes
    /// this safe on the rows holding a field *and* a "Browse…" beside it.
    ///
    /// Run on <c>Loaded</c> rather than when the content is set, because <c>Header</c> and the content
    /// child are assigned in whichever order the parser reaches them and this needs both.
    /// </summary>
    private void NameTrailingControls()
    {
        if (string.IsNullOrEmpty(Header)) return;
        Label(Content, 0);
    }

    private void Label(object? node, int depth)
    {
        // The deepest shape in the page today is a StackPanel inside a StackPanel; the cap is only so a
        // future nesting cannot turn this into an unbounded walk on every page load.
        if (depth > 3) return;

        if (node is Control control)
        {
            if (NeedsName(control)) AutomationProperties.SetName(control, Header);
            return;
        }
        if (node is Panel panel)
        {
            foreach (object? child in panel.Children) Label(child, depth + 1);
        }
    }

    private static bool NeedsName(Control control)
    {
        if (!string.IsNullOrEmpty(AutomationProperties.GetName(control))) return false;
        if (AutomationProperties.GetLabeledBy(control) is not null) return false;
        // A ContentControl whose content is text is already named by WPF from that text.
        return control is not ContentControl { Content: string { Length: > 0 } };
    }

    /// <summary>
    /// The row itself reaches UI Automation as a <c>Group</c> named by its <see cref="Header"/> (T204).
    ///
    /// <para>Naming the trailing control (above) tells a screen reader what one control is; it does not
    /// say that <em>this control and that text are one row</em>. Without an element between the panel
    /// and the control carrying that relation, the page is a flat run of labelled controls where the
    /// visual design is label↔control pairs — and a check sweeping the panel can only assert that a
    /// control announces <em>something</em>, so a rule handing every control the <b>wrong</b> header
    /// would pass. The group is what makes the pairing readable, to a person and to
    /// <c>Check-Interaction.ps1</c> alike.</para>
    ///
    /// <para>A headerless row stays out of the control view rather than adding an unnamed group to
    /// announce: its children reparent to the nearest control-view ancestor, so nothing is lost. So
    /// does a row on a panel that is not showing — <b>measured, because it is not what the peer does by
    /// default:</b> a settings panel keeps its peers once built, and the trailing controls drop out of
    /// the tree on their own while the rows did not, so the sweep saw 38 rows on a five-row panel and a
    /// screen reader would have found four panels' worth of empty groups to walk.</para>
    /// </summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new RowPeer(this);

    private sealed class RowPeer(SettingsRow owner) : FrameworkElementAutomationPeer(owner)
    {
        private string Header => ((SettingsRow)Owner).Header ?? string.Empty;

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        /// <summary>What lets a check find the rows and nothing else — the panels carry other groups.</summary>
        protected override string GetClassNameCore() => nameof(SettingsRow);

        protected override string GetNameCore() => Header;

        protected override bool IsControlElementCore() => Header.Length > 0 && Owner.IsVisible;
    }
}
