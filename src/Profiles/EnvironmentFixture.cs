namespace ClaudeTray;

/// <summary>
/// The environment states a developer's machine is never in, named rather than written (T231).
///
/// <para><see cref="EnvironmentProfile"/> answers off <c>CLAUDE_CONFIG_DIR</c>, and the readings worth
/// looking at are the disagreements: the variable naming a profile that is not the one on the icon
/// (T172's mark), or a folder no registered profile covers (T172's line). Both were shipped without
/// ever being seen, because this machine agrees with itself and the honest way to make it disagree is
/// to rewrite the developer's own registry — which is exactly what this app is careful never to do
/// casually.</para>
///
/// <para>Same shape as <see cref="AccountFixture"/> and <see cref="ContextFixture"/>: the surface is fed
/// a fixture instead of this machine's data, and it is parsed by the very code the surface uses. The
/// difference is that nothing is built on disk — an environment variable is one string, so the fixture
/// is the string.</para>
///
/// <para><b>The mode picks from the profiles that are really here.</b> That is the whole answer to the
/// objection this task carried: a fixture free to name any dir could put the menu in a state where the
/// variable selects a profile the watch list has never heard of, and certify a screen that cannot
/// occur. Every mode below resolves through the same discovery the tray runs, so the sampled
/// environment can only select something the menu can render.</para>
/// </summary>
internal static class EnvironmentFixture
{
    /// <summary>One sampled environment: the flag value, and what it puts on screen.</summary>
    internal readonly record struct Mode(string Name, string What);

    /// <summary>The catalogue, which is also the refusal (T186's rule, one flag along): an unknown name
    /// prints this and exits rather than rendering the ordinary state as if it were what was asked for.
    /// A state that needs no fixture is deliberately absent — an unset variable is reachable by unsetting
    /// it, but it is here because reaching it means editing the machine too.</summary>
    internal static readonly Mode[] Modes =
    {
        new("agrees",  "the variable names the profile the icon follows — the ordinary state, pinned"),
        new("other",   "the variable names a REGISTERED profile that is not the icon's (T172's mark)"),
        new("outside", "the variable names a folder no profile covers (T172's line)"),
        new("unset",   "no variable at all, so the default ~/.claude applies"),
    };

    /// <summary>The catalogue as the text a refusal prints.</summary>
    internal static string Catalogue()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--sample-env <mode> — answer as if CLAUDE_CONFIG_DIR said this, writing nothing:");
        foreach (Mode m in Modes) sb.AppendLine($"  {m.Name,-8}  {m.What}");
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// Put this process on the sampled environment <paramref name="mode"/> names.
    /// </summary>
    /// <returns>Null when it was applied; otherwise why it could not be, as the text to print. A mode
    /// this machine cannot produce a coherent example of is refused and nothing is sampled — "other"
    /// needs a second profile, and inventing one would be the incoherent screen this type exists to
    /// rule out.</returns>
    internal static string? Apply(string? mode)
    {
        Mode? picked = null;
        foreach (Mode m in Modes)
            if (string.Equals(m.Name, mode, StringComparison.OrdinalIgnoreCase)) picked = m;
        if (picked is not { } chosen)
            return $"--sample-env: unknown mode '{mode}'.\n{Catalogue()}";

        Settings settings = Settings.Load();
        List<ClaudeInfo> profiles = ClaudeAccount.Discover(settings.Profiles);
        ClaudeInfo? monitored = ClaudeAccount.PickMonitored(profiles, settings.MonitoredConfigDir);

        switch (chosen.Name)
        {
            case "unset":
                EnvironmentProfile.Sample(null);
                return null;

            case "outside":
                // Never a real config dir, and never a path with the Windows account name in it: the
                // folder is printed on screen and that screen is the reason the fixture exists.
                EnvironmentProfile.Sample(SampleRoot.For("ClaudeTray-sample-env"));
                return null;

            case "other":
            {
                ClaudeInfo? other = profiles.FirstOrDefault(
                    p => monitored is null || !ClaudeAccount.SamePath(p.ConfigDir, monitored.ConfigDir));
                if (other is null)
                    return "--sample-env other: this machine has no second profile for the variable to "
                           + "name, and a made-up one would put the menu in a state it cannot reach. "
                           + "Register a second profile, or use 'outside'.";
                // Through ValueFor, so selecting ~/.claude samples an UNSET variable rather than one
                // pointing at a dir the app deliberately never writes (T136).
                EnvironmentProfile.Sample(EnvironmentProfile.ValueFor(other.ConfigDir));
                return null;
            }

            default: // "agrees"
                if (monitored is null)
                    return "--sample-env agrees: no profile is being monitored, so there is nothing for "
                           + "the variable to agree with.";
                EnvironmentProfile.Sample(EnvironmentProfile.ValueFor(monitored.ConfigDir));
                return null;
        }
    }
}
