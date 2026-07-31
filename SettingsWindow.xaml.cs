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
            FlashNearLimit = current.FlashNearLimit,
            NotifyOnUnexpectedReset = current.NotifyOnUnexpectedReset,
            NotifyOnScheduledReset = current.NotifyOnScheduledReset,
            NotifyOnSessionReset = current.NotifyOnSessionReset,
            SessionResetMinPercent = current.SessionResetMinPercent,
            ScheduledResetMinPercent = current.ScheduledResetMinPercent,
            ClaudeCodeDirectory = current.ClaudeCodeDirectory,
            AutoOpenOnUnauthenticated = current.AutoOpenOnUnauthenticated,
            AuthRetrySeconds = current.AuthRetrySeconds,
            Language = current.Language,
            // Carried through even though no control on the page edits it: ApplySettings copies the
            // whole model back, so a field missing here is a field silently reset on Save — which is
            // what sent the icon back to the default profile after any visit to Settings.
            MonitoredConfigDir = current.MonitoredConfigDir,
            FollowActiveProfile = current.FollowActiveProfile,
            // Deep-copied like every other field: the profile editor mutates these in place, and
            // cancelling must leave the caller's list untouched.
            Profiles = current.Profiles.Select(p => new ClaudeProfile
            {
                Label = p.Label,
                ConfigDir = p.ConfigDir,
                WorkDir = p.WorkDir,
            }).ToList(),
        };

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
        RetryValue.Text = s == 1 ? L.T("settings.sec.one") : L.T("settings.sec.many", s);
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

    private void ContextThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        => UpdateContextThresholdLabel();

    // Shown the same way the window shows every other token count, so the threshold reads in the same
    // units as the gauge it is about.
    private void UpdateContextThresholdLabel()
    {
        if (ContextThresholdValue is null) return;
        ContextThresholdValue.Text = TokenEstimate.Format((int)Math.Round(ContextThresholdSlider.Value));
    }

    // Pick the working directory Claude Code opens in. WinForms' folder dialog is already available
    // (this is a WinForms+WPF hybrid) and gives the familiar Windows folder picker.
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = L.T("settings.cc.browseTitle"),
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
        // Keep the number invariant (period decimal) to match the rest of the app; translate only the unit.
        IntervalValue.Text = m == 1.0
            ? L.T("settings.min.one")
            : L.T("settings.min.many", m.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture));
        UpdateCostEstimate(m);
    }

    // Show, live for the chosen cadence, how much the polling "heartbeat" consumes. It uses Claude
    // Code's subscription login, so there is no separate bill — it just draws a sliver of your usage;
    // the $ figure is only the hypothetical pay-as-you-go API equivalent, for a sense of scale.
    private void UpdateCostEstimate(double minutes)
    {
        if (CostEstimate is null) return;

        // One heartbeat per *polled* profile per interval (T127), so the figures multiply — and say so.
        // Only profiles on the subscription are polled: an API key has no quota window to read, and
        // polling it would spend money to learn nothing.
        int polled = Math.Max(1, DiscoverProfiles().Count(p => p.CountsAgainstSubscription));

        double callsPerHour = 60.0 / minutes * polled;
        double tokensPerCall = InputTokensPerCall + OutputTokensPerCall;
        double tokensPerHour = callsPerHour * tokensPerCall;
        double tokensPerDay = tokensPerHour * 24.0;
        double costPerCall = (InputTokensPerCall * HaikuInputPerM + OutputTokensPerCall * HaikuOutputPerM) / 1_000_000.0;
        double costPerMonth = costPerCall * callsPerHour * 24.0 * 30.0;

        // Format the numbers with the invariant culture so they read consistently (period decimal,
        // comma thousands) regardless of the OS locale; the surrounding words are localized.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string stats = L.T("settings.cost.stats",
            callsPerHour.ToString("0.#", inv),
            tokensPerHour.ToString("0", inv),
            tokensPerDay.ToString("#,0", inv),
            costPerMonth.ToString("0.00", inv));

        CostEstimate.Text = L.T("settings.cost.lead") + stats
                            + (polled > 1 ? "\n" + L.T("settings.cost.profiles", polled) : "");
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

    /// <summary>Preview-only: pick a profile by index in the System page's picker, so a capture can show
    /// a profile other than the first.</summary>
    internal void SelectProfileForPreview(int index)
    {
        if (index >= 0 && index < SysProfileCombo.Items.Count) SysProfileCombo.SelectedIndex = index;
    }

    // ================= Profiles (Claude Code page) =================
    // A profile is a config dir. The editor here changes only the tray's *own* record of which dirs to
    // offer, what to call them and where each opens — it never writes into a config dir, and Remove
    // never deletes a folder (it holds transcripts, memory and settings).

    /// <summary>Discovered + registered profiles, as the editor's combo shows them.</summary>
    private List<ClaudeInfo> _ccProfiles = new();

    /// <summary>Set while the boxes are being filled, so a programmatic change isn't read back as
    /// the user typing.</summary>
    private bool _fillingProfile;

    /// <summary>One discovery sweep per window (or per profile-list edit), shared by the editor here and
    /// the System information picker — both used to run their own, which doubled the disk work of
    /// opening Settings for no benefit.</summary>
    private List<ClaudeInfo> DiscoverProfiles() => _discovered ??= ClaudeAccount.Discover(_settings.Profiles);
    private List<ClaudeInfo>? _discovered;
    private void InvalidateProfiles() => _discovered = null;

    private void LoadProfiles(string? selectDir = null)
    {
        _ccProfiles = DiscoverProfiles();

        _fillingProfile = true;
        ProfileCombo.Items.Clear();
        foreach (ClaudeInfo p in _ccProfiles)
            ProfileCombo.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = p.IsDefault ? L.T("settings.sys.profileDefault", p.Label) : p.Label,
            });
        int index = 0;
        if (selectDir is { Length: > 0 })
            for (int i = 0; i < _ccProfiles.Count; i++)
                if (string.Equals(_ccProfiles[i].ConfigDir, selectDir, StringComparison.OrdinalIgnoreCase))
                    index = i;
        if (_ccProfiles.Count > 0) ProfileCombo.SelectedIndex = index;
        _fillingProfile = false;

        // With one profile there is nowhere for the icon to follow to, so the toggle would be a switch
        // that does nothing. It reappears the moment a second profile is added (T126).
        FollowActiveRow.Visibility = _ccProfiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        FillProfileFields();
    }

    private ClaudeInfo? SelectedProfile =>
        ProfileCombo.SelectedIndex >= 0 && ProfileCombo.SelectedIndex < _ccProfiles.Count
            ? _ccProfiles[ProfileCombo.SelectedIndex]
            : null;

    private void FillProfileFields()
    {
        _fillingProfile = true;
        try
        {
            ClaudeInfo? p = SelectedProfile;
            bool any = p is not null;
            ProfileNameRow.IsEnabled = ProfileWorkDirRow.IsEnabled = any;
            // Only a registered entry can be dropped from the list; a discovered one has nothing to drop.
            ProfileRemoveButton.IsEnabled = p is { IsRegistered: true };

            ProfileNameBox.Text = p?.Label ?? "";
            ProfileWorkDirBox.Text = p is { WorkDir.Length: > 0 } ? p.WorkDir : "";
            ProfileConfigDirText.Text = p?.ConfigDir ?? Dash;
            ProfileStatusText.Text = p is null ? "" : Join(
                p.Plan ?? L.T("settings.cc.profileNoLogin"),
                L.T(p.IsRegistered ? "settings.cc.profileRegistered" : "settings.cc.profileDiscovered"));
            ProfileStatusText.Visibility = Shown(ProfileStatusText.Text);
        }
        finally { _fillingProfile = false; }
    }

    private void Profile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_fillingProfile) return;
        FillProfileFields();
    }

    /// <summary>The registered entry for a profile, created on first edit — typing a name into a
    /// discovered profile is what registers it, since a name only persists if the tray remembers it.</summary>
    private ClaudeProfile Register(ClaudeInfo info)
    {
        ClaudeProfile? existing = _settings.Profiles.FirstOrDefault(
            p => string.Equals(p.ConfigDir, info.ConfigDir, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;

        var added = new ClaudeProfile { ConfigDir = info.ConfigDir };
        _settings.Profiles.Add(added);
        info.IsRegistered = true;
        ProfileRemoveButton.IsEnabled = true;
        ProfileStatusText.Text = Join(info.Plan ?? L.T("settings.cc.profileNoLogin"),
                                      L.T("settings.cc.profileRegistered"));
        return added;
    }

    private void ProfileName_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_fillingProfile || SelectedProfile is not { } p) return;
        Register(p).Label = ProfileNameBox.Text.Trim();
        p.Label = ProfileNameBox.Text.Trim();
    }

    private void ProfileWorkDir_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_fillingProfile || SelectedProfile is not { } p) return;
        Register(p).WorkDir = ProfileWorkDirBox.Text.Trim();
        p.WorkDir = ProfileWorkDirBox.Text.Trim();
    }

    private void ProfileWorkDirBrowse_Click(object sender, RoutedEventArgs e)
    {
        if (PickFolder(L.T("settings.cc.profileWorkDirTitle"), ProfileWorkDirBox.Text) is { } dir)
            ProfileWorkDirBox.Text = dir;
    }

    // Add a profile: pick (or create) a folder. Nothing is written into it — Claude Code creates its
    // own files there on first launch, and the login happens in Claude Code, not here.
    private void ProfileAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Profiles.Count >= Settings.MaxProfiles)
        {
            System.Windows.MessageBox.Show(L.T("settings.cc.profileMax", Settings.MaxProfiles),
                L.T("dialog.appName"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // Suggest the convention, so a profile added here is also found by discovery on another machine.
        string suggested = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ClaudeAccount.ConventionPrefix.TrimEnd('-'));
        if (PickFolder(L.T("settings.cc.profileAddTitle"), suggested, newFolder: true) is not { } dir) return;

        if (_settings.Profiles.Any(p => string.Equals(p.ConfigDir, dir, StringComparison.OrdinalIgnoreCase)))
        {
            LoadProfiles(dir);   // already registered — just select it
            return;
        }
        _settings.Profiles.Add(new ClaudeProfile { ConfigDir = dir });
        InvalidateProfiles();
        LoadProfiles(dir);
        LoadSystemInfo();        // the System page's picker reads the same list
    }

    // Remove from the tray's list only. The folder, its login and its transcripts stay exactly where
    // they are — and if discovery can still see it, it reappears as a discovered (unnamed) profile.
    private void ProfileRemove_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { IsRegistered: true } p) return;
        if (System.Windows.MessageBox.Show(L.T("settings.cc.profileRemoveConfirm", p.Label, p.ConfigDir),
                L.T("dialog.appName"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        _settings.Profiles.RemoveAll(
            r => string.Equals(r.ConfigDir, p.ConfigDir, StringComparison.OrdinalIgnoreCase));
        InvalidateProfiles();
        LoadProfiles();
        LoadSystemInfo();
    }

    // The familiar Windows folder picker, shared by both directory fields on this page.
    private static string? PickFolder(string title, string? current, bool newFolder = false)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = newFolder,
        };
        if (current is { Length: > 0 } && System.IO.Directory.Exists(current)) dlg.SelectedPath = current;
        return dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dlg.SelectedPath : null;
    }

    // ================= System information page =================
    // Read-only: one pass over what Claude Code already keeps on disk (ClaudeAccount) plus this
    // process's own environment. No API call, nothing written, and no transcript touched.

    /// <summary>Shown wherever a reading isn't available — a fresh install, an API-key setup, or a
    /// field this version of Claude Code doesn't write.</summary>
    private const string Dash = "—";

    /// <summary>Every Claude Code config dir found on this machine, default first (T122). Never empty:
    /// a machine with nothing to find still gets the default dir's (mostly null) reading, which is what
    /// renders the "no account" card.</summary>
    private List<ClaudeInfo> _profiles = new();

    /// <summary>Index into <see cref="_profiles"/> of the profile whose rows are on screen.</summary>
    private int _profile;

    /// <summary>Whether the holder's real name/email and the absolute paths are on screen. Off by
    /// default: this page is the one that ends up in a screenshot attached to a bug report.</summary>
    private bool _revealIdentity;

    private void LoadSystemInfo()
    {
        _profiles = DiscoverProfiles();
        if (_profiles.Count == 0) _profiles.Add(ClaudeAccount.Read());

        // The picker only earns its space when there is a choice to make.
        bool several = _profiles.Count > 1;
        SysProfileCard.Visibility = several ? Visibility.Visible : Visibility.Collapsed;
        SysProfileCombo.Items.Clear();   // this runs again whenever the profile list is edited
        if (several)
        {
            SysProfileCount.Text = L.T("settings.sys.profileCount", _profiles.Count);
            foreach (ClaudeInfo p in _profiles)
                SysProfileCombo.Items.Add(new System.Windows.Controls.ComboBoxItem
                {
                    Content = p.IsDefault ? L.T("settings.sys.profileDefault", p.Label) : p.Label,
                });
            SysProfileCombo.SelectedIndex = 0;   // fires SysProfile_Changed → renders
            return;
        }
        RenderSystemInfo();
    }

    private void SysProfile_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Can fire while the items are being added, before there is anything to render.
        if (_profiles.Count == 0 || SysProfileCombo.SelectedIndex < 0) return;
        _profile = Math.Clamp(SysProfileCombo.SelectedIndex, 0, _profiles.Count - 1);
        RenderSystemInfo();
    }

    private void RenderSystemInfo()
    {
        ClaudeInfo c = _profiles[Math.Clamp(_profile, 0, _profiles.Count - 1)];
        var culture = L.Culture;

        // ---- Claude account ----
        bool hasAccount = c.HasAccount;
        SysAccountCard.Visibility = hasAccount ? Visibility.Visible : Visibility.Collapsed;
        SysNoAccountCard.Visibility = hasAccount ? Visibility.Collapsed : Visibility.Visible;

        SysPlan.Text = c.Plan ?? Dash;
        // The raw tier, the seat and how it's billed — the qualifiers that make a plan name precise
        // (and recognizable in a bug report) without turning each into its own row.
        SysPlanTier.Text = Join(
            c.PlanTier,
            c.SeatTier is { } seat ? ClaudeAccount.Pretty(seat) : null,
            c.BillingType is { } bill ? ClaudeAccount.Pretty(bill) : null);
        SysPlanTier.Visibility = Shown(SysPlanTier.Text);

        SysHolder.Text = c.HolderName is { } name
            ? (_revealIdentity ? name : ClaudeAccount.MaskName(name))
            : Dash;
        SysHolderMail.Text = c.HolderEmail is { } mail
            ? (_revealIdentity ? mail : ClaudeAccount.MaskEmail(mail))
            : "";
        SysHolderMail.Visibility = Shown(SysHolderMail.Text);
        SysRevealButton.Content = L.T(_revealIdentity ? "settings.sys.hide" : "settings.sys.reveal");
        SysRevealButton.IsEnabled = c.HolderName != null || c.HolderEmail != null;

        // A personal Pro/Max account has no organization at all — hide the row instead of dashing it.
        bool hasOrg = c.OrgName != null || c.OrgType != null;
        SysOrgRow.Visibility = SysOrgDivider.Visibility = hasOrg ? Visibility.Visible : Visibility.Collapsed;
        SysOrg.Text = c.OrgName ?? Dash;
        SysOrgSub.Text = Join(
            c.OrgType is { } ot ? ClaudeAccount.Pretty(ot) : null,
            c.OrgRole is { } role ? ClaudeAccount.Pretty(role) : null);
        SysOrgSub.Visibility = Shown(SysOrgSub.Text);

        // Which credentials this profile's sessions actually use — and, when they are not the
        // subscription, the plain statement that the tray's percentage is not about them.
        SysAuth.Text = AuthName(c.Auth);
        SysAuthSource.Text = Join(
            c.AuthSource,
            c.AuthConfirmed ? L.T("settings.sys.authConfirmed") : L.T("settings.sys.authInferred"));
        SysAuthSource.Visibility = Shown(SysAuthSource.Text);
        SysAuthCheck.IsEnabled = true;

        bool offSubscription = c.Auth != AuthMethod.Subscription && c.Auth != AuthMethod.None;
        SysAuthWarnCard.Visibility = offSubscription ? Visibility.Visible : Visibility.Collapsed;
        if (offSubscription)
        {
            SysAuthWarnTitle.Text = L.T("settings.sys.authWarnTitle", AuthName(c.Auth));
            SysAuthWarnBody.Text = L.T("settings.sys.authWarnBody");
        }

        SysExtra.Text = c.ExtraUsage switch
        {
            true => L.T("settings.sys.enabled"),
            false => L.T("settings.sys.disabled"),
            null => Dash,
        };

        SysSince.Text = c.FirstToken is { } first ? first.ToString("d", culture) : Dash;
        SysSinceSub.Text = c.AccountCreated is { } created
            ? L.T("settings.sys.accountCreated", created.ToString("d", culture)) : "";
        SysSinceSub.Visibility = Shown(SysSinceSub.Text);

        // The OAuth access token's own clock. Expiry is normal and self-healing (Claude Code refreshes
        // it silently), so an expired token is stated, not alarming.
        SysToken.Text = c.TokenExpires is { } exp
            ? L.T(exp > DateTime.Now ? "settings.sys.tokenValid" : "settings.sys.tokenExpired",
                  exp.ToString("g", culture))
            : Dash;
        SysTokenSub.Text = c.ScopeCount > 0 ? L.T("settings.sys.scopes", c.ScopeCount) : "";
        SysTokenSub.Visibility = Shown(SysTokenSub.Text);

        // ---- The Claude Code installation ----
        SysCli.Text = c.CliVersion is { } cli ? $"v{cli}" : Dash;
        SysCliSub.Text = Join(
            // The install method is a literal token ("native", "npm-global") — shown verbatim rather
            // than prettified, so it matches what Claude Code's own docs and configs call it.
            c.InstallMethod is { } im ? L.T("settings.sys.installMethod", im) : null,
            c.AutoUpdates switch
            {
                true => L.T("settings.sys.autoUpdateOn"),
                false => L.T("settings.sys.autoUpdateOff"),
                null => null,
            });
        SysCliSub.Visibility = Shown(SysCliSub.Text);

        SysConfigDir.Text = DisplayPath(c.ConfigDir);
        SysConfigDirSub.Text = c.ConfigDirOverridden ? L.T("settings.sys.configOverridden") : "";
        SysConfigDirSub.Visibility = Shown(SysConfigDirSub.Text);
        SysOpenConfig.Tag = c.ConfigDir;
        SysOpenConfig.IsEnabled = System.IO.Directory.Exists(c.ConfigDir);

        SysProjects.Text = c.ProjectCount > 0
            ? c.ProjectCount.ToString(culture) : Dash;

        // ---- This app + the machine ----
        SysApp.Text = $"v{Updater.CurrentVersion}";
        SysAppSub.Text = Environment.ProcessPath is { } exe ? DisplayPath(exe) : "";
        SysAppSub.Visibility = Shown(SysAppSub.Text);

        // Windows 11 reports itself as 10.0.<build ≥ 22000>; the build is what a bug report needs.
        Version os = Environment.OSVersion.Version;
        SysOs.Text = L.T(os.Build >= 22000 ? "settings.sys.win11" : "settings.sys.win10", os.Build);
        SysOsSub.Text = Join(
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant());
        SysOsSub.Visibility = Shown(SysOsSub.Text);

        SysDataDir.Text = DisplayPath(Settings.DataDir);
        SysOpenData.Tag = Settings.DataDir;
        SysOpenData.IsEnabled = System.IO.Directory.Exists(Settings.DataDir);
    }

    // A value line is hidden rather than left blank, so a row with nothing to qualify stays compact.
    private static Visibility Shown(string text) =>
        text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    // " · "-joined qualifier line, skipping whatever isn't there.
    private static string Join(params string?[] parts) =>
        string.Join(" · ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    /// <summary>
    /// A path as this page shows it. While the identity is masked, the home and app-data prefixes are
    /// folded back to <c>~</c> / <c>%LocalAppData%</c> — an absolute path spells out the Windows account
    /// name, which is exactly what a screenshot shouldn't. Revealing the holder reveals these too.
    /// </summary>
    private string DisplayPath(string path)
    {
        if (_revealIdentity) return path;
        return Fold(Fold(path,
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "%LocalAppData%"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "~");
    }

    private static string Fold(string path, string prefix, string with) =>
        prefix.Length > 0 && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? with + path[prefix.Length..]
            : path;

    private static string AuthName(AuthMethod auth) => L.T(auth switch
    {
        AuthMethod.Subscription => "settings.sys.authSubscription",
        AuthMethod.ApiKey => "settings.sys.authApiKey",
        AuthMethod.Bedrock => "settings.sys.authBedrock",
        AuthMethod.Vertex => "settings.sys.authVertex",
        _ => "settings.sys.authNone",
    });

    // Ask the CLI itself: `claude auth status --json` under this profile's config dir. It is the
    // authoritative answer (it resolves precedence the way Claude Code does, and covers setups no file
    // read can see) but it costs a Node start, so it runs only when asked — and off the UI thread.
    private async void SysAuthCheck_Click(object sender, RoutedEventArgs e)
    {
        ClaudeInfo c = _profiles[Math.Clamp(_profile, 0, _profiles.Count - 1)];
        SysAuthCheck.IsEnabled = false;
        SysAuthSource.Text = L.T("settings.sys.authChecking");
        SysAuthSource.Visibility = Visibility.Visible;

        (AuthMethod auth, string? source, string? error) = await ClaudeAccount.QueryAuthStatusAsync(c.ConfigDir);

        if (error is not null)
        {
            // The inferred reading stands; say that the check itself failed rather than overwriting it.
            SysAuthSource.Text = Join(c.AuthSource, L.T("settings.sys.authCheckFailed", error));
            SysAuthCheck.IsEnabled = true;
            return;
        }
        c.Auth = auth;
        c.AuthSource = source;
        c.AuthConfirmed = true;
        RenderSystemInfo();
    }

    private void SysReveal_Click(object sender, RoutedEventArgs e)
    {
        _revealIdentity = !_revealIdentity;
        RenderSystemInfo();
    }

    // Open the folder a button carries in its Tag (the config dir, or the app's own data dir).
    private void SysOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag as string is not { Length: > 0 } dir) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(L.T("dialog.openLinkFailed", dir, ex.Message),
                L.T("dialog.appName"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // Copy the page as plain text for a bug report. It copies exactly what is on screen — a masked
    // holder and folded paths stay masked in the clipboard.
    private void SysCopy_Click(object sender, RoutedEventArgs e)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"{L.T("settings.sys.title")} — Claude Code Tray v{Updater.CurrentVersion}");
        foreach ((string label, string value) in
                 new (string, string)[]
                 {
                     // Which profile this reading is of, and how many exist — otherwise a pasted
                     // report from a multi-profile machine is ambiguous about whose numbers it holds.
                     (L.T("settings.sys.profile"), _profiles.Count > 1
                         ? $"{_profiles[_profile].Label} ({L.T("settings.sys.profileCount", _profiles.Count)})"
                         : ""),
                     (L.T("settings.sys.plan"), Line(SysPlan, SysPlanTier)),
                     (L.T("settings.sys.holder"), Line(SysHolder, SysHolderMail)),
                     (L.T("settings.sys.org"), SysOrgRow.Visibility == Visibility.Visible ? Line(SysOrg, SysOrgSub) : ""),
                     (L.T("settings.sys.extra"), SysExtra.Text),
                     (L.T("settings.sys.since"), Line(SysSince, SysSinceSub)),
                     (L.T("settings.sys.session"), Line(SysToken, SysTokenSub)),
                     (L.T("settings.sys.cli"), Line(SysCli, SysCliSub)),
                     (L.T("settings.sys.configDir"), Line(SysConfigDir, SysConfigDirSub)),
                     (L.T("settings.sys.projects"), SysProjects.Text),
                     (L.T("settings.sys.tray"), Line(SysApp, SysAppSub)),
                     (L.T("settings.sys.machine"), Line(SysOs, SysOsSub)),
                     (L.T("settings.sys.dataDir"), SysDataDir.Text),
                 })
            if (value.Length > 0) sb.AppendLine($"{label}: {value}");

        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            SysCopyHint.Text = L.T("settings.sys.copied");
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process; say so instead of failing silently.
            SysCopyHint.Text = ex.Message;
        }
    }

    // One row as text: the reading, plus its qualifier in parentheses when there is one on screen.
    private static string Line(System.Windows.Controls.TextBlock value, System.Windows.Controls.TextBlock sub) =>
        sub.Visibility == Visibility.Visible && sub.Text.Length > 0
            ? $"{value.Text} ({sub.Text})"
            : value.Text;
}
