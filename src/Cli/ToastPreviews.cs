namespace ClaudeTray;

/// <summary>
/// The toast previews, as one table both entry points read: <c>--simulate-reset &lt;variant&gt;</c> leaves the
/// card on screen and <c>--capture-toast &lt;variant&gt; &lt;out.png&gt;</c> renders the same card to a
/// transparent PNG. The Statistics half of this pattern is <see cref="StatsPreviews"/>, and the reasoning is
/// the same one (T186), arriving here through T198.
///
/// <para><b>Why a table.</b> The two flags built their variants in two separate <c>switch</c> expressions
/// whose default arm was a *real* variant — the early weekly reset — so an unrecognised token rendered a
/// card nobody asked for, with no error and no warning. That is not hypothetical drift: <c>extra</c> was
/// documented for both flags and implemented in only one, so <c>--simulate-reset extra</c> silently showed
/// the early weekly reset instead of the extra-usage card. And because the token was read positionally,
/// <c>--capture-toast D:\tmp\out.png</c> read the *path* as the variant, fell through the same default arm
/// and wrote the wrong card to <c>toast.png</c> in the working directory.</para>
///
/// <para>Two rules, as next door: every preview is one row here, so adding one is one edit and both flags
/// gain it; and a token that names no row is <b>refused</b> with the catalogue printed
/// (<see cref="Resolve"/>) rather than falling through to a default.</para>
/// </summary>
internal static class ToastPreviews
{
    /// <summary>One preview. <c>Build</c> takes the current unix second, so every fixture's resets sit the
    /// same distance from whenever it runs.</summary>
    internal sealed record Variant(string Name, string What, Func<long, ToastWindow> Build);

    /// <summary>The four reset cards, built from the same <see cref="TrayContext.ResetToastContent"/> the
    /// live notifier uses, so the wording can never drift from what a user is actually shown.</summary>
    private static Func<long, ToastWindow> Reset(string key, BurnTracker.ResetKind kind, double prev,
                                                double next, Func<long, long> at, Func<long, long> until) =>
        now =>
        {
            var ev = new BurnTracker.ResetEvent(kind, prev, next, at(now), until(now));
            var (emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme) =
                TrayContext.ResetToastContent(key, ev, now);
            return new ToastWindow(emoji, title, subtitle, fromUsage, toUsage, caption, quotaLabel, theme);
        };

    // The order the catalogue prints in, and the first row is what a bare flag shows.
    private static readonly Variant[] All =
    {
        new("unexpected", "the early weekly reset: quota back days before it was due, so the loud " +
                          "\"Surprise!\" framing (the default)",
            Reset("7d", BurnTracker.ResetKind.Unexpected, 0.79, 0.0,
                  now => now + 3 * 86400, now => now + 7 * 86400)),

        new("scheduled", "the routine weekly reset, on the day it was due",
            Reset("7d", BurnTracker.ResetKind.Scheduled, 0.79, 0.0,
                  now => now, now => now + 7 * 86400)),

        new("credit", "a partial mid-window drop, 91% -> 50%: quota back without the window ending",
            Reset("7d", BurnTracker.ResetKind.Credit, 0.91, 0.50,
                  now => now + 4 * 86400, now => now + 4 * 86400)),

        new("session", "the routine 5-hour session reset",
            Reset("5h", BurnTracker.ResetKind.Scheduled, 0.88, 0.0,
                  now => now, now => now + 5 * 3600)),

        new("context", "not a reset at all: the context-load nudge, which shares the card but not its " +
                       "festive framing (ochre, no confetti)",
            _ => new ToastWindow("📇", L.T("toast.context.title"),
                // Invented, for the reason ContextFixture exists at all: this card gets published, and
                // a real repo name here puts a client's name on the marketing page.
                L.T("toast.context.subtitle", "acme/atlas/2026.2"),
                0.27, 0.27,
                L.T("toast.context.caption", TokenEstimate.Format(54_000), (0.336).ToString("0.000", L.Culture)),
                L.T("toast.context.quotaLabel"), ToastWindow.ToastTheme.Context)),

        new("extra", "the extra-usage card (T184), which can only be seen this way: the state it " +
                     "announces is one no machine can be put into on demand",
            // The card's bar is "quota still available", so it renders 1 - x. Passing the complement makes
            // the filled sliver read as the extra usage spent so far, which is what the label beside it says.
            _ => new ToastWindow("🧾", L.T("toast.extra.title"), L.T("toast.extra.subtitle"),
                1 - 0.06, 1 - 0.06, L.T("toast.extra.caption", "2d 3h"),
                L.T("toast.extra.quotaLabel"), ToastWindow.ToastTheme.ExtraUsage)),
    };

    /// <summary>
    /// Resolve the variant a preview command names. Null (no token given) is the first row; anything that
    /// names no row is <b>refused</b> — returning null after printing what exists, so the caller exits
    /// non-zero rather than showing a card it was not asked for.
    /// </summary>
    internal static Variant? Resolve(string? word)
    {
        if (word is null) return All[0];
        if (All.FirstOrDefault(v => v.Name.Equals(word, StringComparison.OrdinalIgnoreCase)) is { } found)
            return found;

        Console.WriteLine($"unknown toast '{word}'. Refusing rather than showing the early weekly reset, " +
                          "which is how a screenshot of the wrong card gets taken (T198).");
        PrintCatalog();
        return null;
    }

    /// <summary>What exists, printed for a refusal — an unknown name, or a command missing its output
    /// path.</summary>
    internal static void PrintCatalog()
    {
        // The catalogue is the one output of these flags a person reads, and it is full of em dashes; a
        // WinExe's console starts on the OEM code page, which prints them as '?'.
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* redirected output */ }
        Console.WriteLine();
        Console.WriteLine("toasts (--simulate-reset <variant> / --capture-toast <variant> <out.png>):");
        foreach (Variant v in All)
            Console.WriteLine($"  {v.Name,-11} {v.What}");
    }
}
