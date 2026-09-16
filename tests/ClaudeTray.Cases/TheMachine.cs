using Winwright.Projects;

using Xunit;

namespace ClaudeTray.Cases;

/// <summary>
/// This repository, found from wherever the cases were built to.
/// <para>
/// WW83. It was written out in both test classes and a third copy was about to be written here,
/// which is the count that turns a repeated line into a member. The walk is the same either way:
/// upwards until the project declaration is beside it, so a case carries no path and runs on any
/// checkout.
/// </para>
/// </summary>
internal static class Checkout
{
    /// <summary>Where <c>winwright.json</c> is, which is what every reading here resolves against.</summary>
    internal static string Root()
    {
        var walking = new DirectoryInfo(AppContext.BaseDirectory);
        while (walking is not null && !File.Exists(Path.Combine(walking.FullName, ProjectDeclaration.FileName)))
            walking = walking.Parent;

        Assert.NotNull(walking);
        return walking.FullName;
    }
}

/// <summary>
/// The machine these cases are about, fabricated once for the whole run.
/// <para>
/// WW83. <see cref="Bench" /> was built inside the case that runs the suite, and the cases that
/// derive an expected set from the application's own read-outs are in the other class — so on a
/// machine with no Claude Code accounts, which is the guest and every hosted runner, they asked a
/// real application about a real desk and got the honest answer: nothing. The red said
/// <c>reported nothing under 'profiles', and an empty expected set is met by an empty window</c>,
/// which is the engine refusing a vacuous set exactly as it should and a claim about this project's
/// own arrangement rather than about anything either case is for.
/// </para>
/// <para>
/// So the bench belongs to the run and not to one case in it. Here rather than duplicated, because
/// it can only exist once: <c>Bench.Under</c> deletes and rebuilds <c>bench/</c> and sets
/// <c>CLAUDE_CONFIG_DIR</c> on this process, and two classes doing that in parallel would each be
/// taking the floor out from under the other — a race that would have arrived as a case failing
/// about profiles on some runs and not others, which is the shape nobody diagnoses.
/// </para>
/// <para>
/// One collection and therefore one at a time, which is the second thing this buys. The classes
/// share a process-wide environment variable, and what they cost in parallelism is nothing: the run
/// is one case long and the load half answers in seconds.
/// </para>
/// </summary>
public sealed class TheMachine : IDisposable
{
    /// <summary>What the collection is called, spelled once so a class cannot join a different one.</summary>
    public const string Name = "the machine these cases are about";

    /// <summary>Build it, before any case in the collection has run.</summary>
    public TheMachine()
    {
        Bench = Bench.Under(Checkout.Root());
    }

    /// <summary>The two profiles, the transcript, and where they are.</summary>
    internal Bench Bench { get; }

    /// <summary>Take it down, and put the environment back the way the desk had it.</summary>
    public void Dispose()
    {
        Bench.Dispose();
    }
}

/// <summary>
/// The collection both classes join. A definition and nothing else — xUnit wants the attribute on a
/// type, and the fixture above is what it is really about.
/// </summary>
[CollectionDefinition(TheMachine.Name)]
public sealed class TheMachineCollection : ICollectionFixture<TheMachine>
{
}
