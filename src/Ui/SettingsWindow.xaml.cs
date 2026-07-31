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

    /// <param name="sampleProfiles">Preview-only (T121): render the profile picker, the Claude Code page
    /// and the System information page over the synthetic <see cref="AccountFixture"/> accounts instead
    /// of this machine's, so the page can be screenshotted for the README and the site.</param>
    /// <param name="revealIdentity">Preview-only: open with the holder and the paths already revealed.
    /// Safe to publish only together with <paramref name="sampleProfiles"/>.</param>
    public SettingsWindow(Settings current, Action<Settings> onSave, string? initialPage = null,
                          List<ClaudeInfo>? sampleProfiles = null, bool revealIdentity = false)
    {
        _onSave = onSave;
        _sampleProfiles = sampleProfiles;
        _revealIdentity = revealIdentity;
        // Edit a copy so closing without saving leaves the caller's instance untouched. The copy is a
        // total one (Settings.Clone, a JSON round-trip) rather than a hand-written field list, because
        // ApplySettings writes the whole model back: a field missing from the list would start at its
        // default and be saved over the user's value — which is what sent the icon back to the default
        // profile after any visit to Settings, and what kept the context-growth toggle off. The profile
        // list is deep-copied by the same round-trip, so the editor mutating rows in place can't reach
        // the caller's.
        _settings = current.Clone();

        InitializeComponent();

        // Language picker: "Automatic (system)" plus each shipped language by its own name (endonym,
        // from L.PickerLanguages), with the preference code carried in each item's Tag. Selection falls
        // back to Automatic.
        LanguageCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = L.T("settings.lang.auto"), Tag = "auto" });
        foreach (var (code, name) in L.PickerLanguages)
            LanguageCombo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = name, Tag = code });
        LanguageCombo.SelectedIndex = 0; // Automatic, unless a saved preference matches below
        for (int i = 0; i < LanguageCombo.Items.Count; i++)
            if ((string)((System.Windows.Controls.ComboBoxItem)LanguageCombo.Items[i]).Tag == _settings.Language)
                LanguageCombo.SelectedIndex = i;

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
        FollowActiveCheck.IsChecked = _settings.FollowActiveProfile;
        EnvSyncCheck.IsChecked = _settings.SyncEnvironmentProfile;
        ShowPctCheck.IsChecked = _settings.ShowPercentage;
        ShowRemainingCheck.IsChecked = _settings.ShowRemaining;
        FlashCheck.IsChecked = _settings.FlashNearLimit;
        NotifyResetCheck.IsChecked = _settings.NotifyOnUnexpectedReset;
        NotifyWeeklyCheck.IsChecked = _settings.NotifyOnScheduledReset;
        NotifySessionCheck.IsChecked = _settings.NotifyOnSessionReset;
        NotifyContextCheck.IsChecked = _settings.NotifyOnContextGrowth;

        WeeklyMinSlider.Minimum = Settings.MinResetNotifyPercent;
        WeeklyMinSlider.Maximum = Settings.MaxResetNotifyPercent;
        WeeklyMinSlider.Value = Math.Clamp(_settings.ScheduledResetMinPercent,
            Settings.MinResetNotifyPercent, Settings.MaxResetNotifyPercent);
        SessionMinSlider.Minimum = Settings.MinResetNotifyPercent;
        SessionMinSlider.Maximum = Settings.MaxResetNotifyPercent;
        SessionMinSlider.Value = Math.Clamp(_settings.SessionResetMinPercent,
            Settings.MinResetNotifyPercent, Settings.MaxResetNotifyPercent);
        ContextThresholdSlider.Minimum = Settings.MinContextNudgeTokens;
        ContextThresholdSlider.Maximum = Settings.MaxContextNudgeTokens;
        ContextThresholdSlider.Value = Math.Clamp(_settings.ContextNudgeTokens,
            Settings.MinContextNudgeTokens, Settings.MaxContextNudgeTokens);
        UpdateWeeklyMinLabel();
        UpdateSessionMinLabel();
        UpdateContextThresholdLabel();
        // "Start with Windows" is a registry entry (HKCU\…\Run), not part of the Settings model;
        // read its live state here and apply it directly on Save.
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        VersionText.Text = L.T("settings.version", Updater.CurrentVersion);

        // About page: the same crisp logo the tray draws, plus the live version chip.
        HeroVersion.Text = L.T("settings.heroVersion", Updater.CurrentVersion);
        LogoImage.Source = RenderLogoSource(192);

        try { Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
            new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath)); }
        catch { /* fall back to the default window icon */ }

        LoadProfiles();
        LoadSystemInfo();

        SelectPage(initialPage switch
        {
            not null when string.Equals(initialPage, "About", StringComparison.OrdinalIgnoreCase) => "About",
            not null when string.Equals(initialPage, "System", StringComparison.OrdinalIgnoreCase) => "System",
            not null when string.Equals(initialPage, "Notifications", StringComparison.OrdinalIgnoreCase) => "Notifications",
            not null when string.Equals(initialPage, "Display", StringComparison.OrdinalIgnoreCase) => "Display",
            not null when string.Equals(initialPage, "ClaudeCode", StringComparison.OrdinalIgnoreCase) => "ClaudeCode",
            _ => "General",
        });
    }

    // Switch the visible page (General / System / About …) and move the sidebar selection highlight.
    private void Nav_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SelectPage((string)((FrameworkElement)sender).Tag);

    private void SelectPage(string page)
    {
        bool general = page == "General";
        bool display = page == "Display";
        bool claudeCode = page == "ClaudeCode";
        bool notifications = page == "Notifications";
        bool system = page == "System";
        bool about = page == "About";

        GeneralPane.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        DisplayPane.Visibility = display ? Visibility.Visible : Visibility.Collapsed;
        ClaudeCodePane.Visibility = claudeCode ? Visibility.Visible : Visibility.Collapsed;
        NotificationsPane.Visibility = notifications ? Visibility.Visible : Visibility.Collapsed;
        SystemPane.Visibility = system ? Visibility.Visible : Visibility.Collapsed;
        AboutPane.Visibility = about ? Visibility.Visible : Visibility.Collapsed;

        var selected = (System.Windows.Media.Brush)FindResource("SubtleFillColorSecondaryBrush");
        var clear = System.Windows.Media.Brushes.Transparent;
        NavGeneral.Background = general ? selected : clear;
        NavDisplay.Background = display ? selected : clear;
        NavClaudeCode.Background = claudeCode ? selected : clear;
        NavNotifications.Background = notifications ? selected : clear;
        NavSystem.Background = system ? selected : clear;
        NavAbout.Background = about ? selected : clear;
        AccentGeneral.Visibility = general ? Visibility.Visible : Visibility.Collapsed;
        AccentDisplay.Visibility = display ? Visibility.Visible : Visibility.Collapsed;
        AccentClaudeCode.Visibility = claudeCode ? Visibility.Visible : Visibility.Collapsed;
        AccentNotifications.Visibility = notifications ? Visibility.Visible : Visibility.Collapsed;
        AccentSystem.Visibility = system ? Visibility.Visible : Visibility.Collapsed;
        AccentAbout.Visibility = about ? Visibility.Visible : Visibility.Collapsed;

        // About and System information are read-only pages: hide Save and turn Cancel into a plain Close.
        bool readOnly = about || system;
        SaveButton.Visibility = readOnly ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = readOnly ? L.T("settings.close") : L.T("settings.cancel");
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
            System.Windows.MessageBox.Show(L.T("dialog.openLinkFailed", url, ex.Message),
                L.T("dialog.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.RefreshSeconds = (int)Math.Round(IntervalSlider.Value * 60.0);
        _settings.ShowPercentage = ShowPctCheck.IsChecked == true;
        _settings.ShowRemaining = ShowRemainingCheck.IsChecked == true;
        _settings.FlashNearLimit = FlashCheck.IsChecked == true;
        _settings.NotifyOnUnexpectedReset = NotifyResetCheck.IsChecked == true;
        _settings.NotifyOnScheduledReset = NotifyWeeklyCheck.IsChecked == true;
        _settings.NotifyOnSessionReset = NotifySessionCheck.IsChecked == true;
        _settings.NotifyOnContextGrowth = NotifyContextCheck.IsChecked == true;
        _settings.ContextNudgeTokens = (int)Math.Round(ContextThresholdSlider.Value);
        _settings.ScheduledResetMinPercent = (int)Math.Round(WeeklyMinSlider.Value);
        _settings.SessionResetMinPercent = (int)Math.Round(SessionMinSlider.Value);
        _settings.ClaudeCodeDirectory = DirectoryBox.Text.Trim();
        _settings.AutoOpenOnUnauthenticated = AutoOpenCheck.IsChecked == true;
        _settings.FollowActiveProfile = FollowActiveCheck.IsChecked == true;
        _settings.SyncEnvironmentProfile = EnvSyncCheck.IsChecked == true;
        _settings.AuthRetrySeconds = (int)Math.Round(RetrySlider.Value);
        _settings.Language = (LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string
                             ?? Settings.DefaultLanguage;

        // Apply the autostart registry entry directly (it lives outside the Settings model).
        bool startup = StartupCheck.IsChecked == true;
        try
        {
            if (StartupManager.IsEnabled() != startup)
                StartupManager.SetEnabled(startup);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(L.T("dialog.startupFailed", ex.Message),
                L.T("dialog.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _onSave(_settings);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Render the window's content to a PNG at 1.5×, off-screen, without depending on it being visible
    /// or foreground — the same deterministic path <c>StatisticsWindow.SaveSnapshot</c> takes, and for
    /// the same reason: the screen-copy capture script grabs whatever pixels are *on screen* in the
    /// window's rectangle, so any app that steals focus or sits on top ends up in the file. Behind
    /// <c>--capture-settings</c>.
    /// </summary>
    /// <param name="scrollBy">Device-independent pixels to scroll the visible page's ScrollViewer down
    /// first, so a section below the fold can be captured without resizing the window.</param>
    internal void SaveSnapshot(string path, double scrollBy = 0)
    {
        UpdateLayout();
        if (scrollBy > 0 && VisiblePageScroller() is { } sv)
        {
            sv.ScrollToVerticalOffset(scrollBy);
            UpdateLayout();
        }
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

        var target = (FrameworkElement)Content;
        // The window's Mica backdrop isn't part of the visual tree, so paint an opaque themed surface
        // behind the content for the snapshot, then put it back.
        System.Windows.Media.Brush? prev = (target as System.Windows.Controls.Panel)?.Background;
        if (target is System.Windows.Controls.Panel panel)
        {
            panel.Background = TryFindResource("SolidBackgroundFillColorBaseBrush") as System.Windows.Media.Brush
                               ?? new System.Windows.Media.SolidColorBrush(
                                   System.Windows.Media.Color.FromRgb(0x20, 0x20, 0x20));
            panel.UpdateLayout();
        }

        const double scale = 1.5;
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(target.ActualWidth * scale), (int)(target.ActualHeight * scale),
            96 * scale, 96 * scale, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(target);

        if (target is System.Windows.Controls.Panel p2) p2.Background = prev;

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
        using System.IO.FileStream fs = System.IO.File.Create(path);
        encoder.Save(fs);
    }

    /// <summary>The ScrollViewer of whichever page is currently shown, so a capture can scroll it.</summary>
    private System.Windows.Controls.ScrollViewer? VisiblePageScroller()
    {
        foreach (System.Windows.Controls.Grid pane in
                 new[] { GeneralPane, DisplayPane, ClaudeCodePane, NotificationsPane, SystemPane, AboutPane })
            if (pane.Visibility == Visibility.Visible)
                return pane.Children.OfType<System.Windows.Controls.ScrollViewer>().FirstOrDefault();
        return null;
    }

}
