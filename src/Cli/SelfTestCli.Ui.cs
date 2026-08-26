using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SelfTestCli"/> — the previews, the captures, and what a control announces.
///
/// <para>One class in several files, on this repository's own rule for a type with many
/// independent surfaces (T133–T134, applied here by T381): the suite reached 9,330 lines and
/// 76 sections in one file, and adding a section meant scrolling past the whole of it to find
/// whether the helper you wanted already existed. <c>Run</c> keeps the ordered list of
/// sections — it is the table of contents, and its order is load-bearing — along with
/// <c>Check</c>, <c>Skip</c>, <c>Temp</c>, <c>CodeOf</c>, <c>Repo</c> and the counters, which
/// are the suite's own vocabulary.</para>
/// </summary>
internal static partial class SelfTestCli
{
    /// <summary>
    /// The Settings round trip, <b>two</b> checks per property: the window is handed a total copy
    /// (<see cref="Settings.Clone"/>), hands back that copy and the untouched one it opened with, and the
    /// tray merges by which write is newer (<see cref="Settings.CarryUnchangedFrom"/>). Every property has
    /// to answer both ways — the page edited it and its value survives, or the page left it alone and a
    /// value the menu moved meanwhile survives.
    ///
    /// <para><b>Both directions, per field, is what T229 needed.</b> T162's sweep asked one question per
    /// property and read the answer off the attribute, so an unmarked field was asserted to keep the page's
    /// value — which is what happened, including for the two fields with a control on <em>both</em>
    /// surfaces, whose menu edit was silently reverted. Nothing here knows which controls a page has, and
    /// it no longer has to: a field the page did not touch is indistinguishable from one it cannot touch,
    /// and both want the live value.</para>
    ///
    /// <para>Driven by reflection over <see cref="Settings.Fields"/> rather than a list written here: a
    /// field added tomorrow is covered tomorrow. A property whose type has no varying rule below
    /// <b>fails</b> rather than being skipped, so adding one this cannot vary is a red check, not a gap.</para>
    /// </summary>
    private static void SettingsRoundTrip()
    {
        foreach (PropertyInfo p in Settings.Fields)
        {
            if (Vary(p) is not { } varied)
            {
                Fail($"{p.Name}: the merge decides which write is newer",
                     "no varying rule for this property's type — add one");
                continue;
            }
            (object? edited, object? moved) = varied;

            // One shape, both directions. The window opened over `opened`; the menu then moved the field
            // to `moved` in the live model; Save hands back what the page holds plus what it opened with.
            static Settings Merge(PropertyInfo p, object? opened, object? onPage, object? live)
            {
                var liveModel = new Settings();
                p.SetValue(liveModel, live);
                var openedCopy = new Settings();
                p.SetValue(openedCopy, opened);
                Settings edited = openedCopy.Clone();
                p.SetValue(edited, onPage);

                Settings applied = edited.Clone();
                applied.CarryUnchangedFrom(liveModel, openedCopy.Clone());
                return applied;
            }

            // The page left it where it found it, and the menu moved it meanwhile: the menu's value wins.
            // This is every field T162 marked, plus the two it could not — and it is T126 and T155.
            object? untouched = p.GetValue(Merge(p, opened: edited, onPage: edited, live: moved));
            Check($"{p.Name}: a value the menu moved survives a window that did not touch it",
                  Json(untouched) == Json(moved), $"{Json(untouched)} vs {Json(moved)}");

            // The page edited it: the page's value wins, whatever the live model holds.
            object? touched = p.GetValue(Merge(p, opened: moved, onPage: edited, live: moved));
            Check($"{p.Name}: and an edit made on the page is not carried away",
                  Json(touched) == Json(edited), $"{Json(touched)} vs {Json(edited)}");
        }

        // The two fields the ownership split could not express, named, because they are the defect: a
        // control on the Claude Code page *and* a toggle in the menu. If either loses its page control the
        // merge still holds — this is here so the case that produced T229 is pinned by name.
        foreach (string shared in new[] { nameof(Settings.FollowActiveProfile),
                                          nameof(Settings.SyncEnvironmentProfile) })
        {
            PropertyInfo p = Settings.Fields.First(f => f.Name == shared);
            var live = new Settings();
            p.SetValue(live, true);                       // the menu turned it on
            var opened = new Settings();                  // the window was built before that: false
            Settings applied = opened.Clone();
            applied.CarryUnchangedFrom(live, opened.Clone());
            Check($"{shared}: flipped in the menu, Save in an open window does not undo it",
                  Equals(p.GetValue(applied), true),
                  $"got {Json(p.GetValue(applied))} — T171's environment write reconciles off this");
        }
    }

    /// <summary>
    /// What T188 was: two toasts wearing the same clay for opposite news — quota back early, and you have
    /// started paying. A colour on these cards is a claim about what kind of news it is, so the rule is one
    /// row per <see cref="ToastWindow.ToastTheme"/> and no two rows alike, and it is asserted rather than
    /// remembered. Driven off the enum, so a theme added tomorrow is covered tomorrow.
    ///
    /// <para>Reading the palette needs no window: it is a static table of hex strings, which is why it was
    /// worth separating from the brush it feeds.</para>
    /// </summary>
    private static void ToastColours()
    {
        var seen = new Dictionary<string, ToastWindow.ToastTheme>(StringComparer.OrdinalIgnoreCase);
        foreach (ToastWindow.ToastTheme t in Enum.GetValues<ToastWindow.ToastTheme>())
        {
            (string light, string mid, string deep) = ToastWindow.Palette(t);
            string row = $"{light}/{mid}/{deep}";

            if (!Check($"{t} names its own three stops", light.Length > 0 && mid.Length > 0 && deep.Length > 0, row))
                continue;
            if (seen.TryGetValue(row, out ToastWindow.ToastTheme other))
                Fail($"{t} has a colour of its own",
                     $"it wears {row}, which is already {other}'s — the T188 collision, in a new pair");
            else
            {
                _passed++;
                Console.WriteLine($"  ok    {t} has a colour of its own ({mid})");
                seen[row] = t;
            }
        }

        // The one row whose value is a cross-surface agreement rather than a free choice: clay is what the
        // icon's bar and the chart's second axis mean by "past the included quota" (T182, T183, T184).
        //
        // Asserted against `Brand`, not against a literal (T310). This check used to pin one of twelve
        // spellings to the hex it happened to be written in — so it held the toast still while the other
        // eleven were free to move, which is the failure it exists to prevent wearing a green tick.
        Check("extra usage keeps the clay the icon and the chart use",
              ToastWindow.Palette(ToastWindow.ToastTheme.ExtraUsage).Mid.Equals(Brand.ClayHex, StringComparison.OrdinalIgnoreCase),
              ToastWindow.Palette(ToastWindow.ToastTheme.ExtraUsage).Mid);

        // The two edges that convert, checked against the value they convert from — GDI+ draws the icon and
        // WPF draws everything else, so the one thing they can share is the bytes.
        Check("and the tray icon's clay is the same three bytes",
              Brand.ClayGdi.R == Brand.ClayR && Brand.ClayGdi.G == Brand.ClayG && Brand.ClayGdi.B == Brand.ClayB);
        Check("as is the brush the charts and the markup resolve",
              Brand.ClayBrush is System.Windows.Media.SolidColorBrush
                  { Color: { R: Brand.ClayR, G: Brand.ClayG, B: Brand.ClayB } });

        // And the rule that keeps the other eleven from coming back. AGENTS.md already forbids hardcoded
        // hex in markup; this is that rule with an owner, over the code as well, because the drift it names
        // arrived in code first and the markup copies were added later by people reading the code.
        Repo("clay is spelled in one file and resolved everywhere else", root =>
        {
            // The needles come from Brand too, so this file spells the clay no more than any other does —
            // the first draft used literals and duly reported itself, which is the check working.
            string hex = Brand.ClayHex.TrimStart('#');
            string bytes = $"{Brand.ClayR}, {Brand.ClayG}, {Brand.ClayB}";
            var spellings = new List<string>();
            foreach (string path in Directory.EnumerateFiles(Path.Combine(root, "src"), "*.*",
                                                             SearchOption.AllDirectories))
            {
                string name = Path.GetFileName(path);
                if (name is "Brand.cs") continue;                       // where it is declared
                if (Path.GetExtension(path) is not (".cs" or ".xaml")) continue;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    if (lines[i].Contains(hex, StringComparison.OrdinalIgnoreCase)
                        || lines[i].Contains(bytes, StringComparison.Ordinal))
                        spellings.Add($"{name}:{i + 1}");
            }
            Check("no surface spells the clay out for itself", spellings.Count == 0,
                  $"{string.Join(", ", spellings)} — one colour means one thing, so it has one source; a " +
                  "second spelling is a surface free to stop agreeing with the icon (T310)");
        }, "src/Ui/Brand.cs");

        DocCommentsAttached();

        // T321. The digits' own colour, on the same rule as the table above: three windows, three colours,
        // no two alike. Driven off the metric list the tray offers, so a fourth window would arrive here
        // uncoloured rather than silently wearing the session's cream — which is the one failure a contact
        // sheet cannot show, because a scope reading as the session looks exactly like a session.
        var inks = new Dictionary<int, string>();
        foreach (string scope in new[] { "5h", "7d", "extra" })
        {
            int argb = IconRenderer.NumberInk(scope).ToArgb();
            if (inks.TryGetValue(argb, out string? other))
                Fail($"the {scope} number has a colour of its own",
                     $"it is drawn in {other}'s ink, so the two windows are one number on screen");
            else
            {
                _passed++;
                Console.WriteLine($"  ok    the {scope} number has a colour of its own");
                inks[argb] = scope;
            }
        }
        Check("the session keeps the cream the icon always drew, so an ordinary tray is unchanged",
              IconRenderer.NumberInk("5h").ToArgb() == Color.White.ToArgb());
        Check("and a scope nobody here spells falls back to it rather than picking a colour",
              IconRenderer.NumberInk("90d").ToArgb() == IconRenderer.NumberInk("5h").ToArgb());

        // T322. Which fact the ink states when there are two: an account that is paying outranks the window
        // the figure happens to be about, because T320 moves that figure onto the week and the state the
        // colour exists for would otherwise read as an ordinary week — with no bar to say otherwise, since
        // "0 left" draws none.
        foreach (string scope in new[] { "5h", "7d", "extra", "90d" })
            Check($"billing outranks the window the number is about ({scope})",
                  IconRenderer.NumberInk(scope, billing: true).ToArgb()
                  == IconRenderer.NumberInk("extra").ToArgb());
        // And it is billing alone: stopped work is not paid work, so it keeps the window's own colour rather
        // than claiming money is being spent (T182's rule, in the one place a colour could break it).
        Check("a window that is merely spent keeps its own colour, since orange means paying",
              IconRenderer.NumberInk("7d").ToArgb() == IconRenderer.NumberInk("7d", billing: false).ToArgb()
              && IconRenderer.NumberInk("7d").ToArgb() != IconRenderer.NumberInk("extra").ToArgb());
        // The pale pair T322 replaced, named by value: a hue this washed out is a white with a cast at 16px,
        // and nothing but a number here can keep the two from being chosen again on an 8x sheet.
        Check("neither ink is one of the washed-out values the tray was reported as drawing white",
              IconRenderer.NumberInk("7d").ToArgb() != Color.FromArgb(255, 255, 233, 160).ToArgb()
              && IconRenderer.NumberInk("extra").ToArgb() != Color.FromArgb(255, 255, 179, 71).ToArgb());
    }

    // ---------------------------------------------------------------- Block AF: the capture's output path

    /// <summary>
    /// What every capture flag owes the path it was handed (T187): the file appears, and the directories
    /// above it are created rather than being assumed. The defect this pins was one <c>File.Create</c> on a
    /// path nothing created, throwing from a <c>DispatcherTimer</c> tick <em>after</em> the window had
    /// rendered — so the check is here rather than in a screenshot, which is the one place it could not be
    /// seen. Held on <see cref="OutFile"/>, which the three <c>SaveSnapshot</c> bodies now share.
    /// </summary>
    private static void OutputPaths(string root)
    {
        string nested = Path.Combine(root, "does", "not", "exist", "capture.png");
        using (FileStream fs = OutFile.Create(nested)) fs.WriteByte(0x89);
        Check("a capture into a directory tree that does not exist writes the file",
              File.Exists(nested), $"nothing at {nested}");

        // Truncation, because a second capture over yesterday's PNG must not leave its tail behind.
        using (FileStream fs = OutFile.Create(nested)) fs.WriteByte(0x89);
        Check("and a second capture over the same path truncates it",
              new FileInfo(nested).Length == 1, $"{new FileInfo(nested).Length} bytes, expected 1");

        // A bare filename's GetDirectoryName is empty, which is the case a naive parent-of check skips.
        string cwd = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(root);
            using (FileStream fs = OutFile.Create("bare.png")) fs.WriteByte(0x89);
            Check("a bare filename resolves against the current directory, not to no parent at all",
                  File.Exists(Path.Combine(root, "bare.png")));
        }
        finally { Directory.SetCurrentDirectory(cwd); }
    }

    // ---------------------------------------------------------------- Block AF: the five string tables

    /// <summary>
    /// Where the Statistics window lands when the tray hands it a rebuilt watch list (T319).
    ///
    /// <para>The picker was filled once, when the window was built, and everything after that rested on
    /// "index 0 is the account the poll is about" — which stops being true the moment the icon changes
    /// hands with the window open, because the list is monitored-first and the switch reorders it. The
    /// reported symptom was a work account's 86% drawn under a personal account's name, while picking the
    /// account actually being worked in read as the empty one.</para>
    ///
    /// <para>Two halves, and each is asserted where it can be. The decision is
    /// <see cref="StatisticsPage.PickerIndex"/> — pure, over keys, no window — and the wiring is a call
    /// site that has to exist in the one method that detects a switch, which is asked of the source the
    /// way T293's single assigner already is.</para>
    /// </summary>
    private static void ProfilePicker()
    {
        static ClaudeInfo P(string label, string dir) => new() { Label = label, ConfigDir = dir };
        static string K(ClaudeInfo p) => ProfileStore.KeyFor(p);

        ClaudeInfo personal = P("Pessoal", @"C:\Users\x\.claude");
        ClaudeInfo work = P("VILT", @"C:\Users\x\.claude-vilt");
        ClaudeInfo third = P("Cliente", @"C:\Users\x\.claude-cliente");

        // Before the switch: the icon is on Pessoal, so that is what the list leads with.
        var before = new List<ClaudeInfo> { personal, work };
        // After it: the same two accounts, and the tray puts the monitored one first.
        var after = new List<ClaudeInfo> { work, personal };

        Check("a window following the icon follows it through the switch",
              StatisticsPage.PickerIndex(after, K(personal), followingMonitored: true) == 0,
              "the icon moved and the report did not: the pushed reading lands under the old name");

        Check("and a profile picked by hand is found where the reorder put it",
              StatisticsPage.PickerIndex(after, K(personal), followingMonitored: false) == 1,
              "position 1 after a switch is not the profile position 1 named before it — this is the " +
              "whole defect: by index, the pick silently becomes the other account");

        Check("a pick that is no longer on the machine falls back to the icon's",
              StatisticsPage.PickerIndex(new List<ClaudeInfo> { work, third }, K(personal),
                                         followingMonitored: false) == 0,
              "an unregistered profile must not leave the picker pointing at nothing");

        Check("and an empty list answers 0 rather than throwing",
              StatisticsPage.PickerIndex(new List<ClaudeInfo>(), K(personal), followingMonitored: false) == 0);

        // The other half of the rebuild: when it may be skipped. The tray re-discovers on every menu open
        // (T137), so most calls change nothing, and rebuilding the items would shut an open dropdown.
        Check("two discoveries of the same machine build the same picker",
              StatisticsPage.Same(before, new List<ClaudeInfo> { P("Pessoal", @"C:\Users\x\.claude"), work }),
              "a fresh ClaudeInfo per discovery must compare by what the picker shows, not by reference");
        Check("a switch is not the same picker, because the order is the answer",
              !StatisticsPage.Same(before, after),
              "the reorder IS the switch — skipping it here is the frozen list all over again");
        Check("and neither is a renamed profile",
              !StatisticsPage.Same(before, new List<ClaudeInfo> { P("Casa", @"C:\Users\x\.claude"), work }),
              "the label is the whole of what an entry shows");
        Check("nor a profile added to the machine",
              !StatisticsPage.Same(before, new List<ClaudeInfo> { personal, work, third }));

        // The wiring. Behaviour here needs a window and a tray; that the call exists, in the method that
        // detects a switch, and above the early return that only a switch passes, does not.
        Repo("the open report is told where the switch is detected", root =>
        {
            string[] body = MethodBody(File.ReadAllLines(Path.Combine(root, "src/Tray/TrayContext.cs")),
                                       "private void RefreshWatched()");
            if (!Check("RefreshWatched's body can be read", body.Length > 0,
                       "the method was renamed — this check is reading nothing"))
                return;

            string[] code = body.Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)).ToArray();
            int told = Array.FindIndex(code, l => l.Contains("Statistics.SetProfiles(", StringComparison.Ordinal));
            if (!Check("the watch list reaches an open Statistics page from here", told >= 0,
                       "filled once at window-open, the picker names accounts the poll is no longer about"))
                return;

            // The early return is the switch check: below it, only a change of monitored account runs.
            int switched = Array.FindIndex(code, l => l.Contains("== before) return;", StringComparison.Ordinal));
            Check("and before the return that only a switch gets past",
                  switched < 0 || told < switched,
                  "a profile added or removed reorders the picker without the icon moving, and a rebuild " +
                  "below that return never sees it");
        }, "src/Tray/TrayContext.cs");
    }

    // ---------------------------------------------------------------- Block A: dating the overage spell

    /// <summary>
    /// The preview and capture flags — the surface every visual verification in this repository goes
    /// through, and the one thing none of the other assertions here touch (T207). Everything else is
    /// arithmetic, stores and rules; the variant tables and the refusals around them were held up by
    /// whoever next ran a flag by hand and read the result.
    ///
    /// <para>Each refusal asserted here has a failure mode that is silent by construction: a table that
    /// stops resolving a name renders <em>the default</em> instead, which is a screenshot of the wrong
    /// thing that looks exactly like a screenshot of the right one (T186, T198, T200), and
    /// <c>--sample</c> failing open publishes a real account (T205). None of them would be noticed by
    /// anything else in this file.</para>
    ///
    /// <para>They are cheap because they are pure: a table lookup and a resolver, returning a value or
    /// null, with no window and no file. The refusals print their catalogue to the console, which is
    /// their job and not this section's output, so those calls run through <see cref="Quietly"/>.</para>
    /// </summary>
    private static void Flags()
    {
        IReadOnlyList<StatsPreviews.Variant> stats = StatsPreviews.Catalogue;
        IReadOnlyList<ToastPreviews.Variant> toasts = ToastPreviews.Catalogue;

        // The guard on the precondition first: an empty table resolves nothing and refuses nothing, and
        // every claim below would hold vacuously over it.
        if (!Check("both preview tables carry their rows", stats.Count >= 8 && toasts.Count >= 5,
                   $"{stats.Count} stats variants, {toasts.Count} toasts"))
            return;

        static string[] Words(string name) => name.Length == 0 ? Array.Empty<string>() : new[] { name };
        static string Named(string name) => name.Length == 0 ? "(default)" : name;

        string[] deadStats = stats.Where(v => StatsPreviews.Resolve(Words(v.Name), capturing: false) is null)
                                  .Select(v => Named(v.Name)).ToArray();
        Check($"every --stats variant the table declares resolves ({stats.Count})", deadStats.Length == 0,
              $"{string.Join(", ", deadStats)} resolved to null — the flag would refuse a name it prints");

        // The asymmetry is the point: --capture-stats must refuse the variants whose content it cannot
        // render, and no others. Anchored on the two that ARE the method popup rather than on
        // `Variant.Capturable` — Resolve decides *by* that flag, so comparing the two is one expression
        // compared with itself, and stays green when the flag is inverted. Confirmed: with
        // `Capturable => true` the earlier version of this assertion passed.
        string[] popup = { "method", "thin" };
        if (!Check("the method-popup variants are still named that", popup.All(n => stats.Any(v => v.Name == n)),
                   $"{string.Join(", ", popup.Where(n => stats.All(v => v.Name != n)))} — renamed, and every " +
                   "claim below would hold by resolving nothing"))
            return;

        string[] leaked = popup
            .Where(n => Quietly(() => StatsPreviews.Resolve(new[] { n }, capturing: true)) is not null).ToArray();
        Check("--capture-stats refuses the method-popup variants it cannot render", leaked.Length == 0,
              $"{string.Join(", ", leaked)} — the PNG would be the page with no popup in it (T186)");

        string[] overRefused = stats.Where(v => !popup.Contains(v.Name))
            .Where(v => Quietly(() => StatsPreviews.Resolve(Words(v.Name), capturing: true)) is null)
            .Select(v => Named(v.Name)).ToArray();
        Check("and refuses nothing else", overRefused.Length == 0, string.Join(", ", overRefused));

        Check("while --stats still shows them, which is the whole asymmetry",
              popup.All(n => StatsPreviews.Resolve(new[] { n }, capturing: false) is not null),
              "the interactive flag refuses them too, so the popup cannot be got on screen at all");

        string[] deadModifiers = StatsPreviews.ModifierRows
            .Where(m => StatsPreviews.Resolve(new[] { m.Name }, capturing: false) is null)
            .Select(m => m.Name).ToArray();
        Check($"every --stats modifier composes ({StatsPreviews.ModifierRows.Count})", deadModifiers.Length == 0,
              string.Join(", ", deadModifiers));

        Check("a --stats name that names no row is refused, not defaulted",
              Quietly(() => StatsPreviews.Resolve(new[] { "nosuchvariant" }, capturing: false)) is null,
              "it resolved — an unknown name renders the default and calls it what was asked for (T186)");

        // A real variant beside an invented word must still be refused: taking the one it recognises is
        // the same defect wearing a legal-looking argument.
        Check("and so is a real variant beside one that is not",
              Quietly(() => StatsPreviews.Resolve(new[] { "shape", "nosuchmodifier" }, capturing: false)) is null,
              "it resolved 'shape' and dropped the word it did not know");

        string[] deadToasts = toasts.Where(v => ToastPreviews.Resolve(v.Name) is null)
                                    .Select(v => v.Name).ToArray();
        Check($"every toast variant the table declares resolves ({toasts.Count})", deadToasts.Length == 0,
              string.Join(", ", deadToasts));

        Check("a bare toast flag is the first row, not a refusal",
              ToastPreviews.Resolve(null)?.Name == toasts[0].Name,
              $"got '{ToastPreviews.Resolve(null)?.Name}', expected '{toasts[0].Name}'");

        Check("an unknown toast name is refused, not defaulted",
              Quietly(() => ToastPreviews.Resolve("nosuchcard")) is null,
              "it resolved — a screenshot of the wrong card looks like one of the right card (T198)");

        // The privacy guard (T200): a week that cannot be honoured must stop the run, because the
        // fallback is this machine's real account rendered into a file that gets published.
        string[] deadWeeks = Enum.GetNames<AccountFixture.SampleWeek>()
            .Where(w => AccountFixture.ResolveWeek(w) is null).ToArray();
        Check($"every week= name resolves ({Enum.GetNames<AccountFixture.SampleWeek>().Length})",
              deadWeeks.Length == 0, string.Join(", ", deadWeeks));
        Check("week= is case-insensitive, the way the flag is typed",
              AccountFixture.ResolveWeek("SPENDING") == AccountFixture.SampleWeek.Spending);
        Check("an absent week= is the default, not a refusal",
              AccountFixture.ResolveWeek(null) == AccountFixture.SampleWeek.Spending &&
              AccountFixture.ResolveWeek("") == AccountFixture.SampleWeek.Spending);
        Check("an unknown week= is refused, so --sample stops instead of falling back",
              Quietly(() => AccountFixture.ResolveWeek("nosuchweek")) is null,
              "it resolved — the run would build the default week and publish the wrong branch (T200)");

        // T205's refusal, which this block added: the one page whose content is an account, on the one
        // path that writes a file.
        Check("capturing the System page without --sample is refused",
              AccountFixture.CaptureRefusal("System", sampled: false) is not null);
        Check("and is refused however it is spelled",
              AccountFixture.CaptureRefusal("system", sampled: false) is not null);
        Check("with --sample it proceeds",
              AccountFixture.CaptureRefusal("System", sampled: true) is null);
        Check("and no other page is touched by the rule",
              AccountFixture.CaptureRefusal("General", sampled: false) is null &&
              AccountFixture.CaptureRefusal(null, sampled: false) is null);

        IReadOnlyList<TooltipCli.Variant> tips = TooltipCli.Catalogue;
        string[] deadTips = tips.Where(v => TooltipText.Compose(v.Build(1_800_000_000)).Length == 0)
                                .Select(v => v.Name).ToArray();
        Check($"every tooltip variant composes text ({tips.Count})", deadTips.Length == 0,
              string.Join(", ", deadTips));

        // The cap is the tooltip's whole design constraint: past it Windows truncates, and the line it
        // would cut is the one carrying the refresh time.
        string[] overCap = tips
            .Where(v => TooltipText.Compose(v.Build(1_800_000_000)).Length > TooltipText.Cap)
            .Select(v => $"{v.Name} ({TooltipText.Compose(v.Build(1_800_000_000)).Length})").ToArray();
        Check($"and none of them exceeds the {TooltipText.Cap}-char cap", overCap.Length == 0,
              string.Join(", ", overCap));

        SkillCatalogue(toasts, tips);
    }

    /// <summary>
    /// T268. The report used to label a summary row <c>Measured files</c> while, nine lines below, a column
    /// headed <c>Measured</c> meant the median startup context read from transcripts — one word, two labels,
    /// one document, and the prose defined only the second. Read on the generated document, where a person
    /// meets it: the header of the same scan said 1021 and that row said 881, both right and neither saying
    /// which was which.
    ///
    /// <para>What is held is narrower than the first draft of it, and the narrowing is the interesting part.
    /// "No summary row may be labelled with a word the Projects table uses as a column" sounds like the
    /// invariant and is not: written that way the check fires on <c>Findings</c>, which is the same quantity
    /// at two scopes — the machine's total and one project's — and misleads nobody. Same word for the
    /// <em>same</em> thing is fine; same word for two different things is the defect, and no regex can tell
    /// those apart. So the assertion names the word that was actually wrong, and the positive rows carry the
    /// rest: the two counts exist, by their own names, and are free to differ.</para>
    /// </summary>
    private static void ReportLabels()
    {
        var scan = new ContextScan { ScannedUtc = DateTime.UtcNow, FilesWalked = 1021, Truncated = true };
        scan.Shared.Add(new ContextSource { Path = @"C:\u\CLAUDE.md", Kind = ContextKind.UserInstructions });

        string doc = ContextReport.Build(scan, Array.Empty<Finding>(), null,
                                        new Dictionary<string, ContextDebt>(), 32_000, DateTime.Now);

        Check("the summary reports what the walk visited, by that name",
              doc.Contains("| Files walked | 1021 |", StringComparison.Ordinal));
        Check("and what it kept, by that name — one source here, and they are allowed to differ",
              doc.Contains("| Sources kept | 1 |", StringComparison.Ordinal));

        // Scoped to the Summary block: read over the whole document this also matches the Projects table's
        // own header row and reports it colliding with itself, which is how the first draft of this check
        // failed on `Project`.
        int from = doc.IndexOf("## Summary", StringComparison.Ordinal);
        int to = doc.IndexOf("## Projects", StringComparison.Ordinal);
        if (!Check("the summary block is readable", from >= 0 && to > from, $"{from}..{to}")) return;

        string[] rowLabels = Regex.Matches(doc[from..to], @"(?m)^\| ([A-Z][^|]*?) \| ")
                                  .Select(m => m.Groups[1].Value).ToArray();
        if (!Check($"the summary's rows are readable ({rowLabels.Length})", rowLabels.Length >= 6,
                   string.Join(" / ", rowLabels)))
            return;

        // The word the Projects table owns, and the one this row used to steal.
        string[] stolen = rowLabels.Where(r => r.Contains("Measured", StringComparison.Ordinal)).ToArray();
        Check($"no summary row calls itself Measured — the column owns that word ({rowLabels.Length} rows)",
              stolen.Length == 0,
              $"{string.Join(", ", stolen)} — and the column nine lines down means something else");
    }

    /// <summary>
    /// T267. The cleanup prompt is the only text this app writes that <b>leaves</b> it — clipboard, then
    /// Claude Code, then something that edits files. So what it carries is a property worth holding, and the
    /// one it was missing is that the list may be partial: a capped walk means findings never reached, and
    /// the caveat was printed in the scan header, above the <c>#</c>, which is outside what a person copies.
    ///
    /// <para>Both directions, because a caveat that is always there is noise a reader learns to skip. And
    /// the two promises the class doc already makes are asserted with it — no file contents, and it asks
    /// before deleting — since those are the reasons this text is safe to hand to an agent at all.</para>
    /// </summary>
    private static void CleanupPrompt()
    {
        var findings = new List<Finding>
        {
            new("eager-large", RuleSeverity.High, "acme/atlas", "AGENTS.md is 23 KB", "Trim it",
                @"D:\acme\atlas\AGENTS.md"),
        };
        var none = Array.Empty<ContextSource>();

        string capped = ContextPrompt.Build("acme/atlas", findings, none, 0, truncated: true);
        string whole = ContextPrompt.Build("acme/atlas", findings, none, 0, truncated: false);

        Check("a capped scan says so inside the text, not above it",
              capped.Contains("floor, not a total", StringComparison.Ordinal));
        Check("and tells the reader to say the list is partial before acting on it",
              capped.Contains("say that this list is partial", StringComparison.Ordinal));
        Check("an uncapped scan says neither — a caveat that is always there is one nobody reads",
              !whole.Contains("floor, not a total", StringComparison.Ordinal)
              && !whole.Contains("partial", StringComparison.Ordinal));

        // The two promises the prompt exists under, on both shapes: this text is handed to something that
        // edits files, and these are the reasons that is safe.
        foreach ((string what, string text) in new[] { ("capped", capped), ("whole", whole) })
        {
            Check($"{what}: it says what was measured and what was not",
                  text.Contains("never file contents", StringComparison.Ordinal));
            Check($"{what}: and it asks before anything is deleted or moved",
                  text.Contains("Do not delete or move any file without", StringComparison.Ordinal)
                  && text.Contains("showing me the plan first", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// T264. The overage preview exists because an account past its included quota is a state no machine can
    /// be put into on demand — so a preview that quietly declines to produce it is worse than none, and that
    /// is what it had become. Its guard was <c>ExtraCurve.Count &gt; 0</c>, and the list is non-empty on any
    /// machine whose history holds a <b>measured zero</b> for overage, which is most of them (T179 writes one
    /// whenever the header was present). So the demo bailed, the real series scaled to nothing, the chart's
    /// <c>hasExtra</c> was false, and <c>--stats overage</c> drew an ordinary week with no second axis. It is
    /// why <c>docs/statistics-overage.png</c> could not be re-taken: it was captured before that history
    /// existed.
    ///
    /// <para>Asserted on the seam itself rather than through a rendered page: the three states it has to
    /// tell apart are a curve of measured zeros, a curve with a real maximum, and no window at all — and
    /// the first is the one every machine is in and the one that was wrong.</para>
    /// </summary>
    private static void OveragePreview()
    {
        static WindowPace Week()
        {
            var w = new WindowPace { HasWindow = true, WindowSeconds = 7 * 86400, ElapsedSeconds = 4 * 86400 };
            w.Util = 1.0;
            return w;
        }

        // The state every machine with overage headers is in: readings on file, every one of them zero.
        WindowPace measuredZeros = Week();
        for (int i = 0; i <= 10; i++) measuredZeros.ExtraCurve.Add((i / 10.0 * 0.57, 0.0));
        StatisticsPage.FillDemoOverage(measuredZeros);
        Check("a curve of measured zeros is filled, so the preview draws the state it is for",
              measuredZeros.ExtraMax > 0, $"ExtraMax {measuredZeros.ExtraMax}, {measuredZeros.ExtraCurve.Count} points");
        Check("and the zeros are replaced rather than drawn under it",
              measuredZeros.ExtraCurve.All(p => p.val > 0 || p.frac > 0),
              "a flat line of real zeros left in front of the demo climb means two things at once");

        // A real spell must be left exactly alone — the seam is for the absence, not for overwriting.
        WindowPace real = Week();
        real.ExtraCurve.Add((0.30, 0.10));
        real.ExtraCurve.Add((0.60, 0.42));
        real.ExtraMax = 0.42;
        StatisticsPage.FillDemoOverage(real);
        Check("a real overage series is untouched", real.ExtraCurve.Count == 2 && real.ExtraMax == 0.42,
              $"{real.ExtraCurve.Count} points, max {real.ExtraMax}");

        // No window is nothing to draw on, and inventing a series over it would be a chart about nothing.
        var noWindow = new WindowPace();
        StatisticsPage.FillDemoOverage(noWindow);
        Check("and a window that does not exist gets no series",
              noWindow.ExtraCurve.Count == 0 && noWindow.ExtraMax == 0);
    }

    /// <summary>
    /// T262. The last named token in this app that guessed. <c>--capture-settings &lt;out&gt; NoSuchPage
    /// --sample</c> wrote a picture of <em>General</em> under the name the caller gave, printed
    /// <c>wrote</c> and exited 0; <c>--main NoSuchDest</c> opened Statistics. Both measured before the fix.
    ///
    /// <para>Swept over <see cref="SettingsPage.Pages"/> and <see cref="MainWindow.Destinations"/>
    /// themselves, so a page or destination added later is covered by having been added — and in both
    /// directions, since a resolver that resolved nothing would satisfy the refusal half and break every
    /// flag that names one.</para>
    ///
    /// <para>The <b>sidebar's own tags are the other end of it</b>: the page names are what
    /// <c>Nav_Click</c> reads off a clicked item, so a name here that the XAML does not carry would select
    /// nothing at all. Asserted against the markup rather than against a second list.</para>
    /// </summary>
    private static void PageNames()
    {
        foreach ((string what, string[] known, Func<string?, string?> resolve) in
                 new (string, string[], Func<string?, string?>)[]
                 {
                     ("settings page", SettingsPage.Pages, SettingsPage.Resolve),
                     ("destination", MainWindow.Destinations, MainWindow.Resolve),
                 })
        {
            Check($"every {what} this build has resolves ({known.Length})",
                  known.All(k => resolve(k) == k),
                  string.Join(", ", known.Where(k => resolve(k) != k)));

            Check($"and it resolves however it was typed — a {what} is not case-sensitive",
                  known.All(k => resolve(k.ToUpperInvariant()) == k && resolve(k.ToLowerInvariant()) == k));

            foreach (string bad in new[] { "NoSuchPage", "Genera", "Sistema", "" })
                Check($"'{bad}' is no {what}, so it resolves to nothing rather than the first one",
                      resolve(bad) is null);

            Check($"and naming no {what} at all is not a refusal — it is a request for the default",
                  resolve(null) is null);
        }

        // The names are only worth anything if the sidebar answers to them.
        Repo("every settings page name is a tag the sidebar carries", PageTags, "src/Ui/SettingsPage.xaml");
    }

    private static void PageTags(string root)
    {
        string xaml = File.ReadAllText(Path.Combine(root, "src/Ui/SettingsPage.xaml"));
        string[] tags = Regex.Matches(xaml, @"Tag=""([A-Za-z]+)""").Select(m => m.Groups[1].Value)
                             .Distinct(StringComparer.Ordinal).ToArray();

        if (!Check("the settings markup is readable", tags.Length >= 6,
                   $"{tags.Length} Tag= values — the check cannot read what it compares"))
            return;

        string[] missing = SettingsPage.Pages.Where(p => !tags.Contains(p, StringComparer.Ordinal)).ToArray();
        Check($"every page name is a sidebar tag ({SettingsPage.Pages.Length})", missing.Length == 0,
              $"{string.Join(", ", missing)} — nameable on the command line, and the sidebar selects nothing");
    }

    /// <summary>
    /// T261. A read-out that could not start says so <em>and</em> exits 1 — it used to say so and exit 0,
    /// which a script driving it reads as a scan that simply found nothing.
    ///
    /// <para>The interesting half is the boundary, and it is asserted here because it is what makes the
    /// rule safe to apply at all: a partial answer must NOT become a refusal. Measured on the types
    /// themselves — <see cref="ContextScan.Error"/> is only ever set on a scan that returned immediately,
    /// while <see cref="UsageEvidence"/> carries <c>Complete</c> beside its <c>Error</c> precisely because
    /// it can finish with holes, and <c>ContextCli</c> prints that one as a note and carries on.</para>
    ///
    /// <para>The exit code is restored afterwards: this runs inside a suite whose own exit code is the
    /// thing being reported, and a check that fails the run by demonstrating a failure is a check that
    /// cannot pass.</para>
    /// </summary>
    private static void ReadOutExit()
    {
        int before = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            Check("a read-out with nothing wrong neither prints nor marks the run",
                  !ReadOut.Failed(null) && Environment.ExitCode == 0);

            Environment.ExitCode = 0;
            bool said = ReadOut.Failed("not found: D:\\nope");
            Check("and one that could not start says so and exits 1",
                  said && Environment.ExitCode == 1, $"said={said} code={Environment.ExitCode}");
        }
        finally { Environment.ExitCode = before; }

        // The boundary, on the types rather than on a promise about them. A scan carries no way to be
        // partial, so treating its Error as a refusal is total; evidence does, so its Error is a note.
        Check("a scan has no partial form, so its error is always a refusal",
              typeof(ContextScan).GetProperty("Complete") is null
              && new ContextScan { Error = "x" }.Projects.Count == 0);
        Check("and evidence does have one, which is why its error stays a note",
              typeof(UsageEvidence).GetProperty("Complete") is not null);
    }

    /// <summary>
    /// The sentence <c>card=</c> prints about what it framed (T375). The resolution itself needs a laid-out
    /// window and is exercised by running the flag; what a check can hold is the part that decides whether
    /// a reader trusts the picture — <b>whole or partial</b>, and the boundary between them.
    ///
    /// <para>The boundary is where this would go wrong quietly. A card laid out to exactly the viewport
    /// height comes back off the layout pass a hair over it, and a strict comparison would report every
    /// full-height card as partial — a warning that fires always is one nobody reads, which is the same
    /// defect as a skip that always fires (T161).</para>
    /// </summary>
    private static void Framing()
    {
        Check("a card shorter than the viewport is framed whole",
              new SettingsPage.Framing("Card", 100, 280, 475) is { Fits: true });
        Check("and one taller than it is not",
              new SettingsPage.Framing("Card", 100, 607, 475) is { Fits: false });
        // The measured pair from the surface this was built for: 607dip of card against a 475dip viewport.
        Check("a partial frame says how much of the element is in the picture",
              new SettingsPage.Framing("LinkCard", 1174, 607, 475).Line
                  is "capture-frame: LinkCard scroll=1174 element=607dip viewport=475dip PARTIAL 78% of it",
              new SettingsPage.Framing("LinkCard", 1174, 607, 475).Line);
        Check("and a whole one says so instead of a percentage",
              new SettingsPage.Framing("LinkHeader", 1149, 17, 475).Line.EndsWith("WHOLE", StringComparison.Ordinal));
        // Exactly at the viewport, and a hair over it: the first must be whole or the warning fires on
        // every full-height card, and the second must not be, or it never fires at all.
        Check("a card exactly the viewport's height is whole, layout jitter included",
              new SettingsPage.Framing("Card", 0, 475, 475) is { Fits: true }
              && new SettingsPage.Framing("Card", 0, 475.4, 475) is { Fits: true });
        Check("and a card a dip taller is not",
              new SettingsPage.Framing("Card", 0, 476, 475) is { Fits: false });
    }

    /// <summary>
    /// An <c>x:Name</c> is a control's identity to everything outside the compiler — UI Automation, a
    /// screen reader, <c>Check-Interaction.ps1</c> — and WPF scopes it per XAML file, so two pages could
    /// each call a control <c>ProfileCombo</c> and the C# in both would still compile. An id lookup then
    /// has two candidates and <c>FindFirst</c> returns whichever the tree reaches first, which depends on
    /// which destinations have been built: a page is built on its first visit and then kept collapsed, so
    /// the answer changes with the route a run took.
    ///
    /// <para>Nothing was wrong on screen when T192 was written, and that was the defect — <c>-Case Names</c>
    /// read the Statistics picker <em>before</em> navigating to Settings, and a comment saying so was the
    /// whole guarantee. The first case that visited Settings and then looked the picker up by id would have
    /// driven the other control and gone on passing. So the rule is asserted here rather than remembered:
    /// <b>an <c>x:Name</c> is unique across the app, not per XAML file.</b></para>
    ///
    /// <para>The types are <em>derived</em>, never listed: a page added later is covered without an edit
    /// here, because a hardcoded list's failure mode is silently not checking the thing it was written
    /// for (§XV.3). A XAML-backed type is the one that implements <c>IComponentConnector</c>, and its
    /// generated fields are exactly the named elements — <c>internal</c> and a <see cref="DependencyObject"/>,
    /// which is what separates them from the page's own hand-written state.</para>
    /// </summary>
    private static void AutomationIds()
    {
        Type connector = typeof(System.Windows.Markup.IComponentConnector);
        List<Type> pages = typeof(SelfTestCli).Assembly.GetTypes()
            .Where(t => t.IsClass && connector.IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        // The guard first, on the precondition and not on a weaker form of the property: reflection
        // yielding nothing would report a clean run over zero controls, which is §XV.3's defect again.
        if (!Check("every XAML-backed window and page is reachable by reflection", pages.Count >= 5,
                   $"found {pages.Count}: {string.Join(", ", pages.Select(p => p.Name))}"))
            return;

        Dictionary<string, List<string>> owners = new(StringComparer.Ordinal);
        foreach (Type page in pages)
            foreach (FieldInfo f in page.GetFields(BindingFlags.Instance | BindingFlags.NonPublic |
                                                   BindingFlags.Public | BindingFlags.DeclaredOnly))
                if (f.IsAssembly && typeof(System.Windows.DependencyObject).IsAssignableFrom(f.FieldType))
                {
                    if (!owners.TryGetValue(f.Name, out List<string>? on)) owners[f.Name] = on = new();
                    on.Add(page.Name);
                }

        if (!Check("and the named controls in them are found", owners.Count >= 50,
                   $"{owners.Count} named controls across {pages.Count} types — too few to be the real set"))
            return;

        string[] shared = owners.Where(kv => kv.Value.Count > 1)
                                .Select(kv => $"{kv.Key} ({string.Join(" + ", kv.Value)})")
                                .OrderBy(s => s, StringComparer.Ordinal).ToArray();
        Check($"no x:Name is carried by two controls ({owners.Count} across {pages.Count} types)",
              shared.Length == 0,
              shared.Length == 0 ? "" : $"{shared.Length} shared — {string.Join("; ", shared)}");

        InteractionIds(owners);
        InteractionCasesDocumented();
    }

    /// <summary>
    /// T227. Every toast draws its emoji as a flat black outline, and the task was to fix that with a font
    /// on the one <c>TextBlock</c> that carries them. <b>Measured, that fix does not exist.</b> Rendering the
    /// six glyphs the cards use under WPF's own text stack and under GDI+, in <c>Segoe UI Emoji</c> itself,
    /// produced no coloured pixel in any of the twelve — and a real <c>--capture-toast unexpected</c> with the
    /// font named came back with the same black popper. WPF draws the font's monochrome base layer because
    /// its text pipeline has no colour-font support at all, and GDI+ has none either; colour would need
    /// Direct2D interop or seven raster assets, which is a non-goal.
    ///
    /// <para><b>What is left is worth keeping, and it is what this checks.</b> Naming the family takes font
    /// <em>linking</em> out of the picture: the run used to resolve against whichever family WPF reached
    /// first from a stack containing none of these codepoints, and now comes from one that carries all of
    /// them. So the property is that every card's glyph is really in the font the card names — a card that
    /// picks a codepoint <c>Segoe UI Emoji</c> lacks would draw a tofu box, which is the same kind of defect
    /// as the black popper and the same kind a capture certifies without complaint.</para>
    ///
    /// <para>Asked of the typeface rather than of pixels, because a missing glyph and a present one both
    /// render ink. The precondition is the other half: the family the glyph used to inherit must still be
    /// missing them, or naming the emoji font is doing nothing and this would pass either way.</para>
    /// </summary>
    private static void ToastGlyphs()
    {
        // From the live notifier's own content, not from a literal here: the reset cards pick their glyph
        // per event kind, and probing one no card uses would answer about the wrong codepoint.
        string[] glyphs = new[] { "7d", "5h" }
            .SelectMany(k => Enum.GetValues<BurnTracker.ResetKind>()
                .Select(kind => TrayContext.ResetToastContent(
                    k, new BurnTracker.ResetEvent(kind, 0.8, 0.0, (long)Now, (long)Now + 3600), (long)Now).emoji))
            .Concat(new[] { "📇", "🧾", "💻", "🚫" })   // the four cards that name theirs at the call site
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (!Check($"the cards' glyphs are reachable ({glyphs.Length} distinct)", glyphs.Length >= 5,
                   string.Join(" ", glyphs)))
            return;

        string[] inherited = glyphs.Where(g => Maps(ToastWindow.BodyFont, g)).ToArray();
        if (!Check("the card's text font still carries none of them — so naming the emoji font does something",
                   inherited.Length == 0,
                   $"{string.Join(" ", inherited)} — already covered, and the fallback was never the issue"))
            return;

        string[] missing = glyphs.Where(g => !Maps(ToastWindow.EmojiFont, g)).ToArray();
        Check($"and the font the card names carries every one of them ({glyphs.Length})", missing.Length == 0,
              $"{string.Join(" ", missing)} — would draw as a tofu box, and a capture would not say so");
    }

    /// <summary>
    /// T225. The System page's project count is presented as a number of directories and was a number of
    /// keys in a file this app does not write. On a real machine the two differed by two: <c>d:/Git/x</c>
    /// and <c>D:/Git/x</c> both recorded, because Windows paths are case-insensitive and Claude Code writes
    /// whatever spelling the shell used.
    ///
    /// <para>Asserted twice over, because the interesting failure is not the arithmetic — it is the one line
    /// in <see cref="ClaudeAccount.Read(string)"/> that decides whether the fold is used at all. So the fold
    /// is checked against the exact shape that was measured, and then a synthetic <c>.claude.json</c> of that
    /// shape is read back through <c>Read</c> itself, in a throwaway tree.</para>
    /// </summary>
    private static void ProjectCount()
    {
        Check("two spellings of one drive letter are one directory",
              ClaudeAccount.CountDirectories(new[] { @"d:\Git\x", @"D:\Git\x" }) == 1);
        Check("and the whole measured shape folds to what the disk holds",
              ClaudeAccount.CountDirectories(new[]
              {
                  "d:/Git/x", "D:/Git/x", "d:/Git/y", "D:/Git/y", "C:/other",
              }) == 3);
        Check("distinct directories are still distinct — the fold is case, not a merge",
              ClaudeAccount.CountDirectories(new[] { @"D:\a", @"D:\b", @"D:\c" }) == 3);
        Check("and an empty map is zero, not one",
              ClaudeAccount.CountDirectories(Array.Empty<string>()) == 0);

        // The other end of the same claim: a file of the measured shape, read the way the page reads one.
        // Five keys, three folders — and the reading has to be the second number.
        Temp(root =>
        {
            File.WriteAllText(Path.Combine(root, ".claude.json"),
                """
                {
                  "installMethod": "native",
                  "projects": {
                    "d:/Git/x": {}, "D:/Git/x": {},
                    "d:/Git/y": {}, "D:/Git/y": {},
                    "C:/other": {}
                  }
                }
                """);
            ClaudeInfo read = ClaudeAccount.Read(root);
            Check("a config file of that shape reads as three projects, not five",
                  read.ProjectCount == 3, $"got {read.ProjectCount}");
        });
    }

    // ---------------------------------------------------------------- Blocks F and G: one number convention

    /// <summary>
    /// No <c>TextTrimming</c> sits on an element that is free to size to its own content, where it can
    /// never fire (T338, widened by T355).
    ///
    /// <para><b>The property is not about any one container.</b> Trimming fires when the element is
    /// measured against a constraint narrower than its text. T338 found one way to escape that — a
    /// horizontal <c>StackPanel</c> measures its children at <em>infinite</em> width, so a
    /// <c>TextBlock</c> inside one always reports the width of its longest line, never discovers it is
    /// too wide, and never trims, while whatever contains the panel clips mid-word. The check written
    /// for it asked "is this inside a horizontal <c>StackPanel</c>", which is the instance and not the
    /// rule.</para>
    ///
    /// <para><b>The second way is the element opting out.</b> A <c>HorizontalAlignment</c> of
    /// <c>Left</c>, <c>Right</c> or <c>Center</c> makes an element size to its content whatever holds
    /// it — only the default <c>Stretch</c> takes the parent's width. So a right-aligned <c>TextBlock</c>
    /// in a fixed-width <c>Grid</c> column overflows sideways out of the cell with the ellipsis never
    /// firing, which is the same defect one container over. T346 shipped exactly that in the Sessions
    /// money heading; <c>--selftest</c> stayed green and the capture showed the heading running into
    /// the column beside it. The fix there was <c>Stretch</c> plus <c>TextAlignment</c>, which is what
    /// gives the column back its say.</para>
    ///
    /// <para>Vertical alignment is deliberately not asked about: <c>TextTrimming</c> is a horizontal
    /// property, and an element free to size to its content in height trims nothing.</para>
    ///
    /// <para>Nesting is what makes this worth scanning rather than reading — T338's trimmed line was two
    /// levels below the horizontal panel, in an inner stack, which is exactly where a reviewer stops
    /// looking. So the scan tracks depth rather than matching adjacent lines.</para>
    /// </summary>
    private static void TrimmingThatCannotFire() =>
        Repo("no TextTrimming sits where it can never fire", root =>
        {
            var bad = new List<string>();
            foreach (string path in Directory.GetFiles(Path.Combine(root, "src"), "*.xaml",
                                                       SearchOption.AllDirectories))
            {
                string[] lines = File.ReadAllLines(path);
                // Depth of the innermost open horizontal StackPanel, or -1 for none.
                var open = new Stack<bool>();
                int horizontal = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.StartsWith("<StackPanel", StringComparison.Ordinal))
                    {
                        bool isHorizontal = line.Contains("Orientation=\"Horizontal\"", StringComparison.Ordinal);
                        bool selfClosed = line.EndsWith("/>", StringComparison.Ordinal);
                        if (!selfClosed) { open.Push(isHorizontal); if (isHorizontal) horizontal++; }
                    }
                    else if (line.StartsWith("</StackPanel>", StringComparison.Ordinal) && open.Count > 0)
                    {
                        if (open.Pop()) horizontal--;
                    }
                    else if (line.Contains("TextTrimming", StringComparison.Ordinal))
                    {
                        // A width of its own is the exception, and it is a real one: a TextBlock with
                        // Width or MaxWidth is measured against that, so trimming does fire. It is the
                        // weaker fix — a number chosen once is wrong at every other window size — but
                        // it is correct where the width is not proportional, which is where the scan
                        // found one and was wrong about it.
                        string element = Element(lines, i);
                        if (element.Contains("MaxWidth=", StringComparison.Ordinal) ||
                            element.Contains("Width=", StringComparison.Ordinal)) continue;

                        // Anything but Stretch sizes to content, so the parent's width never reaches it.
                        // Absent means Stretch, which is the constrained case and the common one.
                        Match align = HorizontalAlignmentAttr.Match(element);
                        bool sizesToContent = align.Success
                            && !align.Groups[1].Value.Equals("Stretch", StringComparison.Ordinal);

                        if (horizontal == 0 && !sizesToContent) continue;
                        string why = horizontal > 0
                            ? (sizesToContent ? "in a horizontal StackPanel and " + align.Value : "in a horizontal StackPanel")
                            : align.Value;
                        bad.Add($"{Path.GetFileName(path)}: {why} — {line[..Math.Min(52, line.Length)]}");
                    }
                }
            }
            Check($"no TextTrimming sits on an element free to size to its content ({bad.Count})",
                  bad.Count == 0,
                  $"{string.Join("; ", bad)} — trimming is measured against a constraint, and neither a "
                  + "horizontal StackPanel (which measures at infinite width) nor a non-Stretch "
                  + "HorizontalAlignment (which sizes to content) gives it one, so the text overflows "
                  + "and the container clips it instead (T338, T355). Use Stretch plus TextAlignment, "
                  + "or give the element its own Width/MaxWidth.");
        }, "src");

    /// <summary>The element's own horizontal alignment, where it states one. A separate field so the
    /// pattern is compiled once rather than per element scanned.</summary>
    private static readonly Regex HorizontalAlignmentAttr =
        new("HorizontalAlignment=\"([A-Za-z]+)\"", RegexOptions.Compiled);

    /// <summary>
    /// The whole element the line at <paramref name="at"/> belongs to, so an attribute on another line of
    /// the same tag counts — XAML wraps, and a scan that reads one line at a time answers about half a tag.
    ///
    /// <para><b>By index, not by text (T355).</b> It used to take the line's text and find it with
    /// <c>Array.FindIndex</c>, which returns the <em>first</em> line that reads the same — and
    /// <c>TextTrimming="CharacterEllipsis"</c> sits alone on four identical lines of
    /// <c>StatisticsPage.xaml</c>. So a scan of the fourth reconstructed the first, and answered about an
    /// element the reader was not looking at. Found by injecting a defect the widened check should have
    /// caught and watching it stay green: the element it examined had no <c>HorizontalAlignment</c>
    /// because it was not the element with the defect. It cuts the other way too — the
    /// <c>Width</c>/<c>MaxWidth</c> exemption could be granted from a different tag entirely.</para>
    /// </summary>
    private static string Element(string[] lines, int at)
    {
        if (at < 0 || at >= lines.Length) return "";
        int from = at;
        while (from > 0 && !lines[from].TrimStart().StartsWith('<')) from--;
        var sb = new StringBuilder();
        for (int i = from; i < lines.Length; i++)
        {
            sb.Append(lines[i].Trim()).Append(' ');
            if (lines[i].Contains("/>", StringComparison.Ordinal) ||
                lines[i].TrimEnd().EndsWith('>')) break;
        }
        return sb.ToString();
    }
}
