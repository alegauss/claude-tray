using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeTray;

/// <summary>
/// Part of <see cref="SelfTestCli"/> — accounts, auto-follow, and the script that links two profiles.
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
    /// T172: whose numbers the icon draws and which profile a new session would start in are two
    /// questions, and the menu marks them only when the answers differ. The states that matter — the
    /// variable naming the *other* profile, or a folder no profile covers — are precisely the ones the
    /// machine running this is not in, so they are driven through
    /// <see cref="EnvironmentProfile.SelectedIn"/> rather than waited for.
    /// </summary>
    private static void EffectiveProfile()
    {
        const string work = @"C:\Users\x\.claude-work";
        const string home = @"C:\Users\x\.claude";
        List<ClaudeInfo> profiles = new()
        {
            new ClaudeInfo { Label = "Personal", ConfigDir = home, IsDefault = true },
            new ClaudeInfo { Label = "Work", ConfigDir = work },
        };

        Check("the variable's dir names the profile that owns it",
              EnvironmentProfile.SelectedIn(profiles, work)?.Label == "Work");
        Check("and the default dir names the default profile",
              EnvironmentProfile.SelectedIn(profiles, home)?.Label == "Personal");

        // The spelling half: a variable set by hand carries a trailing slash or a relative form, and a
        // reading that split one profile into two would report a disagreement that does not exist.
        Check("a trailing separator is the same profile, not an unknown folder",
              EnvironmentProfile.SelectedIn(profiles, work + @"\")?.Label == "Work");
        Check("and so is the other case of the same path",
              EnvironmentProfile.SelectedIn(profiles, work.ToUpperInvariant())?.Label == "Work");

        // The state with no entry to hang a mark on — something other than the tray set the variable.
        Check("a dir no profile covers selects none of them",
              EnvironmentProfile.SelectedIn(profiles, @"C:\Users\x\.claude-elsewhere") is null);
        Check("and an empty profile list answers null rather than throwing",
              EnvironmentProfile.SelectedIn(new List<ClaudeInfo>(), work) is null);

        // What the menu actually decides on: the mark appears only on a real divergence.
        Check("agreement is silent, disagreement is not",
              EnvironmentProfile.SelectedIn(profiles, home)?.ConfigDir == profiles[0].ConfigDir
              && EnvironmentProfile.SelectedIn(profiles, work)?.ConfigDir != profiles[0].ConfigDir);

        // Unset is not "no profile": a bare `claude` lands in ~/.claude, so the honest reading of an
        // absent variable is the default profile — not a blank the menu would have nothing to say about.
        Check("an unset variable reads as the default config dir",
              ClaudeAccount.SamePath(EnvironmentProfile.EffectiveConfigDir(),
                                     EnvironmentProfile.Current() ?? ClaudeAccount.HomeConfigDir));

        // T173: what a queued write reports back. Driven on the outcome itself, because the states worth
        // asserting are a write that threw and a write that silently did not take, and manufacturing
        // either on this machine means rewriting the developer's own CLAUDE_CONFIG_DIR.
        Check("a value that reads back as itself landed",
              new EnvironmentProfile.WriteOutcome(work, work, null, true).Landed);
        Check("and a value that reads back as something else did not",
              !new EnvironmentProfile.WriteOutcome(work, home, null, true).Landed);
        Check("a thrown write never counts as landed, whatever the variable says",
              !new EnvironmentProfile.WriteOutcome(work, work, "access denied", true).Landed);
        // Removing the variable is the "select ~/.claude" case, where null is the intended value — and
        // an absent variable reads back as null, not as an empty string, on one route and not the other.
        Check("removing the variable lands when it reads back absent",
              new EnvironmentProfile.WriteOutcome(null, null, null, true).Landed
              && new EnvironmentProfile.WriteOutcome(null, "", null, true).Landed);
        Check("and does not land while the old value is still there",
              !new EnvironmentProfile.WriteOutcome(null, work, null, true).Landed);
        // A no-op is a landed outcome — the machine already says this — and still reports, so a caller
        // asking "did my choice reach the machine?" is never met with silence.
        Check("nothing to write is still an answer, and a landed one",
              new EnvironmentProfile.WriteOutcome(work, work, null, false) is { Wrote: false, Landed: true });
    }

    /// <summary>
    /// Auto-follow's one refusal that is about the evidence rather than about the profile (T365): two
    /// config dirs behind one <c>projects</c> tree report the same last turn, so neither reading says
    /// which of them is being worked in.
    ///
    /// <para>Two halves, and they are separate on purpose. The rule is driven over synthetic readings,
    /// where the collision can be posed exactly — including the shape that actually moved the icon, a
    /// second reading of one directory taken a second later than the first. The resolver is driven over
    /// a real junction under <paramref name="root"/>, because whether Windows follows a link is not
    /// something a test can assert by restating it.</para>
    /// </summary>
    private static void Follow(string root)
    {
        const double Now = 1_800_000_000;
        static ClaudeInfo P(string label) =>
            new() { Label = label, Auth = AuthMethod.Subscription, HasCredentialsFile = true };

        static List<ProfileActivity.Reading> Readings(params (string Label, double Turn, string Tree)[] rows)
        {
            var list = new List<ProfileActivity.Reading>();
            foreach ((string label, double turn, string tree) in rows)
                list.Add(new ProfileActivity.Reading(P(label), turn, Followable: true, tree));
            return ProfileActivity.MarkShared(list);
        }

        // The ordinary machine: two profiles, two directories, and the newer turn takes the icon.
        List<ProfileActivity.Reading> apart = Readings(
            ("Personal", Now - 600, @"C:\Users\x\.claude\projects"),
            ("Work", Now - 60, @"C:\Users\x\.claude-work\projects"));
        Check("separate trees still follow the profile that just had a turn",
              ProfileActivity.Pick(apart, Now, 0)?.Label == "Work",
              ProfileActivity.Pick(apart, Now, 0)?.Label ?? "nobody");
        Check("and neither of them is marked as sharing anything",
              apart.All(r => !r.SharesTree));

        // The junctioned machine. Equal readings first — what the probe returns when no write lands
        // during it — and then the one that moved the icon: the same directory, walked twice, with a turn
        // landing in between, so the profile scanned *second* looks a second newer than the profile the
        // icon is already on. Under the old rule that was a follow, every time it happened.
        const string shared = @"C:\Users\x\.claude\projects";
        List<ProfileActivity.Reading> tied = Readings(("Personal", Now - 5, shared), ("Work", Now - 5, shared));
        Check("one tree read twice marks both readings, not one of them",
              tied.All(r => r.SharesTree));
        Check("neither is evidence of anybody working",
              tied.All(r => !ProfileActivity.Live(r, Now)));
        Check("so the icon is left where the user put it",
              ProfileActivity.Pick(tied, Now, 0) is null);

        List<ProfileActivity.Reading> raced = Readings(("Personal", Now - 1, shared), ("Work", Now, shared));
        Check("a turn landing between the two walks does not hand the icon to the second one",
              ProfileActivity.Pick(raced, Now, 0) is null,
              ProfileActivity.Pick(raced, Now, 0)?.Label ?? "nobody");

        // A third profile with a tree of its own is unaffected: the refusal is about the pair, not about
        // auto-follow. The sharing pair holds the newest turn here, so a rule that merely demoted them
        // would still answer one of them.
        List<ProfileActivity.Reading> mixed = Readings(
            ("Personal", Now, shared), ("Work", Now, shared),
            ("Client", Now - 120, @"C:\Users\x\.claude-client\projects"));
        Check("a profile with its own tree is still followed while two others share one",
              ProfileActivity.Pick(mixed, Now, 0)?.Label == "Client",
              ProfileActivity.Pick(mixed, Now, 0)?.Label ?? "nobody");

        // One path, two spellings: a config dir typed into the settings file is not the one the
        // filesystem returns, and a comparison that split them would report two trees where there is one.
        Check("the same directory in another case is the same directory",
              Readings(("Personal", Now, shared), ("Work", Now, shared.ToUpperInvariant()))
                  .All(r => r.SharesTree));

        // And the resolver, against a junction Windows actually made. Skipped rather than failed where
        // one cannot be created, since that is the environment refusing and not this rule breaking.
        string real = Path.Combine(root, "real");
        string link = Path.Combine(root, "linked");
        Directory.CreateDirectory(Path.Combine(real, "projects"));
        Directory.CreateDirectory(link);
        if (!Junction(Path.Combine(link, "projects"), Path.Combine(real, "projects")))
        {
            Skip("a junction resolves to the tree it points at", "no junction could be created here");
            return;
        }

        Check("a junction resolves to the tree it points at",
              ClaudeAccount.SamePath(ProfileActivity.ResolvedProjectsDir(link),
                                     ProfileActivity.ResolvedProjectsDir(real)),
              $"{ProfileActivity.ResolvedProjectsDir(link)} vs {ProfileActivity.ResolvedProjectsDir(real)}");
        Check("an ordinary directory resolves to itself",
              ClaudeAccount.SamePath(ProfileActivity.ResolvedProjectsDir(real),
                                     Path.Combine(real, "projects")));
        Check("and a config dir with no projects folder at all is still its own answer",
              !ClaudeAccount.SamePath(ProfileActivity.ResolvedProjectsDir(Path.Combine(root, "never-used")),
                                      ProfileActivity.ResolvedProjectsDir(real)));

        List<ProfileActivity.Reading> onDisk = ProfileActivity.MarkShared(new List<ProfileActivity.Reading>
        {
            new(P("Personal"), Now, true, ProfileActivity.ResolvedProjectsDir(link)),
            new(P("Work"), Now, true, ProfileActivity.ResolvedProjectsDir(real)),
        });
        Check("so a junctioned profile pair follows nobody, read off a real filesystem",
              onDisk.All(r => r.SharesTree) && ProfileActivity.Pick(onDisk, Now, 0) is null);

        // What a surface has to be able to ask (T371): auto-follow's toggle is a control with no effect on
        // a machine where every followable profile shares a tree, and the linking script is what most often
        // puts a machine there. Driven over the same junction, because "the trees are distinct" is the one
        // part of this that a test cannot assert by restating it.
        static ClaudeInfo Dir(string dir, bool followable = true) => new()
        {
            ConfigDir = dir, Auth = followable ? AuthMethod.Subscription : AuthMethod.ApiKey,
            HasCredentialsFile = followable,
        };
        Check("auto-follow can say something while two profiles have trees of their own",
              ProfileActivity.CanFollow(new[] { Dir(real), Dir(Path.Combine(root, "never-used")) }));
        Check("and nothing at all once both of them read one tree",
              !ProfileActivity.CanFollow(new[] { Dir(real), Dir(link) }));
        // The third profile is the reason this is a description and not a disabled control: it makes the
        // setting work again with nothing changed, so the answer has to be recomputed rather than stored.
        Check("a third profile with its own tree brings it back",
              ProfileActivity.CanFollow(new[] { Dir(real), Dir(link), Dir(Path.Combine(root, "own")) }));
        // Followable is part of the question, not a filter applied to the answer: a profile off the
        // subscription has no quota window to read, so its private tree is not what makes this true.
        Check("a profile the icon could never follow does not count as the one with its own tree",
              !ProfileActivity.CanFollow(
                  new[] { Dir(real), Dir(link), Dir(Path.Combine(root, "own"), followable: false) }));

        // The link goes before the tree it sits in does: a recursive delete over a reparse point is the
        // one thing `Temp`'s cleanup cannot do, and a scratch directory left behind is a promise broken.
        try { Directory.Delete(Path.Combine(link, "projects")); } catch { /* the report below says so */ }
    }

    /// <summary>
    /// The linking script, held against the two things it must never stop being (T367): a text that does
    /// not act, and a text that refuses before it half-applies.
    ///
    /// <para>Everything here is asserted over the <em>emitted script</em> rather than over the plan that
    /// produced it, because the script is the artifact — it leaves this app, gets read once and then runs
    /// on somebody's real config dirs, and a correct plan rendered into a command that links
    /// <c>.credentials.json</c> is exactly as bad as a wrong plan. The plan's own table is checked too,
    /// but as the cheaper half.</para>
    ///
    /// <para>The pair is built on disk under <paramref name="root"/> rather than posed synthetically:
    /// <see cref="ProfileLink.For"/> asks the filesystem what is on each side and whether a link is
    /// already there, and a fixture that answered those questions itself would be asserting its own
    /// arithmetic.</para>
    /// </summary>
    private static void LinkScript(string root)
    {
        // The two config dirs, deliberately lopsided: the secondary has entries the primary does not, the
        // primary has entries the secondary does not, and one entry is missing from both. That is the
        // machine §XCI was designed against, where the two sides were 4,553 files and 338 directories.
        string primary = Path.Combine(root, "primary"), secondary = Path.Combine(root, "secondary");
        foreach (string name in new[] { "projects", "file-history", "skills", "plugins" })
        {
            Directory.CreateDirectory(Path.Combine(primary, name));
            Directory.CreateDirectory(Path.Combine(secondary, name));
        }
        Directory.CreateDirectory(Path.Combine(primary, "projects", "d--only-on-the-primary-side"));
        Directory.CreateDirectory(Path.Combine(secondary, "projects", "d--only-on-the-secondary-side"));
        Directory.CreateDirectory(Path.Combine(secondary, "projects", "d--on-both"));
        Directory.CreateDirectory(Path.Combine(primary, "projects", "d--on-both"));
        foreach (string dir in new[] { primary, secondary })
            foreach (string file in new[] { "history.jsonl", "CLAUDE.md", "settings.json", ".claude.json", ".credentials.json" })
                File.WriteAllText(Path.Combine(dir, file), "{}\n");

        // The catalogue first: the four verdicts are not interchangeable, and the two that are refusals
        // are the ones a later edit could quietly turn into links.
        var byName = ProfileLink.Catalogue.ToDictionary(e => e.Name, StringComparer.Ordinal);
        Check($"every catalogue entry is named once ({ProfileLink.Catalogue.Count})",
              byName.Count == ProfileLink.Catalogue.Count);
        Check("the token and the account file are never linked, whatever else is",
              byName[".credentials.json"].Verdict == ProfileLink.Verdict.Never
              && byName[".claude.json"].Verdict == ProfileLink.Verdict.Never);
        Check("settings.json is withheld rather than merged — the union is a decision, not a default",
              byName["settings.json"].Verdict == ProfileLink.Verdict.Withheld);
        Check("plugins is adopted whole, because a per-entry merge leaves absolute installPaths behind",
              byName["plugins"].Verdict == ProfileLink.Verdict.Adopt);
        // A union kind is what tells the script which merge to emit, so a Merge entry without one renders a
        // link with nothing in front of it — the bare rmdir-plus-mklink this task exists to replace.
        Check("every merged entry names how it is unioned, and only merged entries do",
              ProfileLink.Catalogue.All(e =>
                  (e.Verdict == ProfileLink.Verdict.Merge) == (e.Union != ProfileLink.Union.None)));
        // A merged entry's count is the only figure a reader can sanity-check before typing -Apply, so it
        // has to be a count of something named: "338 session uuids" and not "338 items".
        Check("and every merged entry says what it is counting, and only merged entries do",
              ProfileLink.Catalogue.All(e =>
                  (e.Verdict == ProfileLink.Verdict.Merge) == e.Unit.Any));
        // Both forms of it, since the script counts in English with no lang file to reach (T377). Distinct,
        // because a pair that is the same word is a pair somebody wrote to satisfy the type.
        Check("and names it in both its forms, which are two different words",
              ProfileLink.Catalogue.Where(e => e.Unit.Any).All(
                  e => e.Unit.Many.Length > 0 && e.Unit.Many != e.Unit.One),
              string.Join(", ", ProfileLink.Catalogue.Where(e => e.Unit.Any)
                  .Select(e => $"{e.Unit.One}/{e.Unit.Many}")));
        Check("every entry explains itself — the script is read before it is run",
              ProfileLink.Catalogue.All(e => e.Why.Length > 0));
        // The three rows T374 added, and the shape they share with `skills`: one folder each, so the union
        // is by entry name. They were missing for four tasks because neither existed on the machine this
        // was built against, which is the whole argument for the edge report below.
        Check("what a user writes themselves is all merged the same way",
              new[] { "skills", "agents", "commands", "output-styles" }.All(
                  n => byName[n] is { Verdict: ProfileLink.Verdict.Merge, Union: ProfileLink.Union.Entries }));
        // Catalogue and scratch must be disjoint, or an entry is both an opinion and passed over and the
        // edge report becomes incoherent about which of the two it is.
        Check("no entry is both catalogued and dismissed as scratch",
              !ProfileLink.Catalogue.Any(e => ProfileLink.Scratch.Contains(e.Name)),
              string.Join(", ", ProfileLink.Catalogue.Where(e => ProfileLink.Scratch.Contains(e.Name))
                                                     .Select(e => e.Name)));

        ProfileLink.Plan plan = ProfileLink.For(primary, secondary, "Personal", "Work");
        Check("a lopsided pair composes a plan", plan.Error is null, plan.Error ?? "");
        Check("both sides' own paths reach the plan",
              ClaudeAccount.SamePath(plan.PrimaryDir, primary) && ClaudeAccount.SamePath(plan.SecondaryDir, secondary));
        Check("the six entries of a full setup are all acted on",
              plan.Acting.Count() == 6, string.Join(", ", plan.Acting.Select(s => s.Entry.Name)));

        // The count the page renders (T370). It is the figure a reader checks before typing -Apply, so it
        // is asserted against a tree built to a known shape rather than left to "some number appeared".
        ProfileLink.Step projects = plan.Steps.First(s => s.Entry.Name == "projects");
        Check("the union's count is what the secondary side has and the primary does not",
              projects.ToCopy == 1, $"{projects.ToCopy?.ToString() ?? "not counted"} of 1");
        Check("and an entry identical on both sides counts nothing rather than reporting no count",
              plan.Steps.First(s => s.Entry.Name == "skills").ToCopy == 0);
        // Null and zero are different answers and the page renders them differently: history.jsonl is the
        // one entry whose union could only be counted by opening a file full of prompts (§I.1).
        Check("history.jsonl is not counted at all, because counting it would mean reading prompts",
              plan.Steps.First(s => s.Entry.Name == "history.jsonl").ToCopy is null);
        Check("nor is anything adopted, withheld or never linked",
              plan.Steps.Where(s => s.Entry.Union != ProfileLink.Union.Entries).All(s => s.ToCopy is null));

        // The page's own rows, over the same plan. Localized where the script is not, so this asserts the
        // mapping rather than any wording: a verdict and a detail for every entry, and the detail
        // distinguishing "nothing to copy" from "not counted" — which one shared string would not.
        List<LinkPlanRow> rows = LinkPlanRow.From(plan);
        Check($"the page renders one row per catalogue entry ({ProfileLink.Catalogue.Count})",
              rows.Count == ProfileLink.Catalogue.Count);
        Check("every row carries a name, a verdict and a detail",
              rows.All(r => r.Name.Length > 0 && r.Verdict.Length > 0 && r.Detail.Length > 0));
        Check("exactly the acting steps are the rows drawn undimmed",
              rows.Count(r => r.Acts) == plan.Acting.Count());
        Check("and the two the app will never link are never drawn as acting",
              rows.Where(r => r.Name is ".claude.json" or ".credentials.json").All(r => !r.Acts));
        Check("a counted union and an uncounted one do not read the same",
              rows.First(r => r.Name == "skills").Detail != rows.First(r => r.Name == "history.jsonl").Detail);
        // history.jsonl and CLAUDE.md are files, so this plan needs a symlink — which is the only reason
        // the preflight has anything to refuse.
        Check("a plan carrying a file link says so, since that is the only part needing a privilege",
              plan.NeedsSymlink);

        string script = ProfileLink.Script(plan);
        Check("the script is composed from the plan alone, so the same pair renders the same text twice",
              script == ProfileLink.Script(plan));

        // The property everything else is in service of. Every line naming the token or the account file
        // has to be a comment: the script is what runs, and §I.6 is not a thing to re-decide in a merge.
        string[] lines = script.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        string[] acting = lines.Where(l => !l.TrimStart().StartsWith("#", StringComparison.Ordinal)).ToArray();
        Check("no command in the script so much as names the credentials file",
              !acting.Any(l => l.Contains(".credentials.json", StringComparison.OrdinalIgnoreCase)),
              acting.FirstOrDefault(l => l.Contains(".credentials.json", StringComparison.OrdinalIgnoreCase)) ?? "");
        Check("nor the account file that makes a profile a different profile",
              !acting.Any(l => l.Contains(".claude.json", StringComparison.OrdinalIgnoreCase)),
              acting.FirstOrDefault(l => l.Contains(".claude.json", StringComparison.OrdinalIgnoreCase)) ?? "");
        // The reading the withheld decision needs (T373). The two files above are `{}` on both sides, so
        // this is the "already the same" answer — the shape that must not read as a measurement of nothing.
        ProfileLink.Step withheld = plan.Steps.First(s => s.Entry.Name == "settings.json");
        Check("a withheld entry carries the union it is asking you to decide about",
              withheld.Widening is not null);
        Check("and two identical files say so rather than reporting zero of something",
              withheld.Widening is { Error: null, Empty: true });
        Check("nothing else on the plan is asked that question, because nothing else is the user's call",
              plan.Steps.Where(s => s.Entry.Verdict != ProfileLink.Verdict.Withheld)
                        .All(s => s.Widening is null));
        SettingsWidening(root);

        Check("and the withheld settings file is offered as text, never as a command",
              !acting.Any(l => l.Contains("settings.json", StringComparison.Ordinal))
              && lines.Any(l => l.Contains("settings.json", StringComparison.Ordinal)));
        // Named, not merely absent: an entry silently dropped from the script reads as an entry the app
        // has no opinion about, and the next person links it by hand in the wrong order.
        Check("every refusal is still explained in the text",
              ProfileLink.Catalogue.Where(e => e.Verdict is ProfileLink.Verdict.Never or ProfileLink.Verdict.Withheld)
                         .All(e => script.Contains(e.Name, StringComparison.Ordinal)));

        // What the link costs, said before the decision (T371). The plan touches `projects`, so the
        // consequence belongs in this script: after it runs, the two profiles report one last-turn time
        // and auto-follow stops moving the icon between them.
        Check("a plan that links the transcripts says so costs auto-follow", plan.CostsAutoFollow);
        Check("and the script names both profiles in that sentence, not just the fact",
              script.Contains("Follow the active profile", StringComparison.Ordinal)
              && script.Contains("between Personal and", StringComparison.Ordinal));

        // Refuse rather than half-apply. Position is the claim: the throw has to be upstream of the first
        // thing that moves, or the machine without Developer Mode is told after three entries have been
        // relinked and one has not.
        int refusal = script.IndexOf("Developer Mode", StringComparison.Ordinal);
        int firstMove = script.IndexOf("Move-Item", StringComparison.Ordinal);
        Check("the symlink refusal is in the script at all", refusal > 0);
        Check("and it comes before the first thing that moves a directory",
              refusal > 0 && firstMove > refusal, $"refusal at {refusal}, first move at {firstMove}");
        Check("nothing in the script elevates itself",
              !script.Contains("Start-Process", StringComparison.Ordinal)
              && !script.Contains("RunAs", StringComparison.OrdinalIgnoreCase));
        // Not a deletion anywhere in the merge path. The undo comment removes the *link* it made, which is
        // the one Remove-Item here, and it is a comment.
        Check("no command deletes anything — every original is moved aside and kept",
              !acting.Any(l => l.Contains("Remove-Item", StringComparison.Ordinal)
                               || l.Contains("rmdir", StringComparison.OrdinalIgnoreCase)));
        Check("and a bare run acts on nothing: every write is behind the -Apply switch",
              script.Contains("param([switch]$Apply)", StringComparison.Ordinal));
        // ASCII apart from the paths, which are the user's and cannot be normalised. Windows PowerShell
        // 5.1 reads a BOM-less .ps1 as ANSI, so an em-dash in the prose came out as mojibake the first
        // time this was run — harmless in a comment, and the same encoding applied to a `throw` message
        // or a path would not have been. The file is written with a BOM; this keeps a copy-paste of the
        // printed form safe as well, which no encoding choice here can.
        char[] mangled = script.Where(c => c > '~' && !primary.Contains(c) && !secondary.Contains(c))
                               .Distinct().ToArray();
        Check("the script's own prose is ASCII, so no shell can mangle it",
              mangled.Length == 0, new string(mangled));

        // No `(s)` anywhere in a file a user reads before it moves their transcripts (T377). The lang sweep
        // holds the window's strings; this holds the one English surface that has no lang file — and it
        // covers both halves, the text composed here and the counts PowerShell composes at run time,
        // because the second is the half a person actually sees.
        string[] hacked = Ours(script)
            .Where(l => l.Contains("(s)", StringComparison.Ordinal)
                        || l.Contains("(es)", StringComparison.Ordinal)
                        || l.Contains("(ies)", StringComparison.Ordinal))
            .Select(l => l.Trim()).ToArray();
        Check("and counts a thing by naming it, never with (s)", hacked.Length == 0,
              string.Join(" | ", hacked.Take(3)));
        Check("the run-time counts go through the script's own Count, which takes both words",
              script.Contains("function Count($n, $one, $many)", StringComparison.Ordinal)
              && script.Contains("(Count $new.Count $one $many)", StringComparison.Ordinal));

        // Written once, called per entry (T379). Composed per entry this ran to 485 lines for nine of
        // them, with the same twenty-line block and its five-line explanation nine times over — in a file
        // whose whole claim is that it is read before it is run.
        foreach (string fn in new[] { "function MergeEntries(", "function MergeLines(", "function LinkEntry(" })
            Check($"the script defines {fn[9..^1]} exactly once",
                  Occurrences(script, fn) == 1, $"{Occurrences(script, fn)} definition(s)");
        Check("and the plan calls them rather than repeating them",
              Occurrences(script, "\nLinkEntry $link $tgt") + Occurrences(script, "\n  LinkEntry $link $tgt")
                  >= plan.Acting.Count(),
              $"{Occurrences(script, "LinkEntry $link $tgt")} call(s) for {plan.Acting.Count()} acting step(s)");
        // The two sentences that cost a real run to learn survive, once each: which of them a reader skips
        // is the whole reason nine copies was a defect rather than a size.
        Check("the mklink reason and the attribute read are still in the file",
              script.Contains("unprivileged-create flag", StringComparison.Ordinal)
              && script.Contains("does not exist on .NET Framework", StringComparison.Ordinal));
        // A function writing the bare name would count into a copy that dies with it, and the footer and
        // the trap both report that number.
        Check("and every function counts into the script's own total, not a local copy",
              !script.Contains(" $Acted++", StringComparison.Ordinal)
              && Occurrences(script, "$script:Acted++") >= 4,
              $"{Occurrences(script, "$script:Acted++")} script-scoped increment(s)");
        // The singular, which is the case a reader meets most often because the interesting unions are
        // small — and the one `(s)` read worst on.
        Check("one of a thing reads as one of it",
              ProfileLink.Counted(1, ProfileLink.Noun.Of("skill")) == "1 skill"
              && ProfileLink.Counted(2, ProfileLink.Noun.Of("skill")) == "2 skills"
              && ProfileLink.Counted(0, ProfileLink.Noun.Of("skill")) == "0 skills");
        Check("and an irregular noun is spelled, not derived",
              ProfileLink.Counted(1, "entry", "entries") == "1 entry"
              && ProfileLink.Counted(3, "entry", "entries") == "3 entries");

        // The link is made with mklink and not New-Item, and the reason is measured: in PowerShell 5.1
        // New-Item does not pass the unprivileged-create flag, so it fails with "requires administrator
        // privilege" on the very machine whose Developer Mode the preflight just checked for. Asserted
        // because the two lines are far apart and a later tidy-up would read as equivalent.
        Check("links are made with mklink, which honours the Developer Mode the preflight checked",
              script.Contains("mklink", StringComparison.Ordinal)
              && !script.Contains("New-Item -ItemType SymbolicLink", StringComparison.Ordinal));

        // An entry the primary side does not have is skipped rather than linked to nothing. `todos` is not
        // in the catalogue, so this poses it with the one entry neither side was given.
        ProfileLink.Step md = plan.Steps.First(s => s.Entry.Name == "CLAUDE.md");
        Check("an entry present on both sides is acted on", md is { OnPrimary: true, OnSecondary: true });
        File.Delete(Path.Combine(primary, "CLAUDE.md"));
        ProfileLink.Plan gap = ProfileLink.For(primary, secondary, "Personal", "Work");
        Check("an entry the primary side does not have drops out of the plan's acting set",
              gap.Acting.All(s => s.Entry.Name != "CLAUDE.md"));
        Check("and the script says so rather than linking into nothing",
              ProfileLink.Script(gap).Contains("nothing to link into", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(primary, "CLAUDE.md"), "{}\n");

        // The edge (T374): a fixed catalogue has to report what it has no row for, or a reader is told
        // nothing is missing. Both sides contribute, and the two kinds of "not a row" stay apart —
        // scratch is counted, everything else is named.
        Directory.CreateDirectory(Path.Combine(primary, "something-new-claude-code-invented"));
        Directory.CreateDirectory(Path.Combine(secondary, "only-over-here"));
        Directory.CreateDirectory(Path.Combine(primary, "shell-snapshots"));
        Directory.CreateDirectory(Path.Combine(secondary, "paste-cache"));
        Directory.CreateDirectory(Path.Combine(secondary, "projects.pre-link-20260101-000000"));
        ProfileLink.Plan edged = ProfileLink.For(primary, secondary, "Personal", "Work");
        Check("an entry the catalogue has no row for is named, from either side",
              edged.Edge.Unclaimed.Contains("something-new-claude-code-invented")
              && edged.Edge.Unclaimed.Contains("only-over-here"),
              string.Join(", ", edged.Edge.Unclaimed));
        Check("scratch is counted rather than named, which is an opinion and not an absence of one",
              edged.Edge.Ignored >= 2
              && !edged.Edge.Unclaimed.Contains("shell-snapshots")
              && !edged.Edge.Unclaimed.Contains("paste-cache"),
              $"{edged.Edge.Ignored} ignored, unclaimed: {string.Join(", ", edged.Edge.Unclaimed)}");
        // A second run must not report the first one's own leftovers as things nobody has an opinion about.
        Check("and this script's own moved-aside copies are not reported back to the reader",
              !edged.Edge.Unclaimed.Any(n => n.Contains(".pre-link-", StringComparison.Ordinal)),
              string.Join(", ", edged.Edge.Unclaimed));
        Check("nothing in the catalogue turns up as unclaimed",
              !edged.Edge.Unclaimed.Any(n => ProfileLink.Catalogue.Any(
                  e => string.Equals(e.Name, n, StringComparison.OrdinalIgnoreCase))));
        string edgedScript = ProfileLink.Script(edged);
        Check("the script names every unclaimed entry, since a count alone cannot be acted on",
              edged.Edge.Unclaimed.All(n => edgedScript.Contains(n, StringComparison.Ordinal)));
        Check("and says plainly that it has no opinion rather than that they are safe",
              edgedScript.Contains("but because nothing here has an", StringComparison.Ordinal));
        Directory.Delete(Path.Combine(primary, "something-new-claude-code-invented"));
        Directory.Delete(Path.Combine(secondary, "only-over-here"));
        Directory.Delete(Path.Combine(primary, "shell-snapshots"));
        Directory.Delete(Path.Combine(secondary, "paste-cache"));
        Directory.Delete(Path.Combine(secondary, "projects.pre-link-20260101-000000"));

        // The two refusals that are about the arguments, not the filesystem.
        Check("the same directory on both sides is refused rather than linked to itself",
              ProfileLink.For(primary, primary).Error is { Length: > 0 });
        Check("a directory that is not there is refused",
              ProfileLink.For(Path.Combine(root, "nope"), secondary).Error is { Length: > 0 });

        // Idempotence, against a junction Windows actually made — the second run of a script whose first
        // run worked. Skipped where a junction cannot be created, since that is the environment refusing.
        string linked = Path.Combine(secondary, "file-history");
        Directory.Delete(linked);
        if (!Junction(linked, Path.Combine(primary, "file-history")))
        {
            Directory.CreateDirectory(linked);
            Skip("a second run finds the link it made and does nothing", "no junction could be created here");
            return;
        }

        ProfileLink.Plan again = ProfileLink.For(primary, secondary, "Personal", "Work");
        ProfileLink.Step relink = again.Steps.First(s => s.Entry.Name == "file-history");
        Check("a second run finds the link it made and does nothing", relink.AlreadyLinked);
        Check("and the consequence is still ahead of a reader while projects is unlinked",
              again.CostsAutoFollow);

        // The other half of T371's sentence, and the one that keeps it from appearing on every script
        // forever: link `projects` too, and the plan no longer costs anything auto-follow had.
        string tree = Path.Combine(secondary, "projects");
        Directory.Delete(tree, recursive: true);
        if (Junction(tree, Path.Combine(primary, "projects")))
        {
            ProfileLink.Plan shared = ProfileLink.For(primary, secondary, "Personal", "Work");
            Check("once the transcripts are shared, the plan no longer costs auto-follow",
                  !shared.CostsAutoFollow);
            Check("and the sentence about it is gone from the script",
                  !ProfileLink.Script(shared).Contains("Follow the active profile", StringComparison.Ordinal));
            try { Directory.Delete(tree); } catch { /* the report says so */ }
        }
        else
        {
            Directory.CreateDirectory(tree);
            Skip("once the transcripts are shared, the plan no longer costs auto-follow",
                 "no junction could be created here");
        }
        Check("so the already-linked entry is out of the acting set",
              again.Acting.All(s => s.Entry.Name != "file-history"));
        Check("and the script says as much instead of relinking a link",
              ProfileLink.Script(again).Contains("already a link into the Personal profile", StringComparison.Ordinal));
        // The link before the tree it sits in, for `Temp`'s reason: a recursive delete over a reparse point
        // is the one thing its cleanup cannot do.
        try { Directory.Delete(linked); } catch { /* the report says so */ }
    }

    /// <summary>
    /// What unioning two <c>settings.json</c> files would add, which is the whole of the decision T367
    /// refused to make and T373 supplies the evidence for.
    ///
    /// <para>Three properties, and each of them is a way a report about risk stops describing risk. The
    /// two <b>directions</b> are separate, because a union adds to each side and a reader is deciding
    /// about both. <b>Narrowing is counted apart from granting</b>, or twelve rules arriving in
    /// <c>deny</c> read as twelve new capabilities. And <b>unreadable is not zero</b>: a nought presented
    /// as a measurement says "nothing would change" on the one surface whose job is saying what would.
    /// </para>
    /// </summary>
    private static void SettingsWidening(string root)
    {
        string a = Path.Combine(root, "widen-a"), b = Path.Combine(root, "widen-b");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        // `allow` overlaps by one and differs by one each way; `deny` differs one way only; `hooks` exists
        // on one side. Every figure below is therefore a different number, so a reading that conflated two
        // of them could not pass by coincidence.
        File.WriteAllText(Path.Combine(a, "settings.json"), """
            { "permissions": { "allow": ["Bash(git status)", "Read(/src/**)"],
                               "deny": ["Read(/vault/**)"] },
              "hooks": { "PreToolUse": [ { "matcher": "Bash",
                         "hooks": [ { "type": "command", "command": "echo one" },
                                    { "type": "command", "command": "echo two" } ] } ] } }
            """);
        File.WriteAllText(Path.Combine(b, "settings.json"), """
            // a hand-edited settings file carries comments, and a parse refusing them would report
            // "unreadable" for a file Claude Code is using happily
            { "permissions": { "allow": ["Bash(git status)", "WebFetch(domain:example.com)"],
                               "deny": ["Read(/vault/**)", "Read(/secrets/**)"] }, }
            """);

        SettingsUnion.Reading r = SettingsUnion.For(a, b, "settings.json");
        Check("a hand-edited settings file with comments and a trailing comma still parses",
              r.Error is null, r.Error ?? "");
        SettingsUnion.Widening allow = r.Lists.First(w => w.List == "permissions.allow");
        Check("the two directions of the union are reported apart, not as one difference",
              allow.ToPrimary is ["WebFetch(domain:example.com)"] && allow.ToSecondary is ["Read(/src/**)"],
              $"toPrimary=[{string.Join(",", allow.ToPrimary)}] toSecondary=[{string.Join(",", allow.ToSecondary)}]");
        Check("and the entry both files already carry is in neither direction",
              !allow.ToPrimary.Concat(allow.ToSecondary).Contains("Bash(git status)"));

        SettingsUnion.Widening deny = r.Lists.First(w => w.List == "permissions.deny");
        Check("deny is marked as the list where arriving takes capability away", deny.Narrows);
        Check("so granting and narrowing are two figures and not one",
              r.Granting == 2 && r.Narrowing == 1, $"granting={r.Granting}, narrowing={r.Narrowing}");

        Check("a hook event only one side has is reported per event, with a count each side",
              r.Hooks is [{ Event: "PreToolUse", OnPrimary: 2, OnSecondary: 0 }],
              string.Join(", ", r.Hooks.Select(h => $"{h.Event} {h.OnPrimary}/{h.OnSecondary}")));
        // The sentence a reader acts on, held against the numbers above: a hook is a command line, and the
        // emitted comment has to say so rather than counting it beside a path rule.
        string[] lines = SettingsUnion.Lines(r).ToArray();
        Check("the emitted lines are all PowerShell comments",
              lines.All(l => l.TrimStart().StartsWith('#')), lines.FirstOrDefault(
                  l => !l.TrimStart().StartsWith('#')) ?? "");
        Check("and they name the narrowing list as narrowing rather than counting it in",
              lines.Any(l => l.Contains("permissions.deny") && l.Contains("NARROWS", StringComparison.Ordinal)));
        Check("and say what a hook is, since that is the larger of the two decisions",
              lines.Any(l => l.Contains("a hook is a command line that runs", StringComparison.Ordinal)));
        // The `(s)` guard belongs here as well as over the whole script (T377), and finding that out is the
        // lesson: the script-wide scan runs over one plan, and that plan's two settings files are identical,
        // so the granting line it would have caught was never emitted. A scan is worth what its fixture
        // makes the code say — these lines are the only place this branch speaks.
        Check("and count a thing by naming it, never with (s)",
              !lines.Any(l => l.Contains("(s)", StringComparison.Ordinal)
                              || l.Contains("(es)", StringComparison.Ordinal)
                              || l.Contains("(ies)", StringComparison.Ordinal)),
              lines.FirstOrDefault(l => l.Contains("(s)", StringComparison.Ordinal)
                                        || l.Contains("(ies)", StringComparison.Ordinal)) ?? "");

        // Unreadable is its own answer. A file of nonsense must not read as two files that agree.
        File.WriteAllText(Path.Combine(b, "settings.json"), "not json at all {{{");
        SettingsUnion.Reading broken = SettingsUnion.For(a, b, "settings.json");
        Check("a file that will not parse is an absence, never a measured zero",
              broken.Error is { Length: > 0 } && !broken.Empty, broken.Error ?? "reported as empty");
        // A file on one side only is the widest form of this decision, not the emptiest.
        File.Delete(Path.Combine(b, "settings.json"));
        SettingsUnion.Reading oneSided = SettingsUnion.For(a, b, "settings.json");
        Check("and a file only one profile has says the link would hand over its whole contents",
              oneSided.Error is { Length: > 0 } && oneSided.Error.Contains("whole contents",
                  StringComparison.Ordinal), oneSided.Error ?? "");
        File.Delete(Path.Combine(a, "settings.json"));
        Check("while neither side having one is nothing to decide",
              SettingsUnion.For(a, b, "settings.json").Error is { Length: > 0 } none
              && none.Contains("nothing to decide", StringComparison.Ordinal));
    }

    /// <summary>
    /// The linking script <b>run</b>, which is the one thing thirty-one assertions about its text could
    /// not do (T372).
    ///
    /// <para><b>Why this exists.</b> Every check T367 shipped reads the emitted string, and every one of
    /// them passed on a version that could not finish a single run: <c>ResolveLinkTarget</c> does not
    /// exist on .NET Framework, so Windows PowerShell 5.1 threw <c>MethodNotFound</c> at the first entry's
    /// verification, and <c>New-Item -ItemType SymbolicLink</c> fails with "requires administrator
    /// privilege" on a machine whose Developer Mode the preflight has just approved. Neither is visible in
    /// text, neither would be caught by parsing the file, and both were found by a person running it.</para>
    ///
    /// <para><b>Two halves, and the split is what makes it runnable anywhere.</b> The first pair of config
    /// dirs holds <em>directories only</em>, so every link in the plan is a junction and the run needs no
    /// privilege at all — that is the happy path, asserted end to end, on CI included. The second adds a
    /// file, and then the machine decides: a run under Developer Mode links it, and a run without refuses
    /// at the preflight. Both are correct, so the claim is the <em>pair</em> — it either works or it
    /// refuses with nothing moved — which is exactly the property §XCI demanded and no text can hold.</para>
    ///
    /// <para><c>powershell.exe</c> and never <c>pwsh</c>: 5.1 is where both defects lived, and a check
    /// proving only 7.x would have gone green on each of them.</para>
    /// </summary>
    private static void LinkRun(string root)
    {
        if (!Directory.Exists(@"C:\Windows\System32\WindowsPowerShell\v1.0"))
        {
            // The precondition, and only the precondition: this asserts what Windows PowerShell does with
            // the script, so a machine without it can say nothing rather than a weaker thing.
            Skip("the emitted script runs and links a directories-only plan",
                 "Windows PowerShell 5.1 is not on this machine, and it is the shell the defects lived in");
            return;
        }

        string primary = Path.Combine(root, "keeps"), secondary = Path.Combine(root, "links");
        string[] dirs = { "projects", "file-history", "skills", "plugins" };
        foreach (string d in dirs)
        {
            Directory.CreateDirectory(Path.Combine(primary, d));
            Directory.CreateDirectory(Path.Combine(secondary, d));
        }
        Directory.CreateDirectory(Path.Combine(primary, "projects", "on-both"));
        Directory.CreateDirectory(Path.Combine(secondary, "projects", "on-both"));
        Directory.CreateDirectory(Path.Combine(secondary, "projects", "only-the-secondary-had-this"));

        ProfileLink.Plan plan = ProfileLink.For(primary, secondary, "Keeps", "Links");
        if (ReadOut.Failed(plan.Error)) return;
        Check("a directories-only plan is every-link-a-junction, so it needs no privilege",
              !plan.NeedsSymlink && plan.Acting.Count() == dirs.Length,
              $"symlink={plan.NeedsSymlink}, acting={plan.Acting.Count()} of {dirs.Length}");

        string ps1 = Path.Combine(root, "link-profiles.ps1");
        File.WriteAllText(ps1, ProfileLink.Script(plan),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        // The dry run, first and on its own: a bare run must exit 0 AND leave the tree alone. Asserting
        // only the exit code would pass on a script that did all the work and reported success.
        (int dry, string dryOut) = PowerShell(ps1, apply: false);
        Check("a bare run of the emitted script exits 0", dry == 0, Trim(dryOut));
        Check("and changes nothing at all", Directory.GetDirectories(secondary)
            .All(d => !Path.GetFileName(d).Contains(".pre-link-", StringComparison.Ordinal))
            && !IsLink(Path.Combine(secondary, "projects")));

        (int code, string output) = PowerShell(ps1, apply: true);
        Check("and -Apply exits 0", code == 0, Trim(output));

        // Every claim §XCI made about the result, read off the filesystem rather than off the transcript.
        string[] absent = dirs.Where(d => !IsLink(Path.Combine(secondary, d))).ToArray();
        Check($"every entry of the plan is a link on the secondary side ({dirs.Length})",
              absent.Length == 0, string.Join(", ", absent) + " — " + Trim(output));
        Check("and each link resolves into the primary",
              dirs.All(d => LinkTargets(Path.Combine(secondary, d), Path.Combine(primary, d))));
        // Never deleted, only moved aside — the promise the whole design rests on.
        string[] kept = Directory.GetDirectories(secondary)
            .Select(Path.GetFileName).Where(n => n!.Contains(".pre-link-", StringComparison.Ordinal))
            .ToArray()!;
        Check($"and each original is still beside it under .pre-link- ({dirs.Length})",
              kept.Length == dirs.Length, string.Join(", ", kept));
        // The merge, which is the part a bare rmdir-plus-mklink would have lost.
        Check("the union ran before the link: what only the secondary had is now on the primary",
              Directory.Exists(Path.Combine(primary, "projects", "only-the-secondary-had-this")));
        Check("and it is reachable through the link, which is what sharing one setup means",
              Directory.Exists(Path.Combine(secondary, "projects", "only-the-secondary-had-this")));

        // Idempotence against a tree its own first run made, not against one this check built.
        (int again, string againOut) = PowerShell(ps1, apply: true);
        Check("a second -Apply is a no-op rather than a relink of a link",
              again == 0 && againOut.Contains("already a link", StringComparison.Ordinal), Trim(againOut));
        Check("and it made no second set of .pre-link- copies",
              Directory.GetDirectories(secondary).Count(
                  d => Path.GetFileName(d).Contains(".pre-link-", StringComparison.Ordinal)) == dirs.Length);

        Unlink(secondary);

        // --- The second half: a plan carrying a file, where the machine decides which branch is right.
        File.WriteAllText(Path.Combine(primary, "CLAUDE.md"), "# primary\n");
        File.WriteAllText(Path.Combine(secondary, "CLAUDE.md"), "# secondary\n");
        ProfileLink.Plan withFile = ProfileLink.For(primary, secondary, "Keeps", "Links");
        Check("a plan carrying a file says it needs a symlink", withFile.NeedsSymlink);

        string ps2 = Path.Combine(root, "with-file.ps1");
        File.WriteAllText(ps2, ProfileLink.Script(withFile),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        (int fileCode, string fileOut) = PowerShell(ps2, apply: true);

        if (fileCode == 0)
        {
            Check("where a file symlink is allowed, the file is linked and its original kept",
                  IsLink(Path.Combine(secondary, "CLAUDE.md"))
                  && Directory.GetFiles(secondary, "CLAUDE.md.pre-link-*").Length == 1, Trim(fileOut));
            Check("and reading it through the link gives the primary's copy",
                  File.ReadAllText(Path.Combine(secondary, "CLAUDE.md")).Contains("# primary",
                      StringComparison.Ordinal));
        }
        else
        {
            // The refusal, and the claim is that it is CLEAN: no privilege, so nothing moved. A refusal
            // after three entries had been relinked would exit non-zero too, which is why this asserts the
            // tree and not the code.
            Check("where it is not, the preflight refuses before anything moves",
                  fileOut.Contains("Developer Mode", StringComparison.Ordinal), Trim(fileOut));
            Check("and the refusal left the secondary side exactly as it was",
                  !IsLink(Path.Combine(secondary, "CLAUDE.md"))
                  && Directory.GetFiles(secondary, "*.pre-link-*").Length == 0
                  && Directory.GetDirectories(secondary).Length == dirs.Length,
                  $"{Directory.GetDirectories(secondary).Length} dir(s), "
                  + $"{Directory.GetFiles(secondary, "*.pre-link-*").Length} moved aside");
        }

        Unlink(secondary);
        RefusesForReal(root);
    }

    /// <summary>
    /// The refusal, run rather than reasoned about (T386).
    ///
    /// <para><b>Why it needed its own fixture.</b> The pair above asserts that a plan carrying a file
    /// either links it or refuses with nothing moved, and takes whichever branch the machine offers. Both
    /// machines offer the same one: the developer's has Developer Mode on, and — read off the CI log, not
    /// assumed — a hosted runner allows an unprivileged <c>mklink</c> too. So the branch whose whole job is
    /// to stop before anything moves fired in no run anywhere. It had been seen once by hand, by pointing
    /// the preflight's registry read at a key that cannot exist, and that edit was reverted: which is what
    /// this repository calls a comment rather than a check.</para>
    ///
    /// <para><b>A fresh pair, because the promise is a tree.</b> "Nothing moved" cannot be asserted over
    /// directories an earlier half of this section already linked and unlinked — their
    /// <c>.pre-link-</c> copies are still there. And it is the tree that has to be read, not the exit
    /// code: a run that relinked three entries and then threw also exits non-zero.</para>
    ///
    /// <para>Skipped, not failed, when this process is elevated — the other half of the preflight's
    /// condition is <c>IsInRole(Administrator)</c>, and a check must not elevate itself to explore a
    /// branch.</para>
    /// </summary>
    private static void RefusesForReal(string root)
    {
        if (Elevated())
        {
            Skip("a plan needing a file symlink refuses, and moves nothing",
                 "this process is elevated, so the preflight's other half is satisfied and the refusal "
                 + "cannot be reached without dropping a privilege");
            return;
        }

        string a = Path.Combine(root, "refuse", "keeps"), b = Path.Combine(root, "refuse", "links");
        Directory.CreateDirectory(Path.Combine(a, "projects"));
        Directory.CreateDirectory(Path.Combine(b, "projects"));
        File.WriteAllText(Path.Combine(a, "CLAUDE.md"), "# primary\n");
        File.WriteAllText(Path.Combine(b, "CLAUDE.md"), "# secondary\n");

        ProfileLink.Plan real = ProfileLink.For(a, b, "Keeps", "Links");
        if (ReadOut.Failed(real.Error)) return;
        Check("the fixture plan needs a file symlink, so the preflight has something to refuse",
              real.NeedsSymlink);

        // The seam: a key that cannot exist, so `Get-ItemProperty` throws, `$devMode` stays 0, and the
        // condition's other half is already false because this process is not elevated.
        ProfileLink.Plan cannot = real with
        {
            DevModeRegistryKey = @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\NoSuchKeyForT386",
        };
        Check("and the key it reads is the plan's, not a constant in the text",
              ProfileLink.Script(cannot).Contains("NoSuchKeyForT386", StringComparison.Ordinal)
              && ProfileLink.Script(real).Contains(ProfileLink.DevModeKey, StringComparison.Ordinal));

        string ps1 = Path.Combine(root, "refuse", "refuses.ps1");
        File.WriteAllText(ps1, ProfileLink.Script(cannot),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        (int code, string output) = PowerShell(ps1, apply: true);

        Check("a run that cannot see Developer Mode refuses", code != 0, Trim(output));
        Check("and says which setting it needed",
              output.Contains("Developer Mode", StringComparison.Ordinal), Trim(output));
        // The promise, read off the disk. Every entry on the secondary side is exactly as it was: no link,
        // no copy moved aside, and the file still its own.
        string[] moved = Directory.GetFileSystemEntries(b)
            .Select(Path.GetFileName)
            .Where(n => n!.Contains(".pre-link-", StringComparison.Ordinal)).ToArray()!;
        Check("and nothing on the other side moved", moved.Length == 0, string.Join(", ", moved));
        Check("nor was anything linked",
              !IsLink(Path.Combine(b, "projects")) && !IsLink(Path.Combine(b, "CLAUDE.md")));
        Check("and its own file is still its own",
              File.ReadAllText(Path.Combine(b, "CLAUDE.md")).Contains("# secondary", StringComparison.Ordinal));

        // Unconditional, and that is the point: when these claims hold there is nothing to unlink, and the
        // one run that leaves reparse points here is the one where the refusal did NOT happen — which
        // `Temp`'s recursive delete cannot clean up. Measured, by breaking the seam: the suite left a
        // scratch tree behind. "Everything it writes is synthetic and removed" must not depend on the
        // checks passing.
        Unlink(b);
    }

    /// <summary>Whether this process is running elevated. The preflight is satisfied either by Developer
    /// Mode or by elevation, so an elevated run cannot reach the refusal at all.</summary>
    private static bool Elevated()
    {
        try
        {
            return new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        // Unknown is treated as elevated: this decides whether to skip, so it fails towards standing the
        // claim down rather than towards a red build nobody can reproduce.
        catch { return true; }
    }

    /// <summary>Whether a path is a reparse point — the reading the emitted script itself uses, for the
    /// same reason: it is the one both .NET editions have.</summary>
    private static bool IsLink(string path)
    {
        try
        {
            return File.Exists(path) || Directory.Exists(path)
                ? new FileInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint)
                : false;
        }
        catch { return false; }
    }

    private static bool LinkTargets(string link, string target)
    {
        try
        {
            return Directory.ResolveLinkTarget(link, returnFinalTarget: true) is { } t
                   && ClaudeAccount.SamePath(t.FullName, target);
        }
        catch { return false; }
    }

    /// <summary>
    /// Remove every reparse point directly under <paramref name="dir"/>, before <see cref="Temp"/>'s
    /// recursive delete reaches them. Not tidiness: a recursive delete over a junction is the one thing
    /// that cleanup cannot be trusted with, and this check makes several of them inside a temp tree it
    /// promises to remove.
    /// </summary>
    private static void Unlink(string dir)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(dir))
        {
            if (!IsLink(path)) continue;
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path);
                else File.Delete(path);
            }
            catch { Console.WriteLine($"  (could not unlink {path})"); }
        }
    }

    /// <summary>A process's output as one line of failure detail: the last thing it said, which for a
    /// script that stopped is the sentence naming why.</summary>
    private static string Trim(string output)
    {
        string[] lines = output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        string tail = lines.Length == 0 ? "(no output)" : string.Join(" | ", lines.TakeLast(3));
        return tail.Length > 300 ? tail[..300] + "…" : tail;
    }

    /// <summary>
    /// The invariants of the emitted script, over a script composed for <b>every branch</b> rather than for
    /// one plan (T380).
    ///
    /// <para><b>Why this exists, and it is not a worry.</b> A guard written to refuse a parenthesised
    /// plural anywhere in the script was watched to fail, and it <em>passed</em>: the plan it ran against
    /// had two identical <c>settings.json</c> files, so the withheld entry took its already-the-same branch
    /// and the offending line was never emitted at all. The guard was correct, the defect was real, and
    /// the fixture stood between them. A scan is worth what its fixture makes the code say.</para>
    ///
    /// <para><b>The coverage is asserted, not hoped for.</b> Every verdict and every per-entry state is
    /// enumerated from the enums themselves, so a fifth <see cref="ProfileLink.Verdict"/> demands a fixture
    /// on the day it is added rather than passing silently under the four that exist. A branch no plan here
    /// produced is <em>named</em> — the same rule as <c>Unchecked</c> (T193) one level up: a claim that
    /// could have been made and was not is not an absence of news.</para>
    ///
    /// <para>Junctions only, no file symlink, so this runs where Developer Mode does not — the
    /// already-linked branch is one code path whether the reparse point is on a directory or a file, and
    /// <c>plugins</c> covers it without a privilege.</para>
    /// </summary>
    private static void ScriptBranches(string root)
    {
        List<(string Name, ProfileLink.Plan Plan)> plans = BranchPlans(root);
        (string Name, string Text)[] scripts = plans
            .Select(p => (p.Name, Text: ProfileLink.Script(p.Plan))).ToArray();

        // The precondition. A fixture that failed to build composes a plan carrying an Error, and Script is
        // never called on one — over which every invariant below holds vacuously.
        string[] broken = plans.Where(p => p.Plan.Error is { Length: > 0 })
                               .Select(p => $"{p.Name}: {p.Plan.Error}").ToArray();
        if (!Check($"every branch fixture composes a plan ({plans.Count})", broken.Length == 0,
                   string.Join(" | ", broken)))
            return;

        // One line per invariant naming the plans that broke it, rather than one line per plan per
        // invariant: seven scripts times six claims is forty-two lines nobody reads.
        // The script's own prose, not the user's rules quoted inside it: `Bash(ls)` and `Read(/src/**)` are
        // exactly the refused shape and are not ours to rewrite, which is why they carry a marker (T380).
        var plural = new Regex(@"\p{L}\(\p{Ll}{1,3}\)", RegexOptions.Compiled);
        Every(scripts, "no parenthesised plural in the script's own prose",
              t => Ours(t).Any(l => plural.IsMatch(l)));
        Every(scripts, "no command names the credentials file", t => Acting(t)
            .Any(l => l.Contains(".credentials.json", StringComparison.OrdinalIgnoreCase)));
        Every(scripts, "nor the account file", t => Acting(t)
            .Any(l => l.Contains(".claude.json", StringComparison.OrdinalIgnoreCase)));
        Every(scripts, "no command deletes anything", t => Acting(t)
            .Any(l => l.Contains("Remove-Item", StringComparison.Ordinal)));
        Every(scripts, "every write stays behind -Apply",
              t => !t.Contains("param([switch]$Apply)", StringComparison.Ordinal));
        Every(scripts, "nothing elevates itself",
              t => t.Contains("Start-Process", StringComparison.Ordinal));
        Every(scripts, "the prose is ASCII apart from the two paths",
              t => t.Split('\n').Any(l => l.TrimStart().StartsWith('#') && l.Any(c => c > '~')));
        Every(scripts, "links are made with mklink, never New-Item",
              t => t.Contains("New-Item -ItemType SymbolicLink", StringComparison.Ordinal));

        // And the coverage itself.
        var seen = new SortedSet<string>(StringComparer.Ordinal);
        foreach ((string _, ProfileLink.Plan plan) in plans) seen.UnionWith(Branches(plan));
        string[] required = Required().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        string[] missing = required.Where(b => !seen.Contains(b)).ToArray();
        Check($"every branch of the script appears in one of them ({required.Length} branches, {plans.Count} plans)",
              missing.Length == 0,
              Named(missing, "produced by no fixture here, so nothing above was asserted about them"));

        // The reparse points go before the tree they sit in does — `Temp`'s recursive delete cannot be
        // trusted with one, and this section makes seven. Measured: without it the run ends on
        // "(could not remove …)" and leaves the scratch directory behind.
        foreach ((string _, ProfileLink.Plan plan) in plans) Unlink(plan.SecondaryDir);
    }

    /// <summary>One invariant over every script, failing once and naming the plans that broke it.</summary>
    private static void Every((string Name, string Text)[] scripts, string claim, Func<string, bool> bad)
    {
        string[] broke = scripts.Where(s => bad(s.Text)).Select(s => s.Name).ToArray();
        Check($"{claim} ({scripts.Length} scripts)", broke.Length == 0, string.Join(", ", broke));
    }

    private static void Dir(string root, params string[] parts) =>
        Directory.CreateDirectory(Path.Combine(new[] { root }.Concat(parts).ToArray()));

    private static string Label(ProfileLink.Step s) => s.Entry.Verdict switch
    {
        ProfileLink.Verdict.Merge or ProfileLink.Verdict.Adopt => $"{s.Entry.Verdict}/"
            + (s.AlreadyLinked ? "already-linked"
               : !s.OnPrimary ? "absent-primary"
               : !s.OnSecondary ? "link-only" : "acting"),
        ProfileLink.Verdict.Withheld => "Withheld/"
            + (s.Widening is null or { Error.Length: > 0 } ? "unreadable"
               : s.Widening.Empty ? "same" : "granting"),
        _ => "Never/explained",
    };

    /// <summary>
    /// The sampled environment (T231), and the one promise it has to keep: a process answering off a
    /// fixture writes nothing to the machine.
    ///
    /// <para><b>Runs last, and nothing may follow it.</b> <see cref="EnvironmentProfile.Sample"/> is
    /// one-way on purpose — a fixture that could be switched back off would let a later caller write the
    /// real <c>CLAUDE_CONFIG_DIR</c> in a process that has already been lying about it — so every
    /// assertion above that reads the true variable has to have run by now.</para>
    /// </summary>
    private static void SampledEnvironment()
    {
        // Read the machine's own value BEFORE anything is sampled: it is the control the write below is
        // measured against, and after Sample() there is no way left to ask for it.
        string? real = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR", EnvironmentVariableTarget.User);

        // The catalogue is the refusal (T186's rule): an unknown mode names what is on offer rather than
        // rendering the ordinary state under a flag that asked for another.
        string? refusal = EnvironmentFixture.Apply("no-such-mode");
        Check("an unknown --sample-env mode is refused, not silently ignored", refusal is { Length: > 0 });
        Check("and the refusal names every mode there is",
              refusal is { } r && EnvironmentFixture.Modes.All(m => r.Contains(m.Name, StringComparison.Ordinal)));
        Check("a refused mode samples nothing", !EnvironmentProfile.IsSampled);

        const string fake = @"C:\Users\nobody\.claude-sampled";
        EnvironmentProfile.Sample(fake);
        Check("a sampled variable is what every reader sees",
              EnvironmentProfile.IsSampled
              && EnvironmentProfile.Current() == fake
              && EnvironmentProfile.EffectiveConfigDir() == fake);

        // The whole variable, not one caller's answer: the menu asks Selected, the read-out asks
        // EffectiveConfigDir, and a fixture that moved one without the other would certify a screen that
        // cannot occur — which is the objection T231 was filed carrying.
        var profiles = new List<ClaudeInfo>
        {
            new() { Label = "Personal", ConfigDir = ClaudeAccount.HomeConfigDir },
            new() { Label = "Work", ConfigDir = fake },
        };
        Check("and the selection follows it, so the menu and the read-out cannot disagree",
              EnvironmentProfile.SelectedIn(profiles, EnvironmentProfile.EffectiveConfigDir())?.Label == "Work");

        EnvironmentProfile.Sample(null);
        Check("a sampled-absent variable reads as the default config dir, as a real absent one does",
              ClaudeAccount.SamePath(EnvironmentProfile.EffectiveConfigDir(), ClaudeAccount.HomeConfigDir));

        // The promise. Adopt goes through the real queue, the real read-back and the real outcome — the
        // only thing it does not go through is the registry.
        var settings = new Settings();
        EnvironmentProfile.Adopt(settings, fake);
        EnvironmentProfile.Drain();
        Check("a write under the fixture moves the fixture", EnvironmentProfile.Current() == fake);
        Check("and reports itself landed, so T173's whole path is exercised off the machine",
              EnvironmentProfile.Last is { Landed: true, Wrote: true });
        Check("and the machine's own variable is untouched",
              Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR", EnvironmentVariableTarget.User) == real);
    }
}
