using System.Windows;

namespace ClaudeTray;

/// <summary>
/// The settings page — a destination of <see cref="MainWindow"/>, which owns the chrome and the theme
/// (the built-in .NET Fluent one, so it follows the Windows light/dark setting and gets the Windows 11
/// look). The layout lives entirely in
/// <c>SettingsPage.xaml</c> as a declarative grid — there is no imperative z-order stacking, which
/// is what made the old WinForms sidebar fragile. Its own sidebar is the *second* level of navigation:
/// six settings pages inside one of the shell's three destinations.
///
/// On Save it applies the edited <see cref="Settings"/> through the <c>onSave</c> callback supplied at
/// construction and says so in place — the page no longer closes anything, because there is nothing to
/// close. Cancel raises <see cref="Cancelled"/>: the shell builds a fresh page from the live model,
/// which discards the edits by construction rather than by undoing them control by control. The
/// interval is edited in minutes (the model stores seconds).
/// </summary>
internal partial class SettingsPage : System.Windows.Controls.UserControl
{
    private readonly Settings _settings;

    /// <summary>What this page was built from, kept untouched beside the copy it edits. Save hands both
    /// back, because "which write is newer" is a comparison and needs the before as well as the after —
    /// see <see cref="Settings.CarryUnchangedFrom"/> (T229).</summary>
    private readonly Settings _opened;

    private readonly Action<Settings, Settings> _onSave;

    /// <summary>Raised when the user discards the edits. The shell replaces this page with a new one
    /// over the live settings — see the class summary.</summary>
    internal event Action? Cancelled;

    private static double MinMinutes => Settings.MinRefreshSeconds / 60.0;
    private static double MaxMinutes => Settings.MaxRefreshSeconds / 60.0;

    /// <param name="sampleProfiles">Preview-only (T121): render the profile picker, the Claude Code page
    /// and the System information page over the synthetic <see cref="AccountFixture"/> accounts instead
    /// of this machine's, so the page can be screenshotted for the README and the site.</param>
    /// <param name="revealIdentity">Preview-only: open with the holder and the paths already revealed.
    /// Safe to publish only together with <paramref name="sampleProfiles"/>.</param>
    public SettingsPage(Settings current, Action<Settings, Settings> onSave, string? initialPage = null,
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
        _opened = current.Clone();   // a second, never-edited copy: the "before" of the comparison

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
        NotifyExtraCheck.IsChecked = _settings.NotifyOnExtraUsage;
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

        LoadProfiles();
        LoadSystemInfo();

        SelectPage(Resolve(initialPage) ?? "General");
        // …and again once the page is inside a window, because that is where the theme's brushes live:
        // a page built before it is shown resolves none of them (see SelectPage), so the highlight the
        // constructor's pass could not paint lands here.
        Loaded += (_, _) => SelectPage(_page);
    }

    /// <summary>
    /// The six sidebar pages, in the order the sidebar shows them, and the one place their names are
    /// written (T262). The constructor used to carry them as a <c>switch</c> ending in
    /// <c>_ =&gt; "General"</c>, so a name nothing here is called selected General in silence — and
    /// <c>--capture-settings &lt;out&gt; NoSuchPage</c> then wrote a picture of General under the name the
    /// caller gave, printed <c>wrote</c>, and exited 0.
    /// </summary>
    internal static readonly string[] Pages =
        { "General", "Display", "ClaudeCode", "Notifications", "System", "About" };

    /// <summary>The canonical spelling of a page name, or null when nothing here is called that. The
    /// <em>flag</em> is what refuses (see <c>Program.RefusedName</c>); the page itself still falls back,
    /// because a window that throws over a bad argument is a worse answer than one that opens.</summary>
    internal static string? Resolve(string? name) =>
        name is null ? null
        : Array.Find(Pages, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The sidebar page currently shown — re-applied on Loaded, see the constructor.</summary>
    private string _page = "General";

    // Switch the visible page (General / System / About …) and move the sidebar selection highlight.
    private void Nav_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => SelectPage((string)((FrameworkElement)sender).Tag);

    private void SelectPage(string page)
    {
        _page = page;
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

        // TryFindResource, not FindResource: the theme dictionary hangs off the window, and this runs
        // once from the constructor — before the page has a window to look up through. That pass leaves
        // the highlight transparent and the Loaded pass paints it.
        var selected = TryFindResource("SubtleFillColorSecondaryBrush") as System.Windows.Media.Brush
                       ?? System.Windows.Media.Brushes.Transparent;
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

        // About and System information are read-only pages: hide Save and turn Cancel into a plain
        // Close — there are no edits to discard there, and closing the shell is what the other two
        // destinations' footers offer in the same corner.
        _readOnlyPage = about || system;
        SaveButton.Visibility = _readOnlyPage ? Visibility.Collapsed : Visibility.Visible;
        CancelButton.Content = _readOnlyPage ? L.T("settings.close") : L.T("settings.cancel");
        SavedText.Visibility = Visibility.Collapsed;   // the confirmation belongs to the page that saved
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
        _settings.NotifyOnExtraUsage = NotifyExtraCheck.IsChecked == true;
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

        _onSave(_settings, _opened);
        ConfirmSaved();
    }

    /// <summary>
    /// Say the settings were applied, where the button that applied them is. Saving used to close the
    /// window, which was the whole confirmation; inside a page that stays open, silence would be
    /// indistinguishable from a click that missed.
    /// </summary>
    private void ConfirmSaved()
    {
        SavedText.Visibility = Visibility.Visible;
        _savedFade ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _savedFade.Stop();   // a second save restarts the three seconds rather than inheriting them
        _savedFade.Tick -= HideSaved;
        _savedFade.Tick += HideSaved;
        _savedFade.Start();
    }

    private System.Windows.Threading.DispatcherTimer? _savedFade;

    private void HideSaved(object? sender, EventArgs e)
    {
        _savedFade?.Stop();
        SavedText.Visibility = Visibility.Collapsed;
    }

    /// <summary>Set by <see cref="SelectPage"/> for About / System information, where the same button
    /// reads "Close" and means the window rather than "Cancel" over edits that don't exist.</summary>
    private bool _readOnlyPage;

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_readOnlyPage) Window.GetWindow(this)?.Close();
        else Cancelled?.Invoke();
    }

    // The off-screen capture and its framing live in SettingsPage.Capture.cs (T134's convention: a page
    // with several independent surfaces is one class in several files).
}
