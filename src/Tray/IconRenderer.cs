using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeTray;

/// <summary>
/// Draws the tray icon natively with GDI+: a rounded Claude-clay tile with the usage
/// number rendered as a vector path (outlined), so it stays crisp at the real tray
/// pixel size instead of being downscaled from a large bitmap.
/// </summary>
internal static class IconRenderer
{
    // Claude visual identity (brand color — kept for the app logo). Resolved from Brand, not spelled
    // here: this and BarBilling below were two of the twelve places one clay was written out (T310).
    private static readonly Color ClaudeClay = Brand.ClayGdi;
    private static readonly Color Amber      = Color.FromArgb(227, 179, 65);  // API-error
    private static readonly Color Dim         = Color.FromArgb(90, 96, 104);  // connecting
    private static readonly Color Cream       = Color.White;                  // the number
    private static readonly Color Stroke      = Color.FromArgb(20, 38, 60);   // dark outline

    // Tray status tile: a calm slate blue-gray base (orange read too much like a permanent
    // warning), with a deeper slate used while flashing at >=90%.
    private static readonly Color TileBlue  = Color.FromArgb(96, 120, 145);
    private static readonly Color BlueDeep  = Color.FromArgb(58, 76, 96);     // flash background

    // Fill-bar color: a neon green rising from the bottom, Task-Manager style — reads as a
    // distinct "in use" zone over the blue base. Until there are enough samples to project a
    // burn rate the bar is blue ("undefined"); once a projection exists it turns green (on
    // track), or — when usage is set to hit 100% before the window resets — warms from orange
    // toward the alarming red as usage approaches 100%.
    private static readonly Color BarUnknown = Color.FromArgb(60, 200, 255); // pre-projection cyan-azure (pops on the slate base)
    private static readonly Color BarFill    = Color.FromArgb(57, 230, 70);
    private static readonly Color BarOrange   = Color.FromArgb(255, 150, 20); // early-warning orange
    private static readonly Color BarDanger   = Color.FromArgb(255, 35, 30);  // vivid, alarming red

    // Past the included quota and paying for it (T182). Red is spoken for: it means *danger*, warming as
    // usage climbs toward 100%, and it is the right colour for work that has actually stopped. Somebody
    // who deliberately enabled extra usage and is comfortably using it is not in danger — they are in a
    // different state, and a hotter red would say "worse" where the fact is "other". So the full bar goes
    // to the brand clay: outside the projection ramp entirely (green → orange → red), outside the cyan of
    // "no projection yet" and the amber of an API error, and calm at a glance. It is the one colour on the
    // tile that means a category rather than a severity, which is exactly what this is.
    private static readonly Color BarBilling  = Brand.ClayGdi;

    // Which window the digits are about (T321), as the digits' own fill. T320 can move the figure off the
    // window the menu picked — a `0` may be the week on a tray with `5h` ticked — and the tooltip was the
    // only surface that said so, which is the one that is not on screen.
    //
    // The fill is the one attribute on this tile that had never carried meaning, so §XI.3's rule survives
    // intact: the *level* rising from the bottom is still the only thing that means projection, the 2px rule
    // at the top is still identity, and this is a third form again — the glyphs themselves. `5h` keeps
    // `Cream`, so the ordinary tray is unchanged and the colour appears only when the number is about
    // something else.
    //
    // Saturated, and T322 is why: these were `#FFE9A0` and `#FFB347`, picked against `--render`'s scope
    // sheet, which magnifies 8×. That is the one size the tray never uses — at 16px, over slate, inside a
    // dark outline, a pale warm hue is a white with a cast, which is exactly how the first one was reported.
    // The sheet is still the instrument (the pixels have to be visible at all), but the judgement belongs to
    // what a taskbar shows. They stay clear of the bar colours under them: `BarBilling`'s clay is duller
    // than either, and `BarOrange` is a fill that only appears under a *danger* verdict, which billing
    // outranks — so orange digits never sit on it.
    private static readonly Color InkWeek  = Color.FromArgb(255, 214, 61);   // week 7d — yellow
    private static readonly Color InkExtra = Color.FromArgb(255, 138, 40);   // extra usage — orange

    // Per-profile accent (T147), used only for the top-edge identity band. Chosen to be disjoint from
    // every color that already carries meaning here — the green/orange/red of the projection, the cyan
    // of "no projection yet", the amber of an API error and the slate of the tile itself — so the band
    // can never be read as a verdict or a warning. Violets and pinks are what is left, and they hold
    // their hue against both a light and a dark taskbar. More profiles than entries cycle: past three
    // the hues would stop being tellable apart anyway, and the tooltip names the profile regardless.
    private static readonly Color[] Accents =
    {
        Color.FromArgb(167, 110, 255),   // violet
        Color.FromArgb(255,  92, 188),   // pink
        Color.FromArgb(120, 140, 255),   // periwinkle
    };

    /// <summary>How many distinct profile accents there are, before they cycle.</summary>
    public static int AccentCount => Accents.Length;

    /// <summary>The accent's name, for <c>--profiles</c> to say which band a profile wears.</summary>
    public static string AccentName(int accent) => accent < 0 ? "-" : (accent % Accents.Length) switch
    {
        0 => "violet",
        1 => "pink",
        _ => "periwinkle",
    };

    // 3D bevel edges (top-left highlight, bottom-right shadow)
    private static readonly Color BevelLight = Color.FromArgb(150, 255, 255, 255);
    private static readonly Color BevelDark  = Color.FromArgb(150, 0, 0, 0);

    public enum State { Ok, Error, Connecting }

    /// <summary>
    /// Render a square tray bitmap of side <paramref name="size"/> px. The usage fill bar is
    /// colored by the projection <paramref name="verdict"/>: blue while still undefined (not
    /// enough samples yet), green when on track, and orange→red when usage is projected to hit
    /// 100% before the window resets. When <paramref name="showNumber"/> is false, only the
    /// fill bar is drawn (no digits). When <paramref name="showRemaining"/> is true the display
    /// inverts to quota left — full at 100%, draining to 0% — while the color still tracks usage.
    /// <paramref name="accent"/> is which profile the number belongs to (T147) as an index into
    /// <see cref="Accents"/>, or -1 for none — see the mark block below.
    /// <paramref name="billing"/> is the account past its included quota and paying to keep working
    /// (T182): the bar goes clay instead of the alarming red, which is for work that has stopped.
    /// <paramref name="scope"/> is which window the number is about (T321) — <c>5h</c>, <c>7d</c> or
    /// <c>extra</c> — which the digits state as their own fill, unless <paramref name="billing"/> gives them
    /// something more urgent to say (T322). It defaults to the session, so a caller that has nothing to say
    /// about scope draws the cream digits this icon always had.
    /// </summary>
    public static Bitmap Render(double pct, State state, bool flash, int size, Projection verdict = Projection.Unknown, bool showNumber = true, bool showRemaining = false, int accent = -1, bool billing = false, string scope = "5h")
    {
        // pct is always the *used* fraction. In "remaining" mode we display its complement (full
        // at 100%, draining to 0%), but the danger color still keys off `used` so the bar warms to
        // red as the limit nears regardless of which number is shown.
        double used = Math.Min(Math.Max(pct, 0.0), 1.0);
        double shown = showRemaining ? 1.0 - used : used;
        Color bg = flash ? BlueDeep : state switch
        {
            State.Error => Amber,
            State.Connecting => Dim,
            _ => TileBlue,
        };

        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        // Rounded background tile (base color)
        float radius = size * 0.18f;
        using var tile = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), radius);
        using (var brush = new SolidBrush(bg))
            g.FillPath(brush, tile);

        // Vertical fill bar (Task-Manager style): a blue level rising from the bottom,
        // proportional to the percentage — 100% = whole tile, 50% = bottom half.
        if (state == State.Ok && shown > 0)
        {
            float barH = (float)(shown * size);
            using var clip = new Region(tile);
            g.Clip = clip;
            // Blue until a projection exists; green when on track; in danger, blend orange→red
            // by how close usage is to 100%, so the bar deepens to the full alarming red as it
            // nears the limit.
            // Billing outranks the projection: a projection answers "will you run out", and for an
            // account already past its quota that question is settled — what is left to say is which
            // side of the line it is on (T182).
            Color barColor = billing ? BarBilling : verdict switch
            {
                Projection.Unknown => BarUnknown,
                Projection.Danger  => Lerp(BarOrange, BarDanger, used),
                _                  => BarFill,
            };
            using (var barBrush = new SolidBrush(barColor))
                g.FillRectangle(barBrush, 0, size - barH, size, barH);
            g.ResetClip();
        }

        // 3D beveled frame: light on top/left, dark on bottom/right → raised look.
        DrawBevel(g, new RectangleF(0, 0, size - 1, size - 1), radius, Math.Max(1f, size * 0.09f));

        // The profile mark (T147): with the profile global, "whose number is this?" is the question a
        // glance has to answer, and nothing else on the tile can — the number, the fill and the
        // projection color are identical whichever profile the icon follows.
        //
        // A thin band along the top edge, and NOT the profile's initial: rendered at the real 16px, a
        // second glyph and the number cannot both be read (measured — "P" against "54" or "100" blurs
        // both), and the number is the thing the app exists to show. The band costs the digits no space
        // at all, because it lands inside the margin FitToTile already leaves above them.
        //
        // It is a color, which §XI.3 rightly flagged: color here means *projection*. What keeps the two
        // apart is that they never share a hue (Accents is disjoint from every semantic color) or a
        // form — projection is a filled level rising from the bottom, identity is a fixed 2px rule at
        // the top. Drawn after the bevel, so it sits over the fill bar even at 100%.
        if (accent >= 0)
        {
            using var clip = new Region(tile);
            g.Clip = clip;
            using (var brush = new SolidBrush(Accents[accent % Accents.Length]))
                g.FillRectangle(brush, 0, 0, size, Math.Max(2f, size * 0.13f));
            g.ResetClip();
        }

        if (!showNumber)
            return bmp;

        string num = ((int)Math.Round(shown * 100)).ToString();

        using var family = new FontFamily("Segoe UI");
        const int bold = (int)FontStyle.Bold;
        const float em = 100f;

        // Foreground: the digits only, large but with a clear left/right margin.
        using var text = new GraphicsPath();
        text.AddString(num, family, bold, em, new PointF(0, 0), StringFormat.GenericTypographic);
        FitToTile(text, size, size * 0.16f, size * 0.24f);

        // Dark outline then white fill — the outline keeps the number legible when tiny.
        float penW = Math.Max(1.2f, size * 0.11f);
        using (var pen = new Pen(Stroke, penW) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, text);
        using (var fill = new SolidBrush(NumberInk(scope, billing)))
            g.FillPath(fill, text);

        return bmp;
    }

    /// <summary>
    /// What colour the digits are drawn in: the news where there is news, and otherwise the window the
    /// number is about (T321, rescoped by T322).
    ///
    /// <para><b>Orange is about the account, not about a menu pick.</b> It was wired to the metric
    /// <c>extra</c> — the overage window — and an account past its included quota is rarely watching that
    /// window: T320 moves the figure onto the week, so the state this colour existed for drew the week's
    /// yellow and said nothing about money. Worst in <em>remaining</em> mode, where at 0% left the bar drains
    /// to nothing and takes the clay with it, leaving the digits as the tile's only channel.</para>
    ///
    /// <para><b><paramref name="billing"/> only, never stopped.</b> Orange says <em>paying</em>, and putting
    /// it over work that has actually halted is T182's defect in a new place — so a stopped account keeps its
    /// window's colour, which is the honest reading: that window is spent.</para>
    ///
    /// <para><c>internal</c> so <c>--selftest</c> holds the three apart, asserts that billing outranks the
    /// window and that a scope nobody here spells falls back to the cream the icon always had — a window
    /// silently reading as the session is the one failure no picture can catch, because it looks like a
    /// session.</para></summary>
    internal static Color NumberInk(string scope, bool billing = false)
        => billing || scope == "extra" ? InkExtra
         : scope == "7d" ? InkWeek
         : Cream;

    /// <summary>
    /// Render the application icon (used for the .exe / installer / shortcuts): the same
    /// Claude-clay beveled tile as the tray, but with a clean white spark mark instead of a
    /// usage number — a recognizable logo at any size. When <paramref name="errorBadge"/> is
    /// true a small red dot is drawn in the bottom-right corner to flag an API error (e.g. an
    /// HTTP 403) while still showing the recognizable logo rather than a scary amber number.
    /// </summary>
    public static Bitmap RenderLogo(int size, bool errorBadge = false)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float radius = size * 0.18f;
        using var tile = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), radius);
        using (var brush = new SolidBrush(ClaudeClay))
            g.FillPath(brush, tile);

        DrawBevel(g, new RectangleF(0, 0, size - 1, size - 1), radius, Math.Max(1f, size * 0.05f));

        // Four-point spark, centered — legible even at 16px.
        float cx = size / 2f, cy = size / 2f;
        using var spark = Star(cx, cy, size * 0.42f, size * 0.15f, 4, -Math.PI / 2);
        using (var pen = new Pen(Stroke, Math.Max(1f, size * 0.045f)) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, spark);
        using (var fill = new SolidBrush(Cream))
            g.FillPath(fill, spark);

        if (errorBadge)
            DrawErrorBadge(g, size);

        return bmp;
    }

    /// <summary>
    /// Draw a notification-style error badge in the bottom-right corner: a vivid red dot inside a
    /// white halo ring, so it reads clearly against the warm clay tile at any tray size.
    /// </summary>
    private static void DrawErrorBadge(Graphics g, int size)
    {
        float dia = size * 0.38f;              // dot diameter
        float margin = size * 0.03f;
        float dx = size - dia - margin;        // dot origin (bottom-right, slightly inset)
        float dy = size - dia - margin;
        float ring = Math.Max(1f, size * 0.055f);

        using (var halo = new SolidBrush(Cream))
            g.FillEllipse(halo, dx - ring, dy - ring, dia + 2 * ring, dia + 2 * ring);
        using (var dot = new SolidBrush(BarDanger))
            g.FillEllipse(dot, dx, dy, dia, dia);
    }

    /// <summary>
    /// Render the GitHub social-preview banner (1280×640): the logo tile on a warm-dark
    /// gradient next to the project name and a one-line tagline.
    /// </summary>
    public static Bitmap RenderSocial(int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Warm near-black gradient backdrop.
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                   Color.FromArgb(32, 24, 20), Color.FromArgb(14, 10, 8), 25f))
            g.FillRectangle(bg, 0, 0, w, h);

        // Logo tile, vertically centered on the left.
        int logo = 300;
        int lx = 110, ly = (h - logo) / 2;
        using (var tile = RenderLogo(logo))
            g.DrawImage(tile, lx, ly, logo, logo);

        int tx = lx + logo + 72;
        using var titleFont = new Font("Segoe UI", 82f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tagFont = new Font("Segoe UI Semilight", 31f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var urlFont = new Font("Segoe UI", 26f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.FromArgb(245, 244, 242));
        using var clay = new SolidBrush(ClaudeClay);
        using var muted = new SolidBrush(Color.FromArgb(185, 182, 178));
        using var faint = new SolidBrush(Color.FromArgb(130, 126, 122));

        g.DrawString("Claude Code", titleFont, white, tx, 168);
        g.DrawString("Tray", titleFont, clay, tx, 268);

        var tagRect = new RectangleF(tx + 2, 396, w - tx - 80, 170);
        g.DrawString("Rate-limit %, burn-rate projection, and a 24h usage breakdown — always in your Windows tray.",
            tagFont, muted, tagRect);

        g.DrawString("github.com/alegauss/claude-tray", urlFont, faint, tx + 2, h - 70);

        return bmp;
    }

    /// <summary>Linearly interpolate between two colors; <paramref name="t"/> is clamped to [0,1].</summary>
    private static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Min(Math.Max(t, 0.0), 1.0);
        return Color.FromArgb(
            (int)Math.Round(a.R + (b.R - a.R) * t),
            (int)Math.Round(a.G + (b.G - a.G) * t),
            (int)Math.Round(a.B + (b.B - a.B) * t));
    }

    /// <summary>A pointed star polygon (alternating outer/inner radius), first point at angle <paramref name="rot"/>.</summary>
    private static GraphicsPath Star(float cx, float cy, float outer, float inner, int points, double rot)
    {
        int n = points * 2;
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            double ang = rot + Math.PI * i / points;
            float rad = (i % 2 == 0) ? outer : inner;
            pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
        }
        var p = new GraphicsPath();
        p.AddPolygon(pts);
        return p;
    }

    /// <summary>Scale and center a path into the tile, leaving the given horizontal/vertical margins (px).</summary>
    private static void FitToTile(GraphicsPath p, int size, float padX, float padY)
    {
        // GetBounds() on a glyph path returns the bounding box of the Bézier *control points*,
        // which bulge asymmetrically past the actual ink (e.g. the curves in "5"/"4") and throw
        // the centering off. Measure on a flattened copy so the bounds are the true ink extent.
        using var probe = (GraphicsPath)p.Clone();
        probe.Flatten();
        RectangleF b = probe.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;
        float scale = Math.Min((size - 2 * padX) / b.Width, (size - 2 * padY) / b.Height);
        using var m = new Matrix();
        // Center the glyphs, nudged 1px left so the number reads as optically centered in the tile.
        m.Translate(size / 2f - 1f, size / 2f);
        m.Scale(scale, scale);
        m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
        p.Transform(m);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// Draw a 3D bevel around a rounded rect: highlight along the top+left half, shadow
    /// along the bottom+right half (split at the top-right and bottom-left corners),
    /// giving the tile a raised look. GDI+ angles: 0°=E, 90°=S, 180°=W, 270°=N.
    /// </summary>
    private static void DrawBevel(Graphics g, RectangleF r, float radius, float w)
    {
        float d = radius * 2;
        float inset = w / 2f;                 // keep the stroke inside the tile
        RectangleF ir = RectangleF.Inflate(r, -inset, -inset);
        float id = Math.Min(d, Math.Min(ir.Width, ir.Height));

        using var light = new GraphicsPath();
        light.AddArc(ir.X, ir.Bottom - id, id, id, 135, 45);     // upper part of bottom-left corner
        light.AddArc(ir.X, ir.Y, id, id, 180, 90);               // top-left corner
        light.AddArc(ir.Right - id, ir.Y, id, id, 270, 45);      // left part of top-right corner

        using var dark = new GraphicsPath();
        dark.AddArc(ir.Right - id, ir.Y, id, id, 315, 45);       // right part of top-right corner
        dark.AddArc(ir.Right - id, ir.Bottom - id, id, id, 0, 90); // bottom-right corner
        dark.AddArc(ir.X, ir.Bottom - id, id, id, 90, 45);       // lower part of bottom-left corner

        using (var pen = new Pen(BevelLight, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawPath(pen, light);
        using (var pen = new Pen(BevelDark, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawPath(pen, dark);
    }
}
