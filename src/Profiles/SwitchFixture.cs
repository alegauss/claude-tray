namespace ClaudeTray;

/// <summary>
/// The Profile submenu's two switches in a position this machine is not in, named rather than saved
/// (WW86).
///
/// <para><see cref="Settings.FollowActiveProfile"/> and <see cref="Settings.SyncEnvironmentProfile"/>
/// decide four things the submenu draws: each switch's own position, the sentence on every profile entry
/// saying how far a pick reaches (T171), whether Open Claude Code is a submenu or a command (T146), and
/// whether a hand pick pins the icon (T139). The check script read whichever position this machine
/// happened to be in off <c>--profiles</c>, so it only ever checked one side of each switch, and which side
/// depended on the desk. A case wants the position fixed by its fixture, and flipping it for real means
/// saving the developer's own settings.</para>
///
/// <para>Same shape as <see cref="EnvironmentFixture"/>. The fixture is two booleans, laid over whatever
/// <see cref="Settings.Load"/> read, and applying one makes the process an observer
/// (<see cref="ProfileStore.Observe"/>), because a sampled setting that could be saved is a sampled
/// setting written into somebody's file.</para>
/// </summary>
internal static class SwitchFixture
{
    /// <summary>One sampled position of the two switches, and what it puts on screen.</summary>
    internal readonly record struct Mode(string Name, bool Follow, bool Sync, string What)
    {
        /// <summary>Lay this position over <paramref name="settings"/>, and nothing else of it.</summary>
        internal Settings Put(Settings settings)
        {
            settings.FollowActiveProfile = Follow;
            settings.SyncEnvironmentProfile = Sync;
            return settings;
        }
    }

    /// <summary>The catalogue, which is also the refusal: an unknown name prints this and exits rather
    /// than rendering the saved position under a flag that asked for another (T186's rule).</summary>
    internal static readonly Mode[] Modes =
    {
        new("none",   false, false, "both off — the defaults: a pick moves the tray only"),
        new("follow", true,  false, "auto-follow on, so a pick by hand pins the icon (T139)"),
        new("sync",   false, true,  "the machine-wide switch on: a pick reaches Windows too (T171), and Open Claude Code is a command (T146)"),
        new("both",   true,  true,  "both on"),
    };

    /// <summary>The position this process answers with, or null where nothing was sampled.</summary>
    internal static Mode? Sampled { get; private set; }

    /// <summary>The catalogue as the text a refusal prints.</summary>
    internal static string Catalogue()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--sample-switches <mode> — answer as if the Profile submenu's switches were here, writing nothing:");
        foreach (Mode m in Modes) sb.AppendLine($"  {m.Name,-7}  {m.What}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>Put this process on the position <paramref name="mode"/> names, for the rest of its life.</summary>
    /// <returns>Null when it was applied, or the refusal to print. A refused mode samples nothing and
    /// observes nothing.</returns>
    internal static string? Apply(string? mode)
    {
        Mode? picked = null;
        foreach (Mode m in Modes)
            if (string.Equals(m.Name, mode, StringComparison.OrdinalIgnoreCase)) picked = m;
        if (picked is not { } chosen)
            return $"--sample-switches: unknown mode '{mode}'.\n{Catalogue()}";

        Sampled = chosen;
        ProfileStore.Observe();
        return null;
    }

    /// <summary>What <see cref="Settings.Load"/> hands back: the settings read, with the sampled position
    /// laid over them where there is one.</summary>
    internal static Settings Over(Settings settings) => Sampled is { } mode ? mode.Put(settings) : settings;
}
