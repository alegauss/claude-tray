using System.Windows;

namespace ClaudeTray;

/// <summary>Part of <see cref="SettingsPage"/> — one page per file, split out by T134, moved verbatim.</summary>
internal partial class SettingsPage
{
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
            true => L.T("settings.sys.enabled") + ExtraInUse(c),
            false => L.T("settings.sys.disabled"),
            null => Dash,
        };

        SysSince.Text = c.FirstToken is { } first ? first.ToString("d", culture) : Dash;
        SysSinceSub.Text = c.AccountCreated is { } created
            ? L.T("settings.sys.accountCreated", created.ToString("d", culture)) : "";
        SysSinceSub.Visibility = Shown(SysSinceSub.Text);

        // ---- Whether the allowance is being *used*, not merely available (T182) ----
        //
        // "Enabled" is a property of the account and answers a question nobody opening this page
        // mid-doubt is asking: what they want to know is whether it is costing them anything right now.
        // The reading comes from the profile's own stored history (T179) rather than a live call — this
        // page makes no network requests, and the last poll is as fresh as the tray is. A profile with
        // no history yet, or one whose readings predate T179, adds nothing rather than claiming a zero.
        static string ExtraInUse(ClaudeInfo c)
        {
            try
            {
                double? extra = UsageHistory.Latest(ProfileStore.KeyFor(c))?.Extra;
                return extra switch
                {
                    null => "",
                    > 0.001 => L.T("settings.sys.extraInUse", (extra.Value * 100).ToString("0.#", L.Culture)),
                    _ => L.T("settings.sys.extraIdle"),
                };
            }
            catch { return ""; }
        }

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
