using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
// System.Drawing (WinForms) is in the global usings, so disambiguate the WPF types.
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace ClaudeTray;

/// <summary>
/// A bespoke, on-brand "toast" for the unexpected-reset event — a borderless WPF window that slides
/// up from the bottom-right with a rose gradient (see <see cref="Palette"/>), a confetti burst, and the
/// weekly usage bar visibly emptying from its old level to 0%. It deliberately replaces the plain system
/// balloon for this one happy event: the reset hands your quota back, so it should feel like good
/// news. Shown non-modally from the tray; auto-dismisses, or the user can open Claude Code or close it.
/// </summary>
internal partial class ToastWindow : Window
{
    /// <summary>Color theme per notification type, so each is identifiable at a glance.</summary>
    internal enum ToastTheme { Surprise, Bonus, Weekly, Session, Context, ExtraUsage }

    private bool _closing;
    private double _targetScale = 1.0; // available quota after the event; the bar animates to this
    private ToastTheme _theme = ToastTheme.Surprise;

    // Festive palette for the confetti — golds, white, coral and soft pastels read well on the gradient.
    private static readonly Color[] ConfettiColors =
    {
        (Color)ColorConverter.ConvertFromString("#FFD93D"),
        Colors.White,
        (Color)ColorConverter.ConvertFromString("#FF8A65"),
        (Color)ColorConverter.ConvertFromString("#9DE0AD"),
        (Color)ColorConverter.ConvertFromString("#C39BD3"),
        (Color)ColorConverter.ConvertFromString("#FFF3B0"),
    };

    public ToastWindow(string emoji, string title, string subtitle, double fromUsage, double toUsage,
        string caption, string quotaLabel, ToastTheme theme)
    {
        InitializeComponent();

        double fromAvail = 1 - Math.Clamp(fromUsage, 0, 1);
        _targetScale = 1 - Math.Clamp(toUsage, 0, 1); // quota left after the event

        _theme = theme;
        Card.Background = Gradient(theme);
        Emoji.Text = emoji;
        TitleText.Text = title;
        Subtitle.Text = subtitle;
        QuotaLabel.Text = quotaLabel;
        AvailPct.Text = $"{(int)Math.Round(_targetScale * 100)}%";
        Caption.Text = caption;
        FillScale.ScaleX = fromAvail; // quota left before; animates up to _targetScale

        Loaded += OnLoaded;
    }

    /// <summary>
    /// The card's three gradient stops per type — light, mid, deep — read top-left→bottom-right. One row
    /// per <see cref="ToastTheme"/> and <b>no two rows alike</b>: a colour here is a claim about what kind
    /// of news this is, so two types sharing one says they mean the same thing.
    ///
    /// <para><b>Why rose, not clay, for Surprise (T188).</b> Clay is what the tray icon's bar (T182) and
    /// the weekly chart's second axis (T183) wear for <em>past the included quota</em>, and T184 gave the
    /// extra-usage toast the same clay on purpose, so one fact looks the same everywhere. Surprise — the
    /// weekly limit resetting <em>early</em>, unambiguously good news — had been clay since the toasts
    /// shipped, but only as the fallback arm of this switch: a default, never a choice. So it is the one
    /// that moves, into the good-news family Bonus's violet already showed had room.</para>
    ///
    /// <para>Every value is spelled out and there is no fallback arm, which is what stopped the borrowing:
    /// a theme added tomorrow will not compile until it picks a colour, and <c>--selftest</c> asserts the
    /// rows stay distinct.</para>
    /// </summary>
    internal static (string Light, string Mid, string Deep) Palette(ToastTheme theme) => theme switch
    {
        ToastTheme.Surprise => ("#E98BB4", "#CE5F8F", "#92325F"),  // rose — a happy surprise
        ToastTheme.Bonus => ("#B98BDD", "#9460C6", "#5E3496"),     // violet
        ToastTheme.Weekly => ("#43B894", "#23987A", "#136E58"),    // teal/green
        ToastTheme.Session => ("#6BA3E6", "#3F79CF", "#234E96"),   // blue
        ToastTheme.Context => ("#D9A85C", "#BE8535", "#8A5E1E"),   // ochre — a nudge, not a party
        // Clay: the colour the icon's bar and the weekly chart's second axis already wear for
        // "past the included quota", so the same fact looks the same wherever it appears (T184).
        ToastTheme.ExtraUsage => ("#E89072", "#D97757", "#B0512F"),
        _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, "no card colour for this toast"),
    };

    // White text reads on all six; the four good-news ones also carry confetti, which Context and
    // ExtraUsage deliberately do not — see OnLoaded.
    private static LinearGradientBrush Gradient(ToastTheme theme)
    {
        (string a, string b, string c) = Palette(theme);
        static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
        return new LinearGradientBrush(
            new GradientStopCollection { new(C(a), 0), new(C(b), 0.5), new(C(c), 1) },
            new Point(0, 0), new Point(1, 1));
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        PositionBottomRight();
        PlayEntrance();
        FillTheBar();
        // Every toast so far has been good news (quota back). The context nudge is the first that
        // isn't, so it gets the same card and animation without the celebration — and the extra-usage
        // one is a receipt, which is the least celebratory thing this app has to say.
        if (_theme is not (ToastTheme.Context or ToastTheme.ExtraUsage)) LaunchConfetti();

        // Auto-dismiss after a comfortable read; the user can also close or act before then.
        var life = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        life.Tick += (_, _) => { life.Stop(); Dismiss(); };
        life.Start();
    }

    // Park the window above the taskbar, against the right edge of the work area.
    private void PositionBottomRight()
    {
        Rect wa = SystemParameters.WorkArea;
        Left = wa.Right - Width - 12;
        Top = wa.Bottom - Height - 12;
    }

    // Slide up + fade in.
    private void PlayEntrance()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Root.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
        SlideT.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(44, 0, TimeSpan.FromMilliseconds(420)) { EasingFunction = ease });
    }

    // The headline metaphor: the quota-left bar grows to its new level, after a beat.
    private void FillTheBar()
    {
        var fill = new DoubleAnimation(_targetScale, TimeSpan.FromMilliseconds(900))
        {
            BeginTime = TimeSpan.FromMilliseconds(550),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
        };
        FillScale.BeginAnimation(ScaleTransform.ScaleXProperty, fill);
    }

    // A short burst of confetti falling through the card.
    private void LaunchConfetti()
    {
        double w = Confetti.ActualWidth > 0 ? Confetti.ActualWidth : Width - 28;
        double h = Confetti.ActualHeight > 0 ? Confetti.ActualHeight : Height - 28;
        var rnd = new Random();

        for (int i = 0; i < 16; i++)
        {
            double size = 6 + rnd.NextDouble() * 6;
            bool round = rnd.Next(2) == 0;
            Shape piece = round
                ? new Ellipse { Width = size, Height = size }
                : new Rectangle { Width = size, Height = size * 0.6, RadiusX = 1, RadiusY = 1 };
            piece.Fill = new SolidColorBrush(ConfettiColors[rnd.Next(ConfettiColors.Length)]);

            double startX = rnd.NextDouble() * w;
            Canvas.SetLeft(piece, startX);
            Canvas.SetTop(piece, -size);

            var move = new TranslateTransform();
            var spin = new RotateTransform();
            piece.RenderTransformOrigin = new Point(0.5, 0.5);
            piece.RenderTransform = new TransformGroup { Children = { spin, move } };
            Confetti.Children.Add(piece);

            var dur = TimeSpan.FromMilliseconds(1100 + rnd.Next(1100));
            var begin = TimeSpan.FromMilliseconds(rnd.Next(450));
            var fall = new DoubleAnimation(0, h + size + 10, dur)
            {
                BeginTime = begin,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn },
            };
            var drift = new DoubleAnimation(0, (rnd.NextDouble() - 0.5) * 60, dur) { BeginTime = begin };
            var rotate = new DoubleAnimation(0, rnd.Next(180, 540), dur) { BeginTime = begin };
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(400))
            {
                BeginTime = begin + dur - TimeSpan.FromMilliseconds(400),
            };

            move.BeginAnimation(TranslateTransform.YProperty, fall);
            move.BeginAnimation(TranslateTransform.XProperty, drift);
            spin.BeginAnimation(RotateTransform.AngleProperty, rotate);
            piece.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>
    /// Render the card — gradient, confetti and filled bar, with its drop shadow and rounded
    /// corners as real alpha — to a transparent PNG at 2× for crisp documentation. Call once the
    /// entrance and bar-fill animations have settled.
    /// </summary>
    internal void SaveSnapshot(string path)
    {
        // Neutralize Root's own entrance transform/opacity so the snapshot is upright and opaque.
        Root.Opacity = 1;
        SlideT.Y = 0;
        Root.UpdateLayout();

        const double scale = 2.0;
        var rtb = new RenderTargetBitmap(
            (int)(Width * scale), (int)(Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(Root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using FileStream fs = OutFile.Create(path);
        encoder.Save(fs);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Dismiss();

    // Fade out, then close (guarded so the button and the auto-dismiss timer can't double-run it).
    private void Dismiss()
    {
        if (_closing) return;
        _closing = true;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260));
        fade.Completed += (_, _) => Close();
        Root.BeginAnimation(OpacityProperty, fade);
    }
}
