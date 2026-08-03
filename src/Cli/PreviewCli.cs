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
