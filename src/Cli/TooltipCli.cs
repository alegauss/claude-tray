namespace ClaudeTray;

/// <summary>
/// <c>--tooltip [variant]</c>: the tray tooltip's text for a synthetic reading, printed (T214).
///
/// <para>Every other surface this app publishes can be photographed by a flag. The tooltip cannot: it is
/// <c>NOTIFYICONDATA.szTip</c>, drawn by the shell, and it appears only while a pointer rests over an
/// icon that may be inside the overflow flyout — there is no window to snapshot and nothing in the
/// accessibility tree. So the most-seen text this app produces was reviewed by hovering and looking,
/// which is how <c>docs/tooltip.png</c> came to show a line the app had stopped producing a release
/// earlier, with nothing failing because nothing looks.</para>
///
/// <para>This is the read-out half of §XX.12 and it answers the question a picture cannot: <b>what does
/// it actually say</b>, in every state, in any language, at what length. Each row prints its character
/// count against <see cref="TooltipText.Cap"/>, because the budget is what decides whether the
/// projection sentence appears in full, in its compact form, or not at all — and a translation that
/// spends eight more characters changes that silently.</para>
/// </summary>
internal static class TooltipCli
{
    internal sealed record Variant(string Name, string What, Func<long, TooltipText.Input> Build);

    /// <summary>The reading with no data in it, which the last three rows of <see cref="All"/> vary from.
    ///
    /// <para><b>It is declared above <c>All</c> deliberately (T272).</b> Static field initialisers run in
    /// textual order, so below it this field is null while <c>All</c> is being built — and the three rows
    /// that read it survived only because each read sits inside a <c>Func&lt;long, …&gt;</c> nobody invokes
    /// until the static constructor has finished. The compiler said so, three times, as the only warnings
    /// this project produced; one non-deferred read would have turned them into a
    /// <c>NullReferenceException</c> at type initialisation, before <c>--tooltip</c> printed a line. Moving
    /// it up costs nothing — the flag's output is byte-identical across the move — and the order is now
    /// safe rather than lucky.</para></summary>
    private static readonly TooltipText.Input Empty =
        new(null, "5h", false, null, Projection.Unknown, 0, QuotaState.InQuota, "", 0);

    /// <summary>The states worth reading. The order is the catalogue's, and the first row is what a bare
    /// flag shows — the same rule the other two preview tables follow.</summary>
    private static readonly Variant[] All =
    {
        new("ok", "the ordinary reading: a session ahead of pace, a week on track (the default)",
            now => Reading(now, 0.72, 0.38, metric: "5h", verdict: Projection.Ok, eta: 4 * 3600)),

        new("track", "nothing to project yet, so the projection sentence is the untimed form",
            now => Reading(now, 0.10, 0.12, metric: "5h", verdict: Projection.Ok,
                           eta: double.PositiveInfinity)),

        new("danger", "burning fast enough to run out before the window resets",
            now => Reading(now, 0.61, 0.88, metric: "7d", verdict: Projection.Danger, eta: 26 * 3600)),

        new("remaining", "the same reading stated as quota LEFT rather than used (a display option)",
            now => Reading(now, 0.72, 0.38, metric: "5h", verdict: Projection.Ok, eta: 4 * 3600)
                   with { ShowRemaining = true }),

        new("profile", "two profiles watched, so every percentage names whose it is - the line the " +
                       "budget keeps even when it costs the projection its full form",
            now => Reading(now, 0.72, 0.38, metric: "5h", verdict: Projection.Ok, eta: 4 * 3600)
                   with { ProfileLabel = "Personal" }),

        new("extra", "past the included quota and billing: the overage reading, carrying the news that " +
                     "work is continuing rather than stopped (T182, merged onto it by T222), and how long " +
                     "the spell has been running (T280)",
            now => Reading(now, 0.40, 1.0, metric: "7d", verdict: Projection.Unknown, eta: 0,
                           extra: 0.47, state: QuotaState.Billing, spellAgo: 3 * 3600 + 20 * 60)),

        new("extraunspent", "extra usage enabled and not a cent of it spent: no overage reading to carry " +
                            "the news, so the sentence stands on its own — and the spell is still datable, " +
                            "because the header saying so is what the store records (T280)",
            now => Reading(now, 0.40, 1.0, metric: "7d", verdict: Projection.Unknown, eta: 0,
                           state: QuotaState.Billing, spellAgo: 26 * 3600)),

        new("billingelsewhere", "the session rejected at 102% behind a week at 47%, with the icon on the " +
                                "week: the account is paying and the caption cannot say so about THIS " +
                                "window, so the news goes out unscoped (T274) — and the crossing is not on " +
                                "file, so nothing dates it (T280)",
            now => Reading(now, 1.02, 0.47, metric: "7d", verdict: Projection.Ok, eta: 4 * 3600,
                           state: QuotaState.Billing, status5h: "rejected")),

        new("atlimit", "the week's quota reached with no extra usage: work has stopped",
            now => Reading(now, 0.40, 1.0, metric: "7d", verdict: Projection.Unknown, eta: 0,
                           state: QuotaState.Stopped)),

        new("connecting", "before the first poll returns",
            _ => Empty with { Data = null }),

        new("signedout", "no usable token on disk, so Claude Code needs a full browser login",
            _ => Empty with { Data = new UsageData { Error = "401", Unauthorized = true, NeedsFullLogin = true } }),

        new("error", "the API answered something this app could not use",
            _ => Empty with { Data = new UsageData { Error = "403 payment required" } }),
    };

    /// <summary>One synthetic reading, with the resets placed far enough out that their countdowns are
    /// stable text rather than something that changes between two runs of this flag.</summary>
    /// <param name="spellAgo">How long ago the overage spell was first measured, in seconds, or 0 for a
    /// spell whose crossing is not on file (T280). Given as an offset from <paramref name="now"/> rather
    /// than as an instant, for the same reason the resets are: the printed duration has to be the same text
    /// on two runs of this flag.</param>
    private static TooltipText.Input Reading(long now, double session, double week, string metric,
                                             Projection verdict, double eta,
                                             double extra = 0, QuotaState state = QuotaState.InQuota,
                                             string status5h = "allowed", double spellAgo = 0)
        => new(
            Data: new UsageData
            {
                Session5h = session,
                Week7d = week,
                Extra = extra,
                HasExtra = extra > 0,
                Reset5h = now + 2 * 3600,
                Reset7d = now + 3 * 86400,
                ResetExtra = extra > 0 ? now + 3 * 86400 : 0,
                // Named rather than derived, because the row this exists for is the one where the session
                // is the rejected window and the week — the one on the icon — is nowhere near its limit.
                Status = status5h,
                // Keyed on the state, not on the figure: an account that is billing has a weekly window
                // that still permits work whether or not a cent of the allowance has been spent yet, and
                // keying on `extra` alone made that one row read as refused while it says it is paying.
                Status7d = week >= 1.0 ? (state == QuotaState.Billing ? "allowed" : "rejected") : "allowed",
                StatusExtra = extra > 0 ? "allowed" : "unknown",
            },
            Metric: metric,
            ShowRemaining: false,
            ProfileLabel: null,
            Verdict: verdict,
            Eta: eta,
            State: state,
            // A fixed time, not the clock: two runs of a read-out must produce the same text, or a
            // comparison against a published picture is noise.
            Updated: "  ⟳ 14:32:05",
            Now: now,
            SpellSince: spellAgo > 0 ? now - (long)spellAgo : 0);

    internal static int Run(string[] args)
    {
        string? word = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--") && !a.Contains('='));
        Variant[] chosen;
        if (word is null)
        {
            chosen = All;                       // no name: the whole surface, which is the point
        }
        else if (All.FirstOrDefault(v => v.Name.Equals(word, StringComparison.OrdinalIgnoreCase)) is { } one)
        {
            chosen = new[] { one };
        }
        else
        {
            // Same refusal as the other preview tables: showing the default under a name nobody asked
            // for is how a wrong reading gets quoted as if it were the right one (T186, T198).
            Console.WriteLine($"unknown tooltip '{word}'. Refusing rather than showing the default one.");
            PrintCatalog();
            return 1;
        }

        long now = 1_800_000_000;               // fixed, so the read-out is reproducible
        Console.WriteLine($"tray tooltip — {L.Current} — cap {TooltipText.Cap} chars (NOTIFYICONDATA.szTip)");
        foreach (Variant v in chosen)
        {
            string text = TooltipText.Compose(v.Build(now));
            Console.WriteLine();
            Console.WriteLine($"{v.Name}  [{text.Length}/{TooltipText.Cap}]  {v.What}");
            foreach (string line in text.Split('\n')) Console.WriteLine("  │ " + line);
        }
        return 0;
    }

    /// <summary>
    /// <c>--capture-tooltip &lt;out.png&gt; [variant]</c>: the same composed text drawn into the card the
    /// shell draws, so the published picture becomes something anybody can retake (T214).
    ///
    /// <para><b>It is a rendering, and it says so by what it leaves out.</b> The shell owns the real
    /// tooltip, so this cannot be a photograph of it — what it can be is honest about the words, which is
    /// the half that goes stale. So it draws the card and nothing else: no taskbar, no neighbouring
    /// icons, no desktop. The picture it replaces had all three, which is exactly why it could only be
    /// taken by hand on one machine, and why it kept a line the app had stopped producing.</para>
    ///
    /// <para>Transparent outside the card, the way <c>--capture-toast</c> is, so it composites onto the
    /// README's and the site's own backgrounds instead of carrying one machine's wallpaper.</para>
    /// </summary>
    internal static int Capture(string[] args)
    {
        // Both arguments, for T198's reason: a capture flag that defaults its output writes a file
        // nobody named, and the working directory here is the repository root.
        string[] bare = args.Skip(1).Where(a => !a.StartsWith("--") && !a.Contains('=')).ToArray();
        if (bare.Length == 0)
        {
            Console.WriteLine("--capture-tooltip needs an output path: --capture-tooltip <out.png> [variant]");
            PrintCatalog();
            return 1;
        }

        string? word = bare.Length > 1 ? bare[1] : null;
        Variant chosen = All[0];
        if (word is not null)
        {
            if (All.FirstOrDefault(v => v.Name.Equals(word, StringComparison.OrdinalIgnoreCase)) is not { } one)
            {
                Console.WriteLine($"unknown tooltip '{word}'. Refusing rather than capturing the default one.");
                PrintCatalog();
                return 1;
            }
            chosen = one;
        }

        string text = TooltipText.Compose(chosen.Build(1_800_000_000));
        string outPath = Path.GetFullPath(bare[0]);
        Draw(text, outPath);
        Console.WriteLine("wrote " + outPath);
        return 0;
    }

    /// <summary>
    /// The card, at 2× so the published PNG is crisp on a high-DPI display — the same reason every other
    /// picture in <c>docs/</c> is larger than its layout size. Colours are the shell's dark tooltip:
    /// there is no theme resource to bind to here, because this is not a WPF surface.
    /// </summary>
    private static void Draw(string text, string outPath)
    {
        const int scale = 2, pad = 14 * scale, radius = 8 * scale;
        string[] lines = text.Split('\n');

        using var font = new System.Drawing.Font("Segoe UI", 12f * scale, System.Drawing.GraphicsUnit.Pixel);
        // Measured on a throwaway surface first: the card is sized to its text, never the other way round,
        // so a longer translation widens the picture instead of being clipped by it.
        using var probe = new System.Drawing.Bitmap(1, 1);
        using var pg = System.Drawing.Graphics.FromImage(probe);
        // Grayscale AA, not ClearType: subpixel rendering assumes it knows the colour behind the glyph,
        // and over a transparent background it fringes every stroke orange-on-blue.
        pg.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        float wide = 0, lineHeight = 0;
        foreach (string line in lines)
        {
            System.Drawing.SizeF s = pg.MeasureString(line, font);
            wide = Math.Max(wide, s.Width);
            lineHeight = Math.Max(lineHeight, s.Height);
        }

        int w = (int)Math.Ceiling(wide) + pad * 2;
        int h = (int)Math.Ceiling(lineHeight * lines.Length) + pad * 2;

        using var bmp = new System.Drawing.Bitmap(w, h);
        using var g = System.Drawing.Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        g.Clear(System.Drawing.Color.Transparent);

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        int d = radius * 2;
        path.AddArc(0, 0, d, d, 180, 90);
        path.AddArc(w - d - 1, 0, d, d, 270, 90);
        path.AddArc(w - d - 1, h - d - 1, d, d, 0, 90);
        path.AddArc(0, h - d - 1, d, d, 90, 90);
        path.CloseFigure();

        using (var fill = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x2B, 0x2B, 0x2B)))
            g.FillPath(fill, path);
        using (var edge = new System.Drawing.Pen(System.Drawing.Color.FromArgb(0x45, 0x45, 0x45), scale))
            g.DrawPath(edge, path);

        using var ink = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0xF0, 0xF0, 0xF0));
        for (int i = 0; i < lines.Length; i++)
            g.DrawString(lines[i], font, ink, pad, pad + i * lineHeight);

        using FileStream file = OutFile.Create(outPath);   // T187: the directory it was given, made
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    /// <summary>What exists, printed for a refusal.</summary>
    internal static void PrintCatalog()
    {
        Console.WriteLine();
        Console.WriteLine("tooltips (--tooltip [variant]; no variant prints them all):");
        foreach (Variant v in All)
            Console.WriteLine($"  {v.Name,-11} {v.What}");
    }

    /// <summary>The table itself, for the self-check that every row resolves and that this catalogue and
    /// the <c>dev-flags</c> one name the same states (T207).</summary>
    internal static IReadOnlyList<Variant> Catalogue => All;
}
