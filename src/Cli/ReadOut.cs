namespace ClaudeTray;

/// <summary>
/// How a headless read-out reports that it could not do the thing it was asked to do (T261).
///
/// <para><b>The defect.</b> Four read-outs printed <c>error: …</c> and returned, leaving the exit code at
/// 0 — so <c>--context --root D:\nope-does-not-exist</c> told its caller the run succeeded. Every refusal
/// on this surface exits 1 and was made to: an unknown preview variant (T186), an unknown toast (T198), a
/// <c>--capture-toast</c> missing its output path (T198), <c>--probe --live --recorded</c> (T226, asserted
/// by T245), a language this build does not ship (T260). Those refuse <em>arguments</em>. These fail at
/// <em>work</em>, which is the case a script cannot see by reading the output, because the output is what
/// it was going to print anyway. <c>--root</c> exists for the fixture loop, so the caller usually is a
/// script, and a fixture path that moved read as a project list that was simply empty.</para>
///
/// <para><b>How wide the rule goes, measured rather than argued.</b> §XXIX.1 worried that "any
/// <c>Error</c> means exit 1" would be too blunt — that a scan which walked most of a tree and hit one
/// unreadable directory is a partial answer, not a refusal. It is not blunt, because a partial answer
/// never lands here: <see cref="ContextScan.Error"/> is set in exactly two places and both
/// <c>return</c> an otherwise-empty scan (the root is absent, or the whole scan threw). A scan that
/// finished with holes carries <c>Error == null</c> — <c>SafeWalk</c> skips an unreadable subtree and
/// keeps going, and <c>Truncated</c> is the capped-walk signal. <see cref="UsageEvidence"/> is the one
/// type where partial <em>is</em> representable, and it says so with <c>Complete</c> beside its
/// <c>Error</c>: <c>ContextCli</c> prints that one as a note and carries on, which is right and must
/// stay that way.</para>
///
/// <para>So the rule is: <b>a read-out that could not start exits 1; one that finished with holes says so
/// and exits 0.</b> Stated here rather than at four call sites, for the same reason
/// <see cref="OutFile"/> exists.</para>
/// </summary>
internal static class ReadOut
{
    /// <summary>
    /// Report that a read-out could not start, and mark the run failed. Returns true when there was an
    /// error, so a call site reads <c>if (ReadOut.Failed(scan.Error)) return;</c> and cannot print the
    /// sentence without also setting the code.
    /// </summary>
    public static bool Failed(string? error)
    {
        if (error is null) return false;
        Console.WriteLine("error: " + error);
        Environment.ExitCode = 1;
        return true;
    }
}
