namespace ClaudeTray;

/// <summary>
/// A tray-menu entry that is a two-state <b>switch</b> rather than a command, declared so that its
/// <em>off</em> position is a reading rather than an absence (T234).
///
/// <para>A plain <see cref="ToolStripMenuItem"/> exposes a toggle to UI Automation only <b>while it is
/// checked</b>; unchecked, it carries no toggle at all, so "off" and "not a toggle" are one reading. A
/// switch that stopped turning on is caught by a check; one that stopped turning off is not.</para>
///
/// <para><b>Not by role.</b> The obvious repair — <c>AccessibleRole.CheckButton</c>, which does report a
/// toggle in both positions — was measured and rejected: it re-types the entry as a check box, and the
/// entries then vanish from the menu tree entirely, taking the count and the label assertions with them.
/// So the position is announced as words instead, in <see cref="MenuAccess.Announce"/>, which is also the
/// only form a screen reader was ever going to read out here.</para>
/// </summary>
internal class AnnouncingMenuItem(string text) : ToolStripMenuItem(text)
{
    /// <summary>
    /// The whole of T234's mechanism: <c>Help</c> is the one property that carries a sentence beside an
    /// entry's name and is reachable from a UI Automation client, and nothing on
    /// <see cref="ToolStripItem"/> fills it. <c>AccessibleDescription</c> does not — measured: it reaches
    /// only <c>LegacyIAccessible</c>, which the managed client cannot even name a pattern for.
    /// </summary>
    protected override AccessibleObject CreateAccessibilityInstance() => new Announcing(this);

    private sealed class Announcing(ToolStripMenuItem item) : ToolStripItemAccessibleObject(item)
    {
        public override AccessibleRole Role => AccessibleRole.MenuItem;

        public override string Help => item.AccessibleDescription ?? "";

        // The check mark, which the base item's own object reports and this one would drop: a menu entry
        // that stopped announcing "checked" would be a regression traded for the sentence, and the profile
        // entries are read for exactly that.
        public override AccessibleStates State =>
            item.Checked ? base.State | AccessibleStates.Checked : base.State;
    }
}

internal sealed class SwitchMenuItem(string text) : AnnouncingMenuItem(text);

/// <summary>
/// What the tray menu tells UI Automation, which is not what it draws (T234).
///
/// <para><b>The gap.</b> A <see cref="ToolStripItem"/>'s <c>ToolTipText</c> reaches no accessibility
/// property at all — WinForms draws it and stops there. Everything the Profile submenu explains about
/// <em>scope</em> lives in those tooltips: T171's "the tray only" against "the tray and the Windows user
/// environment", and T172's line about a session already running keeping what it started with. Those
/// sentences exist because the icon moving reads as "the machine is now on this profile" and that is only
/// sometimes true — so the person who most needs them is a screen-reader user, and they were the one
/// person who could not reach them.</para>
///
/// <para><b>Swept, not set per call site.</b> Every entry that gains a tooltip gains the announcement,
/// including the ones written later — the same reason the automation-id and formatter sweeps derive their
/// targets. Run it when the menu opens, after whatever populates it.</para>
/// </summary>
internal static class MenuAccess
{
    /// <summary>Give every entry in the tree the description a reader can actually hear: its tooltip, and
    /// for a <see cref="SwitchMenuItem"/> the position it is in.</summary>
    public static void Announce(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            // Newlines are for a tooltip's layout, not for speech: a reader would say "new line".
            string said = (item.ToolTipText ?? "").Replace("\n", " · ");

            // The position, in words and in front, because the sentence behind it is free text that may
            // contain either word ("Turning it off puts the variable back…") — so what a reader hears
            // first, and what a check may match on, is the prefix and only the prefix.
            //
            // A custom accessible object is what makes the sentence reachable at all, and it costs the
            // TogglePattern the framework's own object supplies while an entry is checked. That was a
            // reading worth keeping, so it is kept here as a word: a checked entry says so, and — for a
            // switch, where "off" and "not a switch" used to be the same silence — so does an unchecked one.
            string? state = item switch
            {
                SwitchMenuItem sw => L.T(sw.Checked ? "menu.switchOn" : "menu.switchOff"),
                AnnouncingMenuItem { Checked: true } => L.T("menu.itemChecked"),
                _ => null,
            };
            if (state is not null) said = said.Length > 0 ? state + " · " + said : state;

            if (said.Length > 0) item.AccessibleDescription = said;

            if (item is ToolStripMenuItem { HasDropDownItems: true } parent)
                Announce(parent.DropDownItems);
        }
    }
}
