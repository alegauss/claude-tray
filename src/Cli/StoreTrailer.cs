using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// The Store listing's trailer (T464), after Modelado's MOD84: about 32 seconds at 1920 x 1080 and 30 frames
/// a second, in the screenshots' look. It opens on what the app is — the tray icon filling as the week is
/// used, turning red when the projection says the limit comes before the reset, and the warning sliding in —
/// drawn by the icon's own renderer, so the trailer shows nothing the tray does not. Then the listing's
/// screenshots of that language, eased in, the claim that it is all computed on the PC, and a closing card.
/// Frames go straight into ffmpeg's standard input as raw BGRA, so no folder of a thousand PNGs is written.
/// The thumbnail the Store asks beside each trailer is one frame of the first scene, with the warning shown.
/// </summary>
internal static class StoreTrailer
{
    private const int W = StoreCanvas.Width;
    private const int H = StoreCanvas.Height;
    private const int Fps = 30;
    private const int Fade = 9;

    /// <summary>What one language's trailer says and shows, read from the JSON store-trailer.ps1 writes.</summary>
    internal sealed record Spec(
        string TrayHeadline,
        string TraySubline,
        string LocalHeadline,
        string LocalSubline,
        string End,
        string Badge,
        string Toast,
        string[] Screenshots);

    private sealed record Scene(int Frames, Bitmap Background, Action<Graphics, float>? Draw, int ThumbnailAt = -1);

    private static readonly JsonSerializerOptions SpecJson = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Renders the trailer into an MP4 and its thumbnail beside it, as a PNG of the same name.</summary>
    /// <returns>ffmpeg's exit code, or 1 when the spec or a file it names is missing.</returns>
    public static int Make(string output, string specPath)
    {
        Spec? spec = File.Exists(specPath) ? JsonSerializer.Deserialize<Spec>(File.ReadAllText(specPath), SpecJson) : null;
        if (spec is null) { Console.WriteLine("no trailer spec at " + specPath); return 1; }
        foreach (string f in spec.Screenshots.Append(spec.Toast))
            if (!File.Exists(f)) { Console.WriteLine("missing " + f); return 1; }

        using var toast = new Bitmap(spec.Toast);
        int trayFrames = Frames(10.0);
        var scenes = new List<Scene>
        {
            // The tray: the icon fills, the projection turns it red, the warning arrives while there is time.
            new(trayFrames, Backdrop(spec.Badge, spec.TrayHeadline, spec.TraySubline, new PointF(W / 2f, 560)),
                (g, t) => Tray(g, t, toast), ThumbnailAt: (int)(trayFrames * 0.86)),
        };

        // The interface, as the listing's screenshots show it.
        foreach (string shot in spec.Screenshots)
            scenes.Add(Still(shot, 4.0));

        scenes.Add(new(Frames(5.0), OnYourPc(spec.Badge, spec.LocalHeadline, spec.LocalSubline), null));
        scenes.Add(new(Frames(4.5), Closing(spec.Badge, spec.End), null));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        string thumbnail = Path.ChangeExtension(output, ".png");
        using Bitmap frame = StoreCanvas.NewCanvas();
        using Graphics g = StoreCanvas.Prepare(frame);
        byte[] pixels = new byte[W * H * 4];
        using Process ffmpeg = StartFfmpeg(output);
        int written = 0;
        try
        {
            using Stream input = ffmpeg.StandardInput.BaseStream;
            foreach (Scene scene in scenes)
            {
                for (int f = 0; f < scene.Frames; f++)
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(scene.Background, 0, 0);
                    g.CompositingMode = CompositingMode.SourceOver;
                    scene.Draw?.Invoke(g, f / (float)scene.Frames);

                    // In and out through the backdrop's dark, so scenes do not cut.
                    int edge = Math.Min(f, scene.Frames - 1 - f);
                    if (edge < Fade)
                    {
                        using var dark = new SolidBrush(Color.FromArgb((int)(255 * (1 - edge / (float)Fade)), StoreCanvas.Bottom));
                        g.FillRectangle(dark, 0, 0, W, H);
                    }

                    if (f == scene.ThumbnailAt)
                        using (FileStream fs = OutFile.Create(thumbnail)) frame.Save(fs, ImageFormat.Png);

                    BitmapData data = frame.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    try { System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length); }
                    finally { frame.UnlockBits(data); }
                    input.Write(pixels);
                    written++;
                }
                scene.Background.Dispose();
            }
        }
        catch (IOException e)
        {
            Console.WriteLine("ffmpeg stopped reading: " + e.Message);
        }

        ffmpeg.WaitForExit();
        Console.WriteLine($"trailer: {Path.GetFullPath(output)}  {written} frames, {written / (double)Fps:0.0} s; thumbnail {Path.GetFullPath(thumbnail)}");
        return ffmpeg.ExitCode;
    }

    private static int Frames(double seconds) => (int)Math.Round(seconds * Fps);

    private static float Ease(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return t * t * (3 - 2 * t);
    }

    // The brand mark, the badge, the headline and the backdrop: everything in a scene that does not move.
    private static Bitmap Backdrop(string badge, string headline, string subline, PointF glowAt)
    {
        Bitmap bmp = StoreCanvas.NewCanvas();
        using Graphics g = StoreCanvas.Prepare(bmp);
        StoreCanvas.Backdrop(g, W, H, glowAt);
        float rule = StoreCanvas.BrandMark(g);
        StoreCanvas.Badge(g, badge, rule);
        StoreCanvas.Headline(g, headline, subline, rule + 40);
        return bmp;
    }

    // The tray, large in the middle and small on a taskbar below: the used share climbs through the scene, the
    // projection turns to danger a little past half way, and the warning slides in over the taskbar's corner.
    private static void Tray(Graphics g, float t, Bitmap toast)
    {
        // It stops at 61% used, the 39% left the captured warning says, so the two never disagree on screen.
        // The clock moves with the same progress, a working day from 09:00 to 17:00: the share is used up by
        // hours of work, and a fixed clock would say it went in a moment.
        float progress = Ease(t / 0.78f);
        double pct = 0.12 + 0.49 * progress;
        TimeSpan time = TimeSpan.FromMinutes(9 * 60 + Math.Round(8 * 60 * progress));
        Projection verdict = t < 0.12f ? Projection.Unknown : t < 0.52f ? Projection.Ok : Projection.Danger;

        // The big tile, lit from behind.
        const float big = 300;
        PointF centre = new(W * 0.36f, 560);
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(centre.X - 260, centre.Y - 260, 520, 520);
            Color halo = verdict == Projection.Danger ? Color.FromArgb(255, 60, 40) : Color.FromArgb(70, 220, 90);
            using var glow = new PathGradientBrush(path)
            {
                CenterColor = Color.FromArgb(verdict == Projection.Unknown ? 0 : 60, halo),
                SurroundColors = new[] { Color.FromArgb(0, halo) },
            };
            g.FillPath(glow, path);
        }
        using (Bitmap tile = IconRenderer.Render(pct, IconRenderer.State.Ok, false, (int)big, verdict))
            g.DrawImage(tile, centre.X - big / 2, centre.Y - big / 2, big, big);

        // The taskbar's corner: a chevron, the icon at the size a taskbar shows it, and the clock's place.
        var bar = new RectangleF(W * 0.36f - 260, 860, 520, 120);
        StoreCanvas.TaskbarGlass(g, bar, 26);
        StoreCanvas.Chevron(g, new PointF(bar.X + 62, bar.Y + bar.Height / 2), 13, 3.5f);
        const float small = 72;
        using (Bitmap tile = IconRenderer.Render(pct, IconRenderer.State.Ok, false, (int)small, verdict))
            g.DrawImage(tile, bar.X + 120, bar.Y + (bar.Height - small) / 2, small, small);
        using (var clock = new SolidBrush(Color.FromArgb(220, StoreCanvas.Ink)))
        using (var font = new Font("Segoe UI", 34, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            g.DrawString(time.ToString(@"hh\:mm"), font, clock, new RectangleF(bar.X + 230, bar.Y, 220, bar.Height), middle);

        // The warning, sliding in from the right once the verdict has turned.
        float u = Ease((t - 0.58f) / 0.14f);
        if (u <= 0) return;
        float tw = 760, th = toast.Height * (tw / toast.Width);
        float x = W - 64 - tw + (1 - u) * (tw + 120);
        float y = 560 - th / 2;
        g.DrawImage(toast, x, y, tw, th);
    }

    // A composed screenshot, eased in from 100% to 105% so it does not sit like a slide.
    private static Scene Still(string png, double seconds)
    {
        var shot = new Bitmap(png);
        Bitmap blank = StoreCanvas.NewCanvas();
        using (Graphics g = Graphics.FromImage(blank)) g.Clear(StoreCanvas.Bottom);
        return new Scene(Frames(seconds), blank, (g, t) =>
        {
            float zoom = 1 + 0.05f * Ease(t);
            float w = W * zoom, h = H * zoom;
            g.DrawImage(shot, (W - w) / 2, (H - h) / 2, w, h);
        });
    }

    // "Computed on your PC": a monitor with the app's tile lit on it, and the claim under it, centred.
    private static Bitmap OnYourPc(string badge, string headline, string subline)
    {
        Bitmap bmp = StoreCanvas.NewCanvas();
        using Graphics g = StoreCanvas.Prepare(bmp);
        StoreCanvas.Backdrop(g, W, H, new PointF(W / 2f, 420));
        float rule = StoreCanvas.BrandMark(g);
        StoreCanvas.Badge(g, badge, rule);

        var screen = new RectangleF(W / 2f - 250, 250, 500, 300);
        using (GraphicsPath glass = StoreCanvas.RoundedRect(screen, 20))
        {
            using (var fill = new SolidBrush(StoreCanvas.Bottom)) g.FillPath(fill, glass);
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(screen.X + screen.Width / 2 - 240, screen.Y + screen.Height / 2 - 240, 480, 480);
                using var glow = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(80, StoreCanvas.Accent),
                    SurroundColors = new[] { Color.FromArgb(0, StoreCanvas.Accent) },
                };
                GraphicsState state = g.Save();
                g.SetClip(glass);
                g.FillPath(glow, path);
                g.Restore(state);
            }
            using var frame = new Pen(StoreCanvas.Soft, 8);
            g.DrawPath(frame, glass);
        }
        using (var stand = new SolidBrush(StoreCanvas.Soft))
        {
            g.FillRectangle(stand, screen.X + screen.Width / 2 - 18, screen.Bottom + 4, 36, 46);
            using GraphicsPath foot = StoreCanvas.RoundedRect(new RectangleF(screen.X + screen.Width / 2 - 110, screen.Bottom + 48, 220, 14), 7);
            g.FillPath(stand, foot);
        }
        using (Bitmap logo = IconRenderer.RenderLogo(340))
            g.DrawImage(logo, screen.X + screen.Width / 2 - 85, screen.Y + screen.Height / 2 - 85, 170, 170);

        StoreCanvas.DrawRuns(g, StoreCanvas.Runs(headline), StoreCanvas.Face(headline, false), FontStyle.Bold, 84, 48, W - 200, W / 2f, 730, centred: true);
        StoreCanvas.DrawRuns(g, new() { (subline, false) }, StoreCanvas.Face(subline, false), FontStyle.Regular, 34, 22, W - 200, W / 2f, 800, centred: true, plain: StoreCanvas.Soft);
        return bmp;
    }

    // The closing card: the logo large, the name, the maker, the local claim and where to get it.
    private static Bitmap Closing(string badge, string end)
    {
        Bitmap bmp = StoreCanvas.NewCanvas();
        using Graphics g = StoreCanvas.Prepare(bmp);
        StoreCanvas.Backdrop(g, W, H, new PointF(W / 2f, H / 2f));
        using (Bitmap logo = IconRenderer.RenderLogo(560))
            g.DrawImage(logo, W / 2f - 140, 150, 280, 280);

        StoreCanvas.DrawRuns(g, new() { ("Usage Tray ", false), ("for Claude Code", true) }, "Segoe UI", FontStyle.Bold, 96, 56, W - 200, W / 2f, 580, centred: true);
        using StringFormat centre = new() { Alignment = StringAlignment.Center };
        using var soft = new SolidBrush(StoreCanvas.Soft);
        using var ink = new SolidBrush(StoreCanvas.Ink);
        using var accent = new SolidBrush(StoreCanvas.Accent);
        using var by = new Font("Segoe UI", 36, FontStyle.Regular, GraphicsUnit.Pixel);
        using var local = new Font(StoreCanvas.Face(badge, true), 40, FontStyle.Regular, GraphicsUnit.Pixel);
        using var where = new Font(StoreCanvas.Face(end, true), 52, FontStyle.Regular, GraphicsUnit.Pixel);
        g.DrawString("by Alegauss", by, soft, new RectangleF(0, 610, W, 60), centre);
        g.DrawString(badge, local, ink, new RectangleF(0, 720, W, 60), centre);
        g.DrawString(end, where, accent, new RectangleF(0, 800, W, 80), centre);
        return bmp;
    }

    private static Process StartFfmpeg(string output)
    {
        var start = new ProcessStartInfo("ffmpeg") { RedirectStandardInput = true, UseShellExecute = false };
        foreach (string arg in new[]
        {
            "-y", "-loglevel", "error",
            "-f", "rawvideo", "-pix_fmt", "bgra", "-s", $"{W}x{H}", "-r", $"{Fps}", "-i", "-",
            // The Store refuses a trailer with no audio channel (Modelado's MOD84 measured it: "No audio
            // channels"), so a silent stereo track runs under the picture.
            "-f", "lavfi", "-i", "anullsrc=channel_layout=stereo:sample_rate=48000",
            "-map", "0:v", "-map", "1:a", "-shortest",
            "-c:v", "libx264", "-preset", "slow", "-crf", "18", "-pix_fmt", "yuv420p",
            "-c:a", "aac", "-b:a", "128k", "-movflags", "+faststart",
            output,
        })
            start.ArgumentList.Add(arg);

        return Process.Start(start) ?? throw new InvalidOperationException("ffmpeg did not start; it must be on the PATH.");
    }
}
