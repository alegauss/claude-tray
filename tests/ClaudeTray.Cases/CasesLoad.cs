using Winwright.Asserting;
using Winwright.Projects;
using Winwright.Scenarios;

using Xunit;

namespace ClaudeTray.Cases;

/// <summary>
/// Whether the cases in this repository are cases at all, asked without a window.
/// <para>
/// This is the half of a migration that can be checked anywhere. Every field is judged at the point
/// it is read — a key the format does not have, a value of the wrong kind, an act that is not an act,
/// a fixture nothing declares — so a file that loads is a file whose vocabulary, whose readings and
/// whose fixture names are all real. What it does not say is whether the application does what the
/// case claims; that needs a desk, and this deliberately needs none.
/// </para>
/// <para>
/// It is here rather than left to the run for the reason the format refuses at insertion rather than
/// linting afterwards: a case that will not load is a case nobody should discover on the machine that
/// was going to run it.
/// </para>
/// </summary>
public sealed class CasesLoad
{
    [Fact]
    public void Every_case_file_in_this_repository_loads()
    {
        var project = ProjectDeclaration.Find(Repository());
        var files = ScenarioFile.LoadAll(Path.Combine(Repository(), "cases"));

        Assert.NotEmpty(files);
        Assert.NotEmpty(ScenarioFile.Across(files));

        // The declaration resolves too, or the run would refuse before the first case with a
        // sentence about this file rather than about any case in it.
        Assert.True(File.Exists(project.Executable), $"the declaration names {project.Executable} and it is not built");
    }

    [Fact]
    public void The_profiles_this_machine_has_are_derived_from_the_application_and_never_typed()
    {
        // WW291. The second well, against this application rather than against a fixture: the set is
        // whatever this machine has, so nothing here names a profile and nothing can. What is asserted
        // is that the application answered a set at all — one label per line — and that the labels are
        // the ones its own read-out reports, which is what a case comparing the menu will match inside
        // the decorated entries.
        //
        // The check script this replaces got its numbers out of the `--profiles` report with regexes
        // like `polled every interval:\s*\d+\s+of\s+(\d+)`. That is the typed expectation with an extra
        // step: it goes stale the day a line is reworded and says nothing when it does.
        var project = ProjectDeclaration.Find(Repository());

        Assert.True(
            project.ReportedSets.ContainsKey("profiles"),
            $"{project.Path} declares no reportedSet called 'profiles'");

        var set = DerivedSet.Reported("the profiles this machine has", project, "profiles");

        // Not a count typed here either: what is claimed is that the application named at least one
        // and that every value it named is a label rather than a blank or a line of the report.
        Assert.NotEmpty(set.Expected);
        Assert.All(set.Expected, one => Assert.False(string.IsNullOrWhiteSpace(one), $"'{one}' is not a label"));
        Assert.All(set.Expected, one => Assert.DoesNotContain("   ", one, StringComparison.Ordinal));

        // And the source says how it was asked, so a red carries the flag rather than sending the
        // reader to guess which read-out answered.
        Assert.Contains("--profile-names", set.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void The_menu_state_this_machine_is_in_is_derived_from_the_application()
    {
        // WW294. The single values beside the set, and the ones the submenu's check mark is about.
        // Nothing here names a profile: which one the icon follows is whatever this desk is doing.
        var project = ProjectDeclaration.Find(Repository());

        foreach (var name in new[] { "iconFollows", "envSelects" })
        {
            Assert.True(project.ReportedValues.ContainsKey(name), $"{project.Path} declares no '{name}'");

            var value = DerivedSet.ReportedValue($"the profile {name}", project, name);

            Assert.False(string.IsNullOrWhiteSpace(value), $"'{name}' answered nothing");
        }

        // And the icon follows one of the profiles the application says it has, or the two read-outs
        // disagree about one application and neither is evidence about the menu. `-` is the answer
        // where nothing is being followed, which is a state rather than a failure to read.
        var follows = DerivedSet.ReportedValue("the profile the icon follows", project, "iconFollows");
        var all = DerivedSet.Reported("the profiles", project, "profiles").Expected;

        Assert.True(follows == "-" || all.Contains(follows), $"the icon follows '{follows}', of {string.Join(", ", all)}");
    }

    [Fact]
    public void The_keyboard_case_asserts_the_things_the_script_asserted()
    {
        // WW78's own claim, read off the loaded case rather than off the file.
        var loaded = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));
        var keyboard = Assert.Single(
            loaded,
            one => one.Name == "keyboard input reaches the settings window under the tray's pump");

        // Three checks and four steps. The script reported four passes, and its first was the
        // navigation — which is not a check here but a navigation whose consequence the step after it
        // is the check for: nothing resolves `Edit#DirectoryBox` unless the click landed.
        Assert.Equal(3, keyboard.Checks);
        Assert.True(keyboard.Justified, "a case that says nothing about why it exists says nothing about deleting it");
        Assert.Equal("the settings window under the tray's own pump", keyboard.Fixture.Name);

        // Every act in it synthesises input, and that is the whole point: the pattern routes beside
        // them — set value, set range, invoke — all passed on the day of the bug.
        Assert.Equal(["click", "type", "press", "nudge"], keyboard.Steps.Select(one => one.Verb.Name));
        Assert.All(keyboard.Steps, one => Assert.True(one.Verb.Synthesises, one.Verb.Name));

        // WW229. The slider's claim is that the value moved, not what it moved to: two steps became
        // one, and the one that went was a write through the pattern the key press exists to bypass.
        var slider = keyboard.Steps.Single(one => one.Verb.Name == "nudge");
        Assert.True(slider.Moves);
        Assert.Null(slider.Expected);
    }

    [Fact]
    public void The_panes_claims_are_two_cases_and_not_one()
    {
        // WW79. T165's defect left every tab header readable and took the whole body with them, so
        // "headers but no body" is the shape worth naming — and a case that asserted both would have
        // one verdict for two claims. The script had one function with two blocks in it; this is what
        // that separation looks like once the runner owns the reporting.
        var loaded = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));
        var panes = loaded.Where(one => one.Tags.Contains("panes")).ToList();

        Assert.Equal(2, panes.Count);
        Assert.All(panes, one => Assert.True(one.Justified, "a case that says nothing about why it exists says nothing about deleting it"));

        // Both read and neither acts, which is what lets one window serve both — T195's window
        // lending, which the script did with an Acquire-Main / Release-Main pair.
        Assert.All(panes, one => Assert.True(one.OnlyReads));
        Assert.All(panes, one => Assert.True(one.Fixture.Shareable));
        Assert.Single(panes.Select(one => one.Fixture.Name).Distinct());

        var headers = Assert.Single(panes, one => one.Needs.Count == 0);
        var body = Assert.Single(panes, one => one.Needs.Count > 0);

        // The headers run everywhere, which is the whole of T176: behind the profiles case's
        // two-profile skip this never ran on a one-profile machine, and it is the only thing that
        // would have noticed a template part going missing again.
        Assert.Equal("stats.tab", Assert.Single(headers.Steps).Covers);

        // The body is gated on a precondition and not on a weaker assertion (T161): the pane is
        // Collapsed until a report renders, so with no report there is nothing to look for.
        Assert.Equal(["a transcript to report on"], body.Needs);

        // Three readings, all of the name, all claiming an answer rather than a value. A percentage
        // is this machine's consumption, so a case naming one would only hold on the desk it was
        // written on.
        Assert.Equal(3, body.Checks);
        Assert.All(body.Steps, one => Assert.Equal("name", one.Reads.Name));
        Assert.All(body.Steps, one => Assert.True(one.Answers));
        Assert.All(body.Steps, one => Assert.Null(one.Expected));
    }

    [Fact]
    public void The_sessions_claims_are_the_two_the_script_made_and_the_shapes_it_made_them_in()
    {
        // WW80. Invoke-SessionsCase asserted two things about two different surfaces — a note behind
        // a popup no capture can photograph, and a row that unfolds into a call tree — and reported
        // both under one heading. They fail for unrelated reasons, so they are two cases here.
        var loaded = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));
        var sessions = loaded.Where(one => one.Tags.Contains("sessions")).ToList();

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, one => Assert.True(one.Justified, "a case that says nothing about why it exists says nothing about deleting it"));
        Assert.All(sessions, one => Assert.Equal(["a transcript to report on"], one.Needs));

        // Neither only reads and the window is not lendable, which is the inverse of the panes pair:
        // one opens a popup, the other unfolds a row, and a case after either would inherit whatever
        // it left open.
        Assert.All(sessions, one => Assert.False(one.OnlyReads));
        Assert.All(sessions, one => Assert.False(one.Fixture.Shareable));

        var note = Assert.Single(sessions, one => one.Name.Contains("info dot", StringComparison.Ordinal));
        var row = Assert.Single(sessions, one => one.Name.Contains("call tree", StringComparison.Ordinal));

        // WW250. The note's claim is the date its figure came from, not the words around it. T361 is
        // why: matching 'list prices' found nothing in the other four languages and reported a
        // readable note as unreadable, and a date has the same shape in all five.
        var reading = Assert.Single(note.Steps, one => one.Matches is not null);
        Assert.Equal("name", reading.Reads.Name);
        Assert.Matches(reading.Matches!, "read on 2026-08-26 at list prices");
        Assert.DoesNotMatch(reading.Matches!, "read at list prices");

        // And the surface is put back, which the script did with a second click and a sleep. The last
        // step is what says it landed rather than what says it was attempted.
        Assert.Equal("Off", note.Steps[^1].Expected);

        // WW251. The row's claim is that there is more under it than there was — no count, no reading
        // beside it, and an act that could have put something there. A `read` here would be asserting
        // that a window changed while nobody touched it.
        var unfolds = Assert.Single(row.Steps, one => one.Discloses);
        Assert.Equal("click", unfolds.Verb.Name);
        Assert.True(unfolds.Verb.Synthesises);
        Assert.Null(unfolds.Expected);
        Assert.False(unfolds.Moves);
        Assert.False(unfolds.Answers);
        Assert.Null(unfolds.Covers);
        Assert.Null(unfolds.Matches);
    }

    [Fact]
    public void The_profiles_case_names_no_profile_anywhere_in_it()
    {
        // WW81. The property that makes this case portable, and the one a migration is most likely to
        // lose: the picker holds whatever accounts a desk happens to have, so every claim is about a
        // reading moving or coming back and not one of them is about a value.
        var loaded = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));
        var profiles = Assert.Single(loaded, one => one.Tags.Contains("profiles"));

        Assert.True(profiles.Justified, "a case that says nothing about why it exists says nothing about deleting it");
        Assert.Equal(["a second profile", "a transcript to report on"], profiles.Needs);
        Assert.False(profiles.OnlyReads);
        Assert.False(profiles.Fixture.Shareable);

        // Not one typed expectation in the whole case. `expect` compares a reading to a string, and
        // every string this case could write is this machine's own data.
        Assert.All(profiles.Steps, one => Assert.Null(one.Expected));
        Assert.Equal(profiles.Steps.Count, profiles.Checks);

        // WW267. Both walks are positions and neither is a name, which is the half that could not be
        // written at all before it: `pick` reaches a value, and a value here is an account.
        var walks = profiles.Steps.Where(one => one.Verb.Name == "pick at").ToList();
        Assert.Equal(["1", "0"], walks.Select(one => one.Argument));
        Assert.All(walks, one => Assert.Equal("picked", one.Reads.Name));

        // WW266 and WW255. Away, then back to the step that read it first — and the value neither of
        // them names is the one the round trip is entirely about.
        Assert.True(walks[0].Moves);
        Assert.Equal("the first stop", walks[1].SameAs);

        // WW256, and it is the claim WW81 was filed for beside the round trip: the line is never
        // shown while the report comes back, which no read taken afterwards could tell you.
        var watching = Assert.Single(profiles.Steps, one => one.Never is not null);
        Assert.Equal("stats.live.off", watching.Never);
        Assert.Equal("read", watching.Verb.Name);
    }

    [Fact]
    public void The_names_cases_type_no_label_and_check_both_branches_of_the_rule()
    {
        // WW84. The property the script could not have and this migration exists for: every label
        // here is a key, so the case reads whatever the strings file says in whatever language the
        // fixture launched — and editing en.json cannot leave a stale expectation behind.
        var loaded = ScenarioFile.Across(ScenarioFile.LoadAll(Path.Combine(Repository(), "cases")));
        var names = loaded.Where(one => one.Tags.Contains("names")).ToList();

        Assert.Equal(3, names.Count);
        Assert.All(names, one => Assert.True(one.Justified, "a case that says nothing about why it exists says nothing about deleting it"));
        Assert.All(names, one => Assert.Empty(one.Needs));

        // The two that only read share their windows — T195's lending, which the script did with an
        // Acquire-Main / Release-Main pair around the whole of Invoke-NamesCase. The walk drives the
        // sidebar, so it owns its window and lends it to nobody: a case after it would inherit
        // whichever panel the last member happened to leave showing.
        var reading = names.Where(one => one.OnlyReads).ToList();

        Assert.Equal(2, reading.Count);
        Assert.All(reading, one => Assert.True(one.Fixture.Shareable));

        // T196, and the property the script had and no listed set can: the panels come from the
        // strings the application ships, so a seventh is covered with no edit to the case.
        var walk = Assert.Single(names, one => one.ForEach is not null);

        Assert.Equal("settings.nav", walk.ForEach);
        Assert.False(walk.OnlyReads);
        Assert.False(walk.Fixture.Shareable);

        // WW273. The sidebar items are bare Borders with no automation peer, so their words are the
        // only thing that addresses one — and the words here are a key, not a string, which is what
        // lets this walk a Portuguese window as readily as an English one.
        // Asserted rather than assumed: a step's locator became optional when a step learned to name
        // a tray icon as its subject instead, so a walk step without one is now a shape the type
        // allows. This one has it, and saying so is what makes the next line about the words.
        var addressed = walk.Steps[0].Locator;
        Assert.NotNull(addressed);
        Assert.Contains("{}", addressed.Text, StringComparison.Ordinal);

        // Not one typed label in either of them. WW261 is what makes that possible: before it, a case
        // expecting a label had to write the words, which is the hardcoded set one control down.
        var reads = names.SelectMany(one => one.Steps).Where(one => one.Label is not null).ToList();
        Assert.Equal(7, reads.Count);
        Assert.All(reads, one => Assert.Null(one.Expected));
        Assert.All(reads, one => Assert.Equal("name", one.Reads.Name));

        // T204's half, on every panel, and it is the one a name-existence check is blind to by
        // construction: a rule handing every control its neighbour's header reads exactly like one
        // handing out the right ones.
        Assert.Equal(3, names.SelectMany(one => one.Steps).Count(one => one.OwnHeader));

        // The field-plus-Buttons branch, which is the one that must NOT fire. Four keys and four
        // different strings, so a header that leaked onto the buttons fails three of them by name.
        var row = Assert.Single(names, one => one.Name.Contains("buttons beside it", StringComparison.Ordinal));
        Assert.Equal(
            ["settings.cc.workDir", "settings.cc.browse", "settings.cc.profileAdd", "settings.cc.profileRemove"],
            row.Steps.Select(one => one.Label).OfType<string>());
    }

    /// <summary>This repository's root, found by walking up to the file the project declares itself in.</summary>
    private static string Repository()
    {
        var walking = new DirectoryInfo(AppContext.BaseDirectory);
        while (walking is not null && !File.Exists(Path.Combine(walking.FullName, ProjectDeclaration.FileName)))
            walking = walking.Parent;

        Assert.NotNull(walking);
        return walking.FullName;
    }
}
