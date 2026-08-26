using System.Windows;

namespace ClaudeTray;

/// <summary>Part of <see cref="SettingsPage"/> — one page per file, split out by T134, moved verbatim.</summary>
internal partial class SettingsPage
{
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
    /// opening Settings for no benefit. A preview asked for the account fixture (T121) gets it here, so
    /// no page has to know whether it is looking at this machine.</summary>
    private List<ClaudeInfo> DiscoverProfiles() =>
        _sampleProfiles ?? (_discovered ??= ClaudeAccount.Discover(_settings.Profiles));

    private List<ClaudeInfo>? _discovered;

    /// <summary>The synthetic profiles behind <c>--settings --sample</c>, or null on a real machine.</summary>
    private readonly List<ClaudeInfo>? _sampleProfiles;

    private void InvalidateProfiles() => _discovered = null;

    private void LoadProfiles(string? selectDir = null)
    {
        _ccProfiles = DiscoverProfiles();

        _fillingProfile = true;
        CcProfileCombo.Items.Clear();
        foreach (ClaudeInfo p in _ccProfiles)
            CcProfileCombo.Items.Add(new System.Windows.Controls.ComboBoxItem
            {
                Content = p.IsDefault ? L.T("settings.sys.profileDefault", p.Label) : p.Label,
            });
        int index = 0;
        if (selectDir is { Length: > 0 })
            for (int i = 0; i < _ccProfiles.Count; i++)
                if (string.Equals(_ccProfiles[i].ConfigDir, selectDir, StringComparison.OrdinalIgnoreCase))
                    index = i;
        if (_ccProfiles.Count > 0) CcProfileCombo.SelectedIndex = index;
        _fillingProfile = false;

        // With one profile there is nowhere for the icon to follow to and nothing to point the
        // environment at, so both of the app-wide rows would be switches that do nothing — the whole
        // section goes, header included, and comes back the moment a second profile is added
        // (T126, T145; one gate since T156 put them in a card of their own).
        ProfilesGlobalHeader.Visibility = ProfilesGlobalCard.Visibility =
            _ccProfiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        // The live reading, not the intent — so the row still tells the truth when something else on
        // the machine set the variable, or when the tray's own write failed.
        EnvSyncValue.Text = EnvironmentProfile.Current() is { Length: > 0 } v
            ? v
            : L.T("settings.cc.envSyncNone");
        // Auto-follow's own description says when it cannot do anything (T371). Composed rather than
        // switched between two strings, so the sentence that explains the setting is never replaced by the
        // sentence about this machine — a reader needs both, and needs to see which is which.
        FollowActiveRow.Description = L.T("settings.cc.followActiveDesc")
            + (_ccProfiles.Count > 1 && !ProfileActivity.CanFollow(_ccProfiles)
                ? " " + L.T("settings.cc.followActiveShared") : "");

        FillProfileFields();
        LoadLinkSides();
    }

    private ClaudeInfo? SelectedProfile =>
        CcProfileCombo.SelectedIndex >= 0 && CcProfileCombo.SelectedIndex < _ccProfiles.Count
            ? _ccProfiles[CcProfileCombo.SelectedIndex]
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

            // T143: only offered for a non-default profile — see the row's comment in the XAML for why
            // the default one must not get this command at all, not merely a discouraged one. T144 adds
            // the second half of that reason: `~/.claude` is never selectable by *setting* the variable,
            // whichever profile happens to be the default, so the setx form would be a footgun there
            // even when the dir is not the current default.
            bool offerTerminalDefault = p is { IsDefault: false }
                && !ClaudeAccount.SamePath(p.ConfigDir, ClaudeAccount.HomeConfigDir);
            ProfileTerminalRow.Visibility = ProfileTerminalDivider.Visibility =
                offerTerminalDefault ? Visibility.Visible : Visibility.Collapsed;
            if (offerTerminalDefault && p is not null)
            {
                ProfileTerminalCommand.Text = $"setx CLAUDE_CONFIG_DIR \"{p.ConfigDir}\"";
                ProfileTerminalHint.Text = "";
            }
        }
        finally { _fillingProfile = false; }
    }

    // Copies the command; never runs it. The tray stays a reader of CLAUDE_CONFIG_DIR, never a writer
    // of it at the user-environment level — see the row's comment in the XAML (T143).
    private void ProfileTerminalCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ProfileTerminalCommand.Text);
            ProfileTerminalHint.Text = L.T("settings.sys.copied");
        }
        catch (Exception ex)
        {
            // The clipboard can be locked by another process; say so instead of failing silently.
            ProfileTerminalHint.Text = ex.Message;
        }
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

    // ================= One setup across profiles (T370) =================
    // The surface for ProfileLink: two combos deciding which side keeps its files, the plan those two
    // produce, and a button that writes the script somewhere the user named. Nothing here writes into a
    // config dir, and nothing here runs the script — see the card's comment in the XAML.

    /// <summary>Set while the two combos are being filled, so seeding them does not read back as the user
    /// choosing sides and recompute the plan twice.</summary>
    private bool _fillingLink;

    /// <summary>
    /// Fill both side pickers and hide the whole section below two profiles, on the same gate as the
    /// all-profiles card: one profile has nothing to be linked to.
    ///
    /// <para>The seed is the default profile keeping its files, because that is the folder a bare
    /// <c>claude</c> already uses and the one whose tree is most likely the fuller of the two — and it is
    /// only a seed. The choice is the user's and the combos say so by both being editable.</para>
    /// </summary>
    private void LoadLinkSides()
    {
        LinkHeader.Visibility = LinkCard.Visibility = LinkNote.Visibility =
            _ccProfiles.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (_ccProfiles.Count < 2) return;

        _fillingLink = true;
        try
        {
            foreach (System.Windows.Controls.ComboBox combo in new[] { LinkPrimaryCombo, LinkSecondaryCombo })
            {
                combo.Items.Clear();
                foreach (ClaudeInfo p in _ccProfiles)
                    combo.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = p.Label });
            }
            int primary = Math.Max(0, _ccProfiles.FindIndex(p => p.IsDefault));
            LinkPrimaryCombo.SelectedIndex = primary;
            LinkSecondaryCombo.SelectedIndex = primary == 0 ? 1 : 0;
        }
        finally { _fillingLink = false; }
        ShowLinkPlan();
    }

    private void LinkSide_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_fillingLink) return;
        ShowLinkPlan();
    }

    /// <summary>The plan for whichever pair is selected, or the refusal in its place.</summary>
    private ProfileLink.Plan? _linkPlan;

    private void ShowLinkPlan()
    {
        LinkHint.Text = "";
        ClaudeInfo? a = At(LinkPrimaryCombo), b = At(LinkSecondaryCombo);
        _linkPlan = a is null || b is null ? null : ProfileLink.For(a.ConfigDir, b.ConfigDir, a.Label, b.Label);

        // A refusal is shown where the plan would be, not in a dialog: picking one profile twice is the
        // ordinary way to arrive here and is a sentence, not an error.
        if (_linkPlan is null || _linkPlan.Error is { Length: > 0 })
        {
            LinkPlanList.ItemsSource = null;
            LinkPlanCaption.Text = L.T("settings.cc.linkSame");
            LinkWriteButton.IsEnabled = false;
            return;
        }

        LinkPlanList.ItemsSource = LinkPlanRow.From(_linkPlan);
        int acting = _linkPlan.Acting.Count();
        // The edge, on the surface the decision is made on (T374): a list of opinions has to say what it
        // has no opinion about, or the reader is told nothing is missing.
        string[] unclaimed = _linkPlan.Edge.Unclaimed;
        LinkPlanCaption.Text = L.N("settings.cc.linkPlanCount", acting, _linkPlan.SecondaryLabel)
                               + (_linkPlan.NeedsSymlink ? " " + L.T("settings.cc.linkNeedsDev") : "")
                               + (unclaimed.Length > 0
                                   ? " " + L.N("settings.cc.linkEdge", unclaimed.Length,
                                       string.Join(", ", unclaimed.Take(4))
                                       + (unclaimed.Length > 4 ? " …" : ""))
                                   : "");
        LinkWriteButton.IsEnabled = acting > 0;
    }

    private ClaudeInfo? At(System.Windows.Controls.ComboBox combo) =>
        combo.SelectedIndex >= 0 && combo.SelectedIndex < _ccProfiles.Count
            ? _ccProfiles[combo.SelectedIndex]
            : null;

    /// <summary>
    /// Write the script where the user points, and reveal it. A save dialog rather than a folder this app
    /// chose: the whole promise is that it writes nothing anybody did not ask for, and a path the user
    /// typed is the strongest form of that — it also means the confirmation has somewhere true to point,
    /// since a file in <c>%LocalAppData%</c> is a file most people will not find and will compose again.
    /// </summary>
    private void LinkWrite_Click(object sender, RoutedEventArgs e)
    {
        if (_linkPlan is not { Error: null } plan) return;

        using var dlg = new System.Windows.Forms.SaveFileDialog
        {
            Title = L.T("settings.cc.linkSaveTitle"),
            Filter = L.T("settings.cc.linkSaveFilter") + " (*.ps1)|*.ps1",
            FileName = "link-profiles.ps1",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            OverwritePrompt = true,
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

        try
        {
            // UTF-8 with a BOM, for ProfilesCli's reason: Windows PowerShell 5.1 reads a BOM-less .ps1 as
            // ANSI, and the two config-dir paths are in this text verbatim.
            System.IO.File.WriteAllText(dlg.FileName, ProfileLink.Script(plan),
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            LinkHint.Text = L.T("settings.cc.linkWrote");
            Reveal(dlg.FileName);
        }
        catch (Exception ex)
        {
            // Said in place, where the button is, rather than thrown: the disk refusing a path is the
            // user's to fix and is not a reason for the window to go away.
            LinkHint.Text = ex.Message;
        }
    }

    /// <summary>Open Explorer with the file selected. Best effort — a shell that will not start is not a
    /// failed write, and the hint above already names the file.</summary>
    private static void Reveal(string path)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe",
                $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch { /* the confirmation names the path either way */ }
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
}
