using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace ClaudeTray;

/// <summary>
/// A <see cref="Border"/> that UI Automation can see (T448).
/// <para>
/// A plain Border creates no automation peer, so a focusable one is a keyboard stop that a screen reader
/// cannot announce and no case can address: the live chart's project strip took the focus and opened the
/// Sessions pane on Enter or Space, and nothing outside the process could tell. This gives it the
/// framework's own element peer, which carries its <c>x:Name</c> as the automation id and its
/// <c>AutomationProperties.Name</c> as what a reader says.
/// </para>
/// </summary>
internal sealed class PeeredBorder : Border
{
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);
}
