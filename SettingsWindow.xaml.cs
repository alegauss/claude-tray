using System.Windows;

namespace ClaudeTray;

/// <summary>
/// The settings window, built with WPF + the built-in .NET Fluent theme (<c>ThemeMode="System"</c>),
/// so it follows the Windows light/dark setting and gets the Windows 11 look (Mica, rounded corners,
/// Fluent controls) with no extra dependencies. The layout lives entirely in
/// <c>SettingsWindow.xaml</c> as a declarative grid — there is no imperative z-order stacking, which
/// is what made the old WinForms sidebar fragile.
///
/// Shown non-modally from the tray; on Save it applies the edited <see cref="Settings"/> through the
/// <c>onSave</c> callback supplied at construction. The interval is edited in minutes (the model
/// stores seconds).
/// </summary>
internal partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Action<Settings> _onSave;

    private static double MinMinutes => Settings.MinRefreshSeconds / 60.0;
    private static double MaxMinutes => Settings.MaxRefreshSeconds / 60.0;

    public SettingsWindow(Settings current, Action<Settings> onSave, string? initialPage = null)
    {
        _onSave = onSave;
        // Edit a copy so closing without saving leaves the caller's instance untouched.
        _settings = new Settings
        {
            RefreshSeconds = current.RefreshSeconds,
            ShowPercentage = current.ShowPercentage,
            ShowRemaining = current.ShowRemaining,
            NotifyOnUnexpectedReset = current.NotifyOnUnexpectedReset,
            NotifyOnScheduledReset = current.NotifyOnScheduledReset,
            NotifyOnSessionReset = current.NotifyOnSessionReset,
            SessionResetMinPercent = current.SessionResetMinPercent,
            ScheduledResetMinPercent = current.ScheduledResetMinPercent,
            ClaudeCodeDirectory = current.ClaudeCodeDirectory,
            AutoOpenOnUnauthenticated = current.AutoOpenOnUnauthenticated,
            AuthRetrySeconds = current.AuthRetrySeconds,
        };

        InitializeComponent();

        IntervalSlider.Minimum = MinMinutes;
        IntervalSlider.Maximum = MaxMinutes;
        IntervalSlider.Value = Math.Clamp(_settings.RefreshSeconds / 60.0, MinMinutes, MaxMinutes);
        UpdateIntervalLabel();

        RetrySlider.Minimum = Settings.MinAuthRetrySeconds;
        RetrySlider.Maximum = Settings.MaxAuthRetrySeconds;
        RetrySlider.Value = Math.Clamp(_settings.AuthRetrySeconds,
            Settings.MinAuthRetrySeconds, Settings.MaxAuthRetrySeconds);
        UpdateRetryLabel();

        DirectoryBox.Text = string.IsNullOrWhiteSpace(_settings.ClaudeCodeDirectory)
            ? Settings.DefaultDirectory
            : _settings.ClaudeCodeDirectory;
        AutoOpenCheck.IsChecked = _settings.AutoOpenOnUnauthenticated;
        ShowPctCheck.IsChecked = _settings.ShowPercentage;
        ShowRemainingCheck.IsChecked = _settings.ShowRemaining;
        NotifyResetCheck.IsChecked = _settings.NotifyOnUnexpectedReset;
        NotifyWeeklyCheck.IsChecked = _settings.NotifyOnScheduledReset;
        NotifySessionCheck.IsChecked = _settings.NotifyOnSessionReset;

        WeeklyMinSlider.Minimum = Settings.MinResetNotifyPercent;
        WeeklyMinSlider.Maximum = Settings.MaxResetNotifyPercent;
        WeeklyMinSlider.Value = Math.Clamp(_settings.ScheduledResetMinPercent,
            Settings.MinResetNotifyPercent, Settings.MaxResetNotifyPercent);
        SessionMinSlider.Minimum = Settings.MinResetNotifyPercent;
        SessionMinSlider.Maximum = Settings.MaxResetNotifyPercent;
        SessionMinSlider.Value = Math.Clamp(_settings.SessionResetMinPercent,
            Settings.MinResetNotifyPercent, Settings.MaxResetNotifyPercent);
        UpdateWeeklyMinLabel();
        UpdateSessionMinLabel();
        // "Start with Windows" is a registry entry (HKCU\…\Run), not part of the Settings model;
        // read its live state here and apply it directly on Save.
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        VersionText.Text = $"Version {Updater.CurrentVersion}";

        // About page: the same crisp logo the tray draws, plus the live version chip.
        HeroVersion.Text = $"v{Updater.CurrentVersion}";
        LogoImage.Source = RenderLogoSource(192);

        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath)); }
        catch { /* fall back to the default window icon */ }

        SelectPage(initialPage switch
        {
            not null when string.Equals(initialPage, "About", StringComparison.OrdinalIgnoreCase) => "About",
            not null when string.Equals(initialPage, "Notifications", StringComparison.OrdinalIgnoreCase) => "Notifications",
            _ => "General",
        });
    }

    // Switch the visible page (General / About) and move the sidebar selection highlight.
    private void Nav_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SelectPage((string)((FrameworkElement)sender).Tag);

    private void SelectPage(string page)
    {
        bool general = page == "General";
        bool notifications = page == "Notifications";
        bool about = page == "About";

        GeneralPane.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        NotificationsPane.Visibility = notifications ? Visibility.Visible : Visibility.Collapsed;
        AboutPane.Visibility = about ? Visibility.Visible : Visibility.Collapsed;

        var selected = (System.Windows.Media.Brush)FindResource("SubtleFillColorSecondaryBrush");
        var clear = System.Windows.Media.Brushes.Transparent;
        NavGeneral.Background = general ? selected : clear;
        NavNotifications.Background = notifications ? selected : clear;
        NavAbout.Background = about ? selected : clear;
        AccentGeneral.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        AccentNotifications.Visibility = notifications ? Visibility.Visible : Visibility.Collapsed;
        AccentAbout.Visibility = about ? Visibility.Visible : Visibility.Collapsed;

        // The About page has nothing to save: hide Save and turn Cancel into a plain Close.
        SaveButton.Visibility = about ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = about ? "Close" : "Cancel";
    }

    // Open a card's Tag URL in the default browser.
    private void Link_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => OpenUrl(((FrameworkElement)sender).Tag as string);

    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open the link:\n{url}\n\n{ex.Message}",
                "Claude Code Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Render the GDI+ app logo and hand it to WPF as a frozen PNG-backed image (no GDI handle to leak).
    private static System.Windows.Media.ImageSource RenderLogoSource(int size)
    {
        using System.Drawing.Bitmap bmp = IconRenderer.RenderLogo(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
        ms.Position = 0;
        var img = new System.Windows.Media.Imaging.BitmapImage();
        img.BeginInit();
        img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        img.StreamSource = ms;
        img.EndInit();
        img.Freeze();
        return img;
    }

    // Approximate tokens billed per heartbeat: the request carries ~10 input tokens
    // ("hi" + message framing) and max_tokens=1 caps the reply at 1 output token.
    private const double InputTokensPerCall = 10;
    private const double OutputTokensPerCall = 1;
    // Haiku 4.5 price per 1M tokens (input, output) — matches the table in UsageInsights.
    private const double HaikuInputPerM = 1.0;
    private const double HaikuOutputPerM = 5.0;

    private void IntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateIntervalLabel();

    private void RetrySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateRetryLabel();

    private void UpdateRetryLabel()
    {
        // RetryValue can be null while the slider's initial value is set during InitializeComponent.
        if (RetryValue is null) return;
        int s = (int)Math.Round(RetrySlider.Value);
        RetryValue.Text = s == 1 ? "1 second" : $"{s} seconds";
    }

    private void WeeklyMinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateWeeklyMinLabel();

    private void SessionMinSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateSessionMinLabel();

    // The threshold labels read as a sentence with the leading "Only notify above" caption:
    // "above 2%". The value labels can be null while the slider value is set in InitializeComponent.
    private void UpdateWeeklyMinLabel()
    {
        if (WeeklyMinValue is null) return;
        WeeklyMinValue.Text = $"{(int)Math.Round(WeeklyMinSlider.Value)}%";
    }

    private void UpdateSessionMinLabel()
    {
        if (SessionMinValue is null) return;
        SessionMinValue.Text = $"{(int)Math.Round(SessionMinSlider.Value)}%";
    }

    // Pick the working directory Claude Code opens in. WinForms' folder dialog is already available
    // (this is a WinForms+WPF hybrid) and gives the familiar Windows folder picker.
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose the directory Claude Code opens in",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        string current = DirectoryBox.Text.Trim();
        if (current.Length > 0 && System.IO.Directory.Exists(current))
            dlg.SelectedPath = current;
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DirectoryBox.Text = dlg.SelectedPath;
    }

    private void UpdateIntervalLabel()
    {
        // IntervalValue can be null while the slider's initial value is set during InitializeComponent.
        if (IntervalValue is null) return;
        double m = IntervalSlider.Value;
        IntervalValue.Text = m == 1.0 ? "1 minute" : $"{m:0.#} minutes";
        UpdateCostEstimate(m);
    }

    // Show, live for the chosen cadence, how much the polling "heartbeat" consumes. It uses Claude
    // Code's subscription login, so there is no separate bill — it just draws a sliver of your usage;
    // the $ figure is only the hypothetical pay-as-you-go API equivalent, for a sense of scale.
    private void UpdateCostEstimate(double minutes)
    {
        if (CostEstimate is null) return;

        double callsPerHour = 60.0 / minutes;
        double tokensPerCall = InputTokensPerCall + OutputTokensPerCall;
        double tokensPerHour = callsPerHour * tokensPerCall;
        double tokensPerDay = tokensPerHour * 24.0;
        double costPerCall = (InputTokensPerCall * HaikuInputPerM + OutputTokensPerCall * HaikuOutputPerM) / 1_000_000.0;
        double costPerMonth = costPerCall * callsPerHour * 24.0 * 30.0;

        // Format the numbers with the invariant culture so they read consistently with the
        // English UI (period decimal, comma thousands) regardless of the OS locale.
        string stats = System.FormattableString.Invariant(
            $"At this interval: ≈ {callsPerHour:0.#} calls/h · ~{tokensPerHour:0} tokens/h (~{tokensPerDay:#,0}/day · ≈ ${costPerMonth:0.00}/mo if billed as pay-as-you-go API).");

        CostEstimate.Text =
            "Each refresh is one 1-token Haiku call (“heartbeat”) sent with your Claude Code login — " +
            "it uses a sliver of your usage, not a separate bill.\n" + stats;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.RefreshSeconds = (int)Math.Round(IntervalSlider.Value * 60.0);
        _settings.ShowPercentage = ShowPctCheck.IsChecked == true;
        _settings.ShowRemaining = ShowRemainingCheck.IsChecked == true;
        _settings.NotifyOnUnexpectedReset = NotifyResetCheck.IsChecked == true;
        _settings.NotifyOnScheduledReset = NotifyWeeklyCheck.IsChecked == true;
        _settings.NotifyOnSessionReset = NotifySessionCheck.IsChecked == true;
        _settings.ScheduledResetMinPercent = (int)Math.Round(WeeklyMinSlider.Value);
        _settings.SessionResetMinPercent = (int)Math.Round(SessionMinSlider.Value);
        _settings.ClaudeCodeDirectory = DirectoryBox.Text.Trim();
        _settings.AutoOpenOnUnauthenticated = AutoOpenCheck.IsChecked == true;
        _settings.AuthRetrySeconds = (int)Math.Round(RetrySlider.Value);

        // Apply the autostart registry entry directly (it lives outside the Settings model).
        bool startup = StartupCheck.IsChecked == true;
        try
        {
            if (StartupManager.IsEnabled() != startup)
                StartupManager.SetEnabled(startup);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not change the startup setting:\n{ex.Message}",
                "Claude Code Tray", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _onSave(_settings);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
