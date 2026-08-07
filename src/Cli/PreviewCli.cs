namespace ClaudeTray;

/// <summary>The fixture/capture/render entry points: deterministic previews for screenshots. Split out of `Program.cs` by T132 —
/// moved verbatim.</summary>
internal static class PreviewCli
{
    // Dev helper: show a toast card with sample data so it can be previewed / screenshotted standalone.
    // Which card is one row of ToastPreviews, the table --capture-toast reads too (T198); it uses the same
    // display strings as the live notifier, so nothing here can drift from what a user is shown.
    internal static void SimulateReset(string? variant)
    {
        if (ToastPreviews.Resolve(variant) is not { } chosen)
        {
            Environment.ExitCode = 1;
            return;
        }
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        ToastWindow toast = chosen.Build(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        toast.Closed += (_, _) => app.Shutdown();
        toast.Show();
        app.Run();
    }

    // Dev/preview helper: the same card SimulateReset shows, but instead of leaving it on screen it waits
    // for the entrance + bar-fill animations to settle, snapshots it to a transparent PNG (so it
    // composites on any README/site background), and exits. `outPath` is required by the caller: a capture
    // flag that defaults its *output* writes a file nobody named, and this one used to land `toast.png` in
    // the working directory — which here is the repository root (T198).
    internal static void CaptureToast(string variant, string outPath)
    {
        if (ToastPreviews.Resolve(variant) is not { } chosen)
        {
            Environment.ExitCode = 1;
            return;
        }
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        string full = System.IO.Path.GetFullPath(outPath);
        ToastWindow toast = chosen.Build(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        toast.Show();
        var settle = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1700), // entrance (420ms) + bar fill (550+900ms)
        };
        settle.Tick += (_, _) =>
        {
            settle.Stop();
            try
            {
                // A capture certifies that the card rendered, not that it fits (T228). The card's inner
                // grid clips, so text arranged past the bottom edge is simply absent from the PNG — the
                // flag printed "wrote" and exited 0 over the pt-BR card that was missing its caption.
                // Refuse rather than write: a picture of the defect is worse than no picture, because it
                // gets committed.
                if (toast.Overflow() is { Count: > 0 } spilled)
                {
                    Console.WriteLine($"the '{variant}' card does not fit its own frame in " +
                                      $"{L.Codes.First(c => L.Resolve(c) == L.Current)}, so nothing was written:");
                    foreach (string s in spilled) Console.WriteLine("  " + s);
                    Environment.ExitCode = 1;
                    return;
                }
                toast.SaveSnapshot(full);
                Console.WriteLine("wrote " + full);
            }
            finally { app.Shutdown(); }
        };
        settle.Start();
        app.Run();
    }

    /// <summary>
    /// Every toast card, asked whether it fits its own frame, in the language this process is in (T257).
    ///
    /// <para><b>Why this exists beside T228's refusal.</b> That refusal is in front of
    /// <see cref="CaptureToast"/>, so it fires exactly where somebody wanted a picture — and the card that
    /// shipped clipped for a release was the one nobody was photographing. Eight cards in five languages is
    /// forty questions and the only asker was a shell loop typed by hand.</para>
    ///
    /// <para><b>Why not <c>--selftest</c>.</b> The display language is fixed for the process (<c>L.Apply</c>
    /// runs once, and <c>{local:Loc}</c> resolves when a window is parsed), so five languages is five
    /// processes however this is written — and fit is a property of a laid-out window, not of a value a
    /// headless check can compute. So it is a flag, run once per language, and <c>check.yml</c> is what
    /// loops it: one command per language beats one command that spawns itself five times.</para>
    ///
    /// <para><b>No settle timer.</b> <see cref="CaptureToast"/> waits 1700ms because it photographs an
    /// animation; this reads a layout, which is done by <c>Loaded</c>. The short wait below is for the
    /// dispatcher to get there, and the cards are never activated (<c>ShowActivated="False"</c>), so a run
    /// takes no focus from whatever else is on the desktop — which matters, because T256 made a stolen
    /// foreground a real reading elsewhere in this repository.</para>
    /// </summary>
    internal static void CheckToasts()
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown,
        };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string lang = L.Codes.FirstOrDefault(c => L.Resolve(c) == L.Current) ?? "?";
        var pending = new Queue<ToastPreviews.Variant>(ToastPreviews.Catalogue);
        int spilled = 0;

        Console.WriteLine($"Toast cards in {lang} — does each one fit its own frame? ({pending.Count} cards)");
        Console.WriteLine();

        void Ask()
        {
            if (pending.Count == 0)
            {
                Console.WriteLine();
                Console.WriteLine(spilled == 0
                    ? $"all {ToastPreviews.Catalogue.Count} fit in {lang}."
                    : $"{spilled} of {ToastPreviews.Catalogue.Count} do not fit in {lang} — a card that "
                      + "overflows is clipped by its own grid, so a capture of it looks fine (T228).");
                Environment.ExitCode = spilled == 0 ? 0 : 1;
                app.Shutdown();
                return;
            }

            ToastPreviews.Variant v = pending.Dequeue();
            ToastWindow card = v.Build(now);
            card.Show();
            var settle = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250),   // the dispatcher reaching Loaded, not an animation
            };
            settle.Tick += (_, _) =>
            {
                settle.Stop();
                IReadOnlyList<string> over = card.Overflow();
                if (over.Count == 0) Console.WriteLine($"  ok    {v.Name}");
                else
                {
                    spilled++;
                    Console.WriteLine($"  SPILL {v.Name}");
                    foreach (string s in over) Console.WriteLine($"          {s}");
                }
                card.Close();
                Ask();
            };
            settle.Start();
        }

        Ask();
        app.Run();
    }

    // Dev helper: dump sample icons as PNG at real tray sizes for visual inspection.
    internal static void RenderTest(string dir)
    {
        Directory.CreateDirectory(dir);
        (double pct, IconRenderer.State st, bool fl, Projection verdict)[] cases =
        {
            (0.08, IconRenderer.State.Ok, false, Projection.Unknown),
            (0.08, IconRenderer.State.Ok, false, Projection.Ok),
            (0.54, IconRenderer.State.Ok, false, Projection.Danger),
            (1.00, IconRenderer.State.Ok, true, Projection.Danger),
        };
        foreach (int size in new[] { 16, 20, 32 })
            foreach (var (pct, st, fl, verdict) in cases)
                using (var bmp = IconRenderer.Render(pct, st, fl, size, verdict))
                    bmp.Save(Path.Combine(dir, $"icon_{(int)(pct * 100)}_{size}.png"));

        // The third state (T182): a full tile that is *paying* rather than stopped. Rendered beside the
        // stopped one at the same sizes, because the only question worth asking of this colour is
        // whether the two are tellable apart at 16px without either reading as an alarm.
        foreach (int size in new[] { 16, 20, 32 })
        {
            using (var stopped = IconRenderer.Render(1.00, IconRenderer.State.Ok, false, size, Projection.Danger))
                stopped.Save(Path.Combine(dir, $"icon_stopped_{size}.png"));
            using (var billing = IconRenderer.Render(1.00, IconRenderer.State.Ok, false, size, Projection.Danger, billing: true))
                billing.Save(Path.Combine(dir, $"icon_billing_{size}.png"));
        }
        // …and the sheet that lets the question actually be asked (T265). The two PNGs above have sat
        // beside each other on disk since T182 while the only way to compare them was to magnify them by
        // hand — which is the argument T147 already made for the accent band: a 16px PNG viewed at 16px
        // cannot be judged at all. Its own file rather than a row added to the accent sheet, because that
        // one asserts a band a glance has to notice and this one asserts two fills a glance must not
        // confuse, and one image carrying two claims is how a reader stops knowing which of them failed.
        SaveBillingSheet(Path.Combine(dir, "billing_sheet.png"));

        // And the third claim about this tile's colours (T321), on the same terms and in its own file: the
        // digits now say which window their number is about, and that is only worth having if the three are
        // tellable apart at 16px over every bar colour they can land on.
        SaveScopeSheet(Path.Combine(dir, "scope_sheet.png"));

        // The same cases carrying each profile accent (T147), plus a contact sheet magnified 8x with
        // the real 16px pixels preserved — the band has to be judged at tray size, and a 16px PNG
        // viewed at 16px cannot be judged at all.
        foreach (int size in new[] { 16, 20, 32 })
            foreach (var (pct, st, fl, verdict) in cases)
                for (int accent = 0; accent < IconRenderer.AccentCount; accent++)
                    using (var bmp = IconRenderer.Render(pct, st, fl, size, verdict, accent: accent))
                        bmp.Save(Path.Combine(dir, $"accent{accent}_{(int)(pct * 100)}_{size}.png"));
        SaveMarkSheet(Path.Combine(dir, "mark_sheet.png"), cases);

        // Logo, plain and with the API-error badge (the 403 state), at real + large sizes.
        foreach (int size in new[] { 16, 20, 32, 128 })
        {
            using (var plain = IconRenderer.RenderLogo(size))
                plain.Save(Path.Combine(dir, $"logo_{size}.png"));
            using (var err = IconRenderer.RenderLogo(size, errorBadge: true))
                err.Save(Path.Combine(dir, $"logo_error_{size}.png"));
        }
        Console.WriteLine("rendered to " + Path.GetFullPath(dir));
    }

    /// <summary>
    /// The sheet for the one question <c>IconRenderer</c> says is worth asking about this colour (T265):
    /// at 16px, are <em>stopped</em> and <em>paying</em> tellable apart, and does neither read as an alarm?
    ///
    /// <para><b>Paired and adjacent</b>, stopped then billing at each size, because the claim is about two
    /// fills a glance must not confuse — putting them side by side is the comparison, and separating them
    /// by a size would be asking a different question. Each tile is drawn at its own size × the zoom in a
    /// cell wide enough for the largest, so 16 next to 32 also shows how much less room the small one has.
    /// Both backdrops, for the same reason the accent sheet has both: the tile does not change and the
    /// taskbar does.</para>
    ///
    /// <para>Its own file rather than a row on <see cref="SaveMarkSheet"/>'s: that one asserts a band a
    /// glance has to notice, this one asserts two fills a glance must not confuse, and one image carrying
    /// two claims is how a reader stops knowing which of them failed.</para>
    /// </summary>
    private static void SaveBillingSheet(string path)
    {
        const int zoom = 8, gap = 10, widest = 32;
        int[] sizes = { 16, 20, 32 };
        int cell = widest * zoom + gap;
        int w = gap + sizes.Length * 2 * cell;
        int h = gap + 2 * (widest * zoom + gap);

        using var sheet = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        for (int row = 0; row < 2; row++)
        {
            int top = gap + row * (widest * zoom + gap);
            using (var bg = new SolidBrush(row == 0 ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240)))
                g.FillRectangle(bg, 0, top - gap / 2, w, widest * zoom + gap);

            int col = 0;
            foreach (int size in sizes)
                foreach (bool billing in new[] { false, true })
                {
                    using var bmp = IconRenderer.Render(1.00, IconRenderer.State.Ok, false, size,
                                                        Projection.Danger, billing: billing);
                    g.DrawImage(bmp, gap + col * cell, top, size * zoom, size * zoom);
                    col++;
                }
        }
        using (FileStream fs = OutFile.Create(path)) sheet.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("billing sheet: " + Path.GetFullPath(path));
    }

    /// <summary>
    /// The sheet for the claim T321 makes: the digits' colour says which window the number is about, and the
    /// three have to be tellable apart <em>at 16px</em> and legible over every bar colour that can sit under
    /// them.
    ///
    /// <para><b>Grouped by bar state, three scopes adjacent</b>, for the reason
    /// <see cref="SaveBillingSheet"/> pairs its two: the claim is about a comparison, so the things compared
    /// are neighbours, and separating them by a column would be asking something else. 16px only, and that is
    /// the point rather than an economy — 20 and 32 are the easy cases, and a sheet that includes them
    /// invites the judgement to be made on those.</para>
    ///
    /// <para>The hard states are here on purpose. Clay under orange digits is the pair that shares a hue.
    /// The flash covers its deep blue with a nearly full bar, so what it tests is the tile's edge rather
    /// than a new background. And the last two are <b>the states T320 produces</b>: nothing left, stated as
    /// <em>remaining</em>, so the bar drains to nothing and the digits sit on bare slate — paying, where the
    /// ink is the only thing that says so, and merely spent, where it is the only thing naming the window.</para>
    ///
    /// <para><b>Every band twice: 8× and then at the real 16px</b> (T322). The washed-out first pair was
    /// chosen on the magnified cells alone, which is the one size a tray never draws — blown up, a pale hue
    /// looks deliberate; at 16px inside a dark outline it is a white with a cast, which is how it was
    /// reported. The magnified band is still what shows <em>which</em> pixels are wrong, so both are here and
    /// the judgement belongs to the small one.</para>
    /// </summary>
    private static void SaveScopeSheet(string path)
    {
        const int px = 16, zoom = 8, gap = 8;
        string[] scopes = { "5h", "7d", "extra" };
        (double pct, Projection verdict, bool flash, bool billing, bool remaining)[] states =
        {
            (0.35, Projection.Unknown, false, false, false),  // cyan: no projection yet
            (0.35, Projection.Ok, false, false, false),       // green: on track
            (0.92, Projection.Danger, false, false, false),   // orange→red: about to run out
            (1.00, Projection.Danger, false, true, false),    // clay: past the quota and paying
            (0.95, Projection.Danger, true, false, false),    // the near-limit flash
            (1.00, Projection.Danger, false, true, true),     // T320 paying: "0 left", so no bar at all
            (1.00, Projection.Danger, false, false, true),    // ...and stopped, where the ink names the window
        };

        int cell = px * zoom + gap;
        int strip = px + gap;                     // the real-pixel band under each magnified one
        int band = cell + strip;
        int w = gap + states.Length * scopes.Length * cell;
        int h = gap + 2 * band;

        using var sheet = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        for (int row = 0; row < 2; row++)
        {
            int top = gap / 2 + row * band;
            using (var bg = new SolidBrush(row == 0 ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240)))
                g.FillRectangle(bg, 0, top, w, band);

            int col = 0;
            foreach (var (pct, verdict, flash, billing, remaining) in states)
                foreach (string scope in scopes)
                {
                    using var bmp = IconRenderer.Render(pct, IconRenderer.State.Ok, flash, px, verdict,
                                                        showRemaining: remaining, billing: billing,
                                                        scope: scope);
                    g.DrawImage(bmp, gap + col * cell, gap + row * band, px * zoom, px * zoom);
                    // The same tile at the size the notification area asks for, spaced the way icons sit
                    // beside each other there — this is the band the colour is judged on.
                    g.DrawImage(bmp, gap + col * (px + 4), gap + row * band + cell, px, px);
                    col++;
                }
        }
        using (FileStream fs = OutFile.Create(path)) sheet.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("scope sheet: " + Path.GetFullPath(path));
    }

    /// <summary>
    /// A contact sheet for the profile accent: every case unmarked and then once per accent, drawn at
    /// the real 16px and blown up 8x with nearest-neighbour so the actual tray pixels are what gets
    /// judged. Two backdrops, because the band has to survive a light and a dark taskbar and the tile
    /// is the same on both.
    /// </summary>
    private static void SaveMarkSheet(string path, (double pct, IconRenderer.State st, bool fl, Projection verdict)[] cases)
    {
        const int px = 16, zoom = 8, gap = 8;
        int[] marks = new[] { -1 }.Concat(Enumerable.Range(0, IconRenderer.AccentCount)).ToArray();
        int cell = px * zoom + gap;
        int w = gap + cases.Length * marks.Length * cell;
        int h = gap + 2 * cell;

        using var sheet = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(sheet);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;

        for (int row = 0; row < 2; row++)
        {
            using (var bg = new SolidBrush(row == 0 ? Color.FromArgb(32, 32, 32) : Color.FromArgb(240, 240, 240)))
                g.FillRectangle(bg, 0, gap / 2 + row * cell, w, cell);

            int col = 0;
            foreach (var (pct, st, fl, verdict) in cases)
                foreach (int mark in marks)
                {
                    using var bmp = IconRenderer.Render(pct, st, fl, px, verdict, accent: mark);
                    g.DrawImage(bmp, gap + col * cell, gap + row * cell, px * zoom, px * zoom);
                    col++;
                }
        }
        using (FileStream fs = OutFile.Create(path)) sheet.Save(fs, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine("mark sheet: " + Path.GetFullPath(path));
    }

    // Dev helper: a hand-built pacing report with a data gap in the session curve, so the "unavailable"
    // outage rendering (red dashed span along the usage line) can be inspected deterministically.
    internal static PaceReport BuildGapDemoReport(long now)
    {
        var r = new PaceReport { ComputedLocal = DateTimeOffset.FromUnixTimeSeconds(now).LocalDateTime };

        var s = r.Session;
        s.WindowSeconds = 5 * 3600;
        s.ResetUnix = now + 2 * 3600;
        s.SecondsToReset = 2 * 3600;
        s.ElapsedSeconds = 3 * 3600;
        s.Util = 0.72;
        s.HasWindow = true;
        s.Verdict = PaceVerdict.Ahead;
        s.ExhaustSeconds = s.ElapsedSeconds * (1 - s.Util) / s.Util;
        s.InputTokens = 200_000; s.OutputTokens = 560_000; s.CacheCreationTokens = 4_200_000;
        // A past outage in the middle of the window that has since recovered: readings stop at 32%,
        // resume at 48%, and continue normally to "now" (60%). The gap segment is drawn red.
        s.Curve = new() { (0, 0), (0.08, 0.10), (0.16, 0.18), (0.24, 0.26), (0.32, 0.32), (0.48, 0.50), (0.54, 0.60), (0.60, 0.72) };
        s.Gaps = new() { (0.32, 0.32, 0.48, 0.50) };   // no reading logged from 32% to 48% (the outage)

        var w = r.Weekly;
        w.WindowSeconds = 7 * 86400;
        w.ResetUnix = now + 3 * 86400;
        w.SecondsToReset = 3 * 86400;
        w.ElapsedSeconds = 4 * 86400;
        w.Util = 0.38;
        w.HasWindow = true;
        w.Verdict = PaceVerdict.Adequate;
        w.ExhaustSeconds = w.ElapsedSeconds * (1 - w.Util) / w.Util;
        w.InputTokens = 900_000; w.OutputTokens = 2_100_000; w.CacheCreationTokens = 18_000_000;
        w.Curve = new() { (0, 0), (0.14, 0.09), (0.29, 0.17), (0.43, 0.26), (0.571, 0.38) };

        return r;
    }

    // Dev helper: build a multi-resolution .ico for the app (PNG-compressed entries, valid on
    // Windows Vista+) from the GDI+ logo renderer.
    internal static void MakeIcon(string path)
    {
        int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
        byte[][] pngs = new byte[sizes.Length][];
        for (int i = 0; i < sizes.Length; i++)
        {
            using Bitmap bmp = IconRenderer.RenderLogo(sizes[i]);
            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            pngs[i] = ms.ToArray();
        }

        using FileStream fs = OutFile.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0);              // reserved
        bw.Write((short)1);              // type: icon
        bw.Write((short)sizes.Length);   // image count

        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            bw.Write((byte)(s >= 256 ? 0 : s)); // width (0 = 256)
            bw.Write((byte)(s >= 256 ? 0 : s)); // height
            bw.Write((byte)0);                  // palette
            bw.Write((byte)0);                  // reserved
            bw.Write((short)1);                 // color planes
            bw.Write((short)32);                // bits per pixel
            bw.Write(pngs[i].Length);           // image size in bytes
            bw.Write(offset);                   // image offset
            offset += pngs[i].Length;
        }
        foreach (byte[] png in pngs) bw.Write(png);

        Console.WriteLine("wrote " + Path.GetFullPath(path));
    }
}
