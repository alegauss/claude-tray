using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeTray;

/// <summary>
/// The Store listing's look (T464), after Modelado's MOD81: the brand mark in the top-left corner, a badge
/// under it saying everything is computed on the PC, a headline whose stressed words are in the accent, a
/// line under it, and the window on the logo's warm browns, with a burn-up chart drawn faintly into two
/// corners — "usage" without a word, where Modelado's wire mesh says "3D". The screenshots, the hero art,
/// the poster and box art and every frame of the trailer stand on the same backdrop, so the listing reads
/// as one piece. The window is drawn as captured: the Store's policy, and the people who install the app,
/// both expect the listing to show it as it is.
/// </summary>
internal static class StoreCanvas
{
    public const int Width = 1920;
    public const int Height = 1080;
    private const float Margin = 64;

    // The headline's words in [brackets] are drawn in the accent, a lighter clay than the logo's so it
    // holds against the dark; the rest in Ink.
    internal static readonly Color Accent = Color.FromArgb(240, 146, 112);
    internal static readonly Color Ink = Color.FromArgb(247, 242, 236);
    internal static readonly Color Soft = Color.FromArgb(206, 192, 182);
    private static readonly Color Top = Color.FromArgb(64, 37, 28);
    internal static readonly Color Bottom = Color.FromArgb(16, 11, 9);

    internal static Bitmap NewCanvas(int w = Width, int h = Height) =>
        new(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

    internal static Graphics Prepare(Bitmap bmp)
    {
        Graphics g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    /// <summary>One listing screenshot: a capture composed at 1920 x 1080 under its headline and line.</summary>
    public static Bitmap Shot(Bitmap capture, string headline, string subline, string badge)
    {
        Bitmap bmp = NewCanvas();
        using Graphics g = Prepare(bmp);
        RectangleF frame = WindowFrame(capture);
        Backdrop(g, Width, Height, new PointF(frame.X + frame.Width / 2f, Math.Min(frame.Y + frame.Height / 2f, Height * 0.62f)));
        float rule = BrandMark(g);
        Badge(g, badge, rule);
        Headline(g, headline, subline, rule + 40);
        Window(g, capture, frame);
        return bmp;
    }

    // Below the badge, which ends at 216, with room to breathe. A window taller than the room runs off the
    // bottom rather than shrinking to a thumbnail: the top of a window is what says what it is. Never
    // enlarged past 1.4 — a toast is already rendered at twice its size.
    internal static RectangleF WindowFrame(Bitmap capture)
    {
        const float top = 252, side = Width - 2 * Margin;
        float room = Height - 30 - top;
        float scale = Math.Min(Math.Min(side * 0.86f / capture.Width, room * 1.3f / capture.Height), 1.4f);
        float w = capture.Width * scale, h = capture.Height * scale;
        float y = h <= room ? top + (room - h) / 2f : top + 10;
        return new RectangleF((Width - w) / 2f, y, w, h);
    }

    /// <summary>
    /// The backdrop at any size, drawn on the 1920 x 1080 grid scaled to the width: the warm gradient, a clay
    /// glow behind what the picture is about, and the chart in two corners.
    /// </summary>
    internal static void Backdrop(Graphics g, int width, int height, PointF glowAt)
    {
        float scale = width / (float)Width;
        using (var fill = new LinearGradientBrush(new Rectangle(0, 0, width, height), Top, Bottom, 90f))
            g.FillRectangle(fill, 0, 0, width, height);

        float r = 1000 * scale;
        using (var path = new GraphicsPath())
        {
            path.AddEllipse(glowAt.X - r, glowAt.Y - r, r * 2, r * 2);
            using var glow = new PathGradientBrush(path)
            {
                CenterPoint = glowAt,
                CenterColor = Color.FromArgb(70, Brand.ClayGdi),
                SurroundColors = new[] { Color.FromArgb(0, Brand.ClayGdi) },
            };
            g.FillPath(glow, path);
        }

        // The chart, bottom left, and the same turned half round into the top right — fainter there, where
        // the headline stands over it.
        GraphicsState state = g.Save();
        g.ScaleTransform(scale, scale);
        Chart(g, 1f);
        g.TranslateTransform(Width, height / scale);
        g.ScaleTransform(-1, -1);
        Chart(g, 0.35f);
        g.Restore(state);
    }

    // A burn-up chart seen up close: a grid fading away from the corner, the used share climbing in steps,
    // and the dashed projection running on from where it stands — the app's own picture of a week.
    private static void Chart(Graphics g, float strength)
    {
        const float left = -20, bottom = Height + 10, span = 900, tall = 430, cell = 60;
        float Fade(float x, float y) => Math.Max(0f, 1f - (float)Math.Sqrt(Math.Pow((x - left) / span, 2) + Math.Pow((bottom - y) / tall, 2)) * 0.85f);
        Color A(int alpha) => Color.FromArgb((int)(alpha * strength), Accent);

        using (var dot = new SolidBrush(Color.Empty))
            for (float x = left; x <= left + span; x += cell)
                for (float y = bottom; y >= bottom - tall; y -= cell)
                {
                    int a = (int)(90 * Fade(x, y));
                    if (a <= 0) continue;
                    dot.Color = A(a);
                    g.FillEllipse(dot, x - 2.2f, y - 2.2f, 4.4f, 4.4f);
                }

        // The used share: flat stretches (nights) and climbs (work), up to "now" at two thirds of the span.
        float[] steps = { 0.00f, 0.04f, 0.04f, 0.13f, 0.17f, 0.17f, 0.17f, 0.29f, 0.36f, 0.36f, 0.36f, 0.47f, 0.52f, 0.52f, 0.60f };
        var pts = new List<PointF>();
        float stepW = span * 0.66f / (steps.Length - 1);
        for (int i = 0; i < steps.Length; i++)
            pts.Add(new PointF(left + i * stepW, bottom - steps[i] * tall));

        using (var line = new Pen(A(110), 3.2f) { LineJoin = LineJoin.Round })
            g.DrawLines(line, pts.ToArray());

        PointF now = pts[^1];
        using (var dash = new Pen(A(80), 2.4f) { DashPattern = new[] { 4f, 3f } })
            g.DrawLine(dash, now, new PointF(left + span * 0.97f, bottom - tall * 0.93f));
        using (var mark = new SolidBrush(A(150)))
            g.FillEllipse(mark, now.X - 7, now.Y - 7, 14, 14);
    }

    /// <summary>The logo and the name in the top-left corner, and the rule beside them; returns the rule's x.</summary>
    internal static float BrandMark(Graphics g)
    {
        using (Bitmap logo = IconRenderer.RenderLogo(168))
            g.DrawImage(logo, Margin, 52, 84, 84);

        using var name = new Font("Segoe UI Semibold", 44f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var by = new Font("Segoe UI", 24f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var ink = new SolidBrush(Ink);
        using var soft = new SolidBrush(Soft);
        using StringFormat typo = Typographic();
        g.DrawString("Usage Tray", name, ink, 166, 50, typo);
        g.DrawString("for Claude Code", by, soft, 168, 106, typo);

        float end = 166 + Math.Max(g.MeasureString("Usage Tray", name, PointF.Empty, typo).Width,
                                   g.MeasureString("for Claude Code", by, PointF.Empty, typo).Width + 2);
        float rule = Math.Max(430, end + 28);
        using var pen = new Pen(Color.FromArgb(90, Soft), 2);
        g.DrawLine(pen, rule, 50, rule, 150);
        return rule;
    }

    /// <summary>
    /// The badge under the brand mark, on every screenshot and every trailer frame: the app's first claim, in
    /// the listing's language. A shield and the words on an accent-tinted pill, the type shrinking before
    /// the pill reaches the rule beside it.
    /// </summary>
    internal static void Badge(Graphics g, string text, float rule)
    {
        const float left = Margin, top = 172, height = 44;
        float room = rule - left - 16;
        using StringFormat typo = Typographic();
        float size = 21;
        Font font = new(Face(text, semibold: true), size, FontStyle.Regular, GraphicsUnit.Pixel);
        while (size > 14 && g.MeasureString(text, font, PointF.Empty, typo).Width + 62 > room)
        {
            font.Dispose();
            size -= 0.5f;
            font = new Font(Face(text, semibold: true), size, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        using (font)
        {
            float width = g.MeasureString(text, font, PointF.Empty, typo).Width + 62;
            using (GraphicsPath pill = RoundedRect(new RectangleF(left, top, width, height), height / 2))
            {
                using var fill = new SolidBrush(Color.FromArgb(40, Accent));
                g.FillPath(fill, pill);
                using var edge = new Pen(Color.FromArgb(160, Accent), 1.5f);
                g.DrawPath(edge, pill);
            }

            // A shield with a tick: kept here, on this PC.
            float cx = left + 26, cy = top + height / 2;
            using (var shield = new GraphicsPath())
            {
                shield.AddLine(cx, cy - 12, cx + 10, cy - 8);
                shield.AddBezier(cx + 10, cy - 8, cx + 10, cy + 3, cx + 6, cy + 9, cx, cy + 13);
                shield.AddBezier(cx, cy + 13, cx - 6, cy + 9, cx - 10, cy + 3, cx - 10, cy - 8);
                shield.CloseFigure();
                using var accent = new SolidBrush(Accent);
                g.FillPath(accent, shield);
            }
            using (var tick = new Pen(Bottom, 2.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(tick, new[] { new PointF(cx - 4.5f, cy + 0.5f), new PointF(cx - 1, cy + 4), new PointF(cx + 5, cy - 3.5f) });

            using var words = new SolidBrush(Ink);
            float textH = font.GetHeight(g);
            g.DrawString(text, font, words, left + 46, cy - textH / 2f + 1, typo);
        }
    }

    /// <summary>
    /// The headline, its words in [brackets] in the accent, and the smaller line under it, left-aligned at
    /// <paramref name="left"/>; each shrinks before it runs past the right margin, down to a floor.
    /// </summary>
    internal static void Headline(Graphics g, string headline, string subline, float left)
    {
        float room = Width - Margin - left;
        DrawRuns(g, Runs(headline), Face(headline, semibold: false), FontStyle.Bold, 66, 40, room, left, 112, centred: false);

        using StringFormat typo = Typographic();
        float size = 31;
        Font small = new(Face(subline, semibold: false), size, FontStyle.Regular, GraphicsUnit.Pixel);
        while (size > 22 && g.MeasureString(subline, small, PointF.Empty, typo).Width > room)
        {
            small.Dispose();
            size -= 1;
            small = new Font(Face(subline, semibold: false), size, FontStyle.Regular, GraphicsUnit.Pixel);
        }
        using (small)
        using (var soft = new SolidBrush(Soft))
            g.DrawString(subline, small, soft, left + 2, 162 - Ascent(small), typo);
    }

    /// <summary>"[" and "]" split a headline into runs that alternate plain and stressed.</summary>
    internal static List<(string Text, bool Stressed)> Runs(string headline)
    {
        var runs = new List<(string, bool)>();
        bool stressed = false;
        foreach (string part in headline.Split('[', ']'))
        {
            if (part.Length > 0) runs.Add((part, stressed));
            stressed = !stressed;
        }
        return runs;
    }

    /// <summary>
    /// Draws runs on one baseline at the largest size from <paramref name="max"/> down to <paramref name="min"/>
    /// that fits <paramref name="room"/>; from <paramref name="x"/>, or centred on it.
    /// </summary>
    internal static void DrawRuns(Graphics g, List<(string Text, bool Stressed)> runs, string family, FontStyle style,
        float max, float min, float room, float x, float baseline, bool centred, Color? plain = null)
    {
        Color ink = plain ?? Ink;
        using StringFormat typo = Typographic();
        float size = max;
        Font font = new(family, size, style, GraphicsUnit.Pixel);
        float Total() => runs.Sum(r => g.MeasureString(r.Text, font, PointF.Empty, typo).Width);
        while (size > min && Total() > room)
        {
            font.Dispose();
            size -= 1;
            font = new Font(family, size, style, GraphicsUnit.Pixel);
        }

        using (font)
        {
            float at = centred ? x - Total() / 2f : x;
            float y = baseline - Ascent(font);
            using var paint = new SolidBrush(ink);
            foreach ((string text, bool accent) in runs)
            {
                paint.Color = accent ? Accent : ink;
                g.DrawString(text, font, paint, at, y, typo);
                at += g.MeasureString(text, font, PointF.Empty, typo).Width;
            }
        }
    }

    /// <summary>The window: an opaque capture gets rounded corners, a shadow and a faint edge, the frame a
    /// window has on a desktop; one with a transparent corner (a toast, which has its own) is drawn as it is.</summary>
    internal static void Window(Graphics g, Bitmap capture, RectangleF frame)
    {
        if (capture.GetPixel(0, 0).A != 255)
        {
            g.DrawImage(capture, frame);
            return;
        }

        const float r = 14;
        using (var shadow = new SolidBrush(Color.FromArgb(10, 0, 0, 0)))
            for (int i = 14; i >= 1; i--)
            {
                using GraphicsPath layer = RoundedRect(new RectangleF(frame.X - i * 2, frame.Y - i * 2 + 18, frame.Width + i * 4, frame.Height + i * 4), r + i * 2);
                g.FillPath(shadow, layer);
            }

        using GraphicsPath clip = RoundedRect(frame, r);
        GraphicsState state = g.Save();
        g.SetClip(clip);
        g.DrawImage(capture, frame);
        g.Restore(state);
        using var edge = new Pen(Color.FromArgb(50, 255, 255, 255), 1.5f);
        g.DrawPath(edge, clip);
    }

    /// <summary>
    /// The 16:9 Super hero art, without which the Store plays no trailer (T464): the backdrop, and right of
    /// centre the notification area blown up — three tray icons as the week fills, green, warming, red. No
    /// text: the Store sets the title and the buttons over the left third and the bottom quarter.
    /// </summary>
    public static Bitmap Hero(int w, int h)
    {
        Bitmap bmp = NewCanvas(w, h);
        using Graphics g = Prepare(bmp);
        float s = h / (float)Height;
        PointF centre = new(w * 0.66f, h * 0.42f);
        Backdrop(g, w, h, centre);

        float icon = 190 * s, gap = 70 * s, pad = 64 * s;
        float barW = pad * 2 + 70 * s + 3 * icon + 3 * gap, barH = icon + 2 * 58 * s;
        var bar = new RectangleF(centre.X - barW / 2, centre.Y - barH / 2, barW, barH);
        TaskbarGlass(g, bar, 46 * s);

        float x = bar.X + pad;
        Chevron(g, new PointF(x + 26 * s, bar.Y + barH / 2), 22 * s, 6 * s);
        x += 70 * s + gap;
        foreach ((double pct, Projection verdict) in new[] { (0.34, Projection.Ok), (0.66, Projection.Danger), (0.93, Projection.Danger) })
        {
            using Bitmap tile = IconRenderer.Render(pct, IconRenderer.State.Ok, false, (int)icon, verdict);
            g.DrawImage(tile, x, bar.Y + (barH - icon) / 2, icon, icon);
            x += icon + gap;
        }
        return bmp;
    }

    /// <summary>A strip of the taskbar's dark glass, with a shadow under it.</summary>
    internal static void TaskbarGlass(Graphics g, RectangleF bar, float radius)
    {
        using (var shadow = new SolidBrush(Color.FromArgb(12, 0, 0, 0)))
            for (int i = 12; i >= 1; i--)
            {
                float d = i * radius / 18f;
                using GraphicsPath layer = RoundedRect(new RectangleF(bar.X - d, bar.Y - d + radius * 0.5f, bar.Width + 2 * d, bar.Height + 2 * d), radius + d);
                g.FillPath(shadow, layer);
            }
        using GraphicsPath glass = RoundedRect(bar, radius);
        using (var fill = new LinearGradientBrush(bar, Color.FromArgb(235, 44, 38, 36), Color.FromArgb(235, 28, 24, 23), 90f))
            g.FillPath(fill, glass);
        using var edge = new Pen(Color.FromArgb(60, 255, 255, 255), Math.Max(1.5f, radius / 30f));
        g.DrawPath(edge, glass);
    }

    /// <summary>The notification area's "show hidden icons" chevron.</summary>
    internal static void Chevron(Graphics g, PointF at, float half, float width)
    {
        using var pen = new Pen(Color.FromArgb(220, Ink), width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        g.DrawLines(pen, new[] { new PointF(at.X - half, at.Y + half / 2), new PointF(at.X, at.Y - half / 2), new PointF(at.X + half, at.Y + half / 2) });
    }

    // Segoe UI has no Chinese, and a fallback chosen glyph by glyph mixes faces in one line: text that holds
    // any is set wholly in Microsoft YaHei UI, Windows' own (as Modelado's MOD92).
    internal static string Face(string text, bool semibold)
    {
        bool cjk = text.Any(c => c is >= '⺀' and <= '鿿' or >= '豈' and <= '﫿' or >= '＀' and <= '￯');
        if (cjk) return "Microsoft YaHei UI";
        return semibold ? "Segoe UI Semibold" : "Segoe UI";
    }

    internal static StringFormat Typographic()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
        return f;
    }

    internal static float Ascent(Font font) =>
        font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style);

    internal static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
