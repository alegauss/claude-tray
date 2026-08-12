namespace ClaudeTray;

/// <summary>
/// Every mutation this process makes to its own store, counted as it happens (T356).
///
/// <para><b>Why a counter and not a diff.</b> <c>--selftest</c> promised that a check run adds nothing to
/// the user's files, and asserted it by fingerprinting <c>%LocalAppData%\ClaudeTray</c> before and after.
/// That tree is shared with the user's resident tray, which polls on its own timer — so a run straddling
/// a poll went red on work the check under test did not do. Seen four times in one afternoon, naming
/// <c>usage-history.jsonl</c>, <c>settings.json</c>, <c>session-index.json</c> and
/// <c>activity-sweep.json</c>, each time green on the very next run.</para>
///
/// <para>The deeper problem is that a diff cannot answer the question the promise makes. When the gate
/// works, <em>every</em> change the tree shows is somebody else's by definition, so "something moved" and
/// "we moved something" are different facts and only one of them is the promise. This counts the writes
/// at the point they are performed, which is the only place the answer is known.</para>
///
/// <para><b>Only effective mutations count.</b> Creating a directory that exists and deleting a file that
/// does not are no-ops on disk, and counting them would make the assertion fire on calls that changed
/// nothing — a false positive in a check whose whole value is that it does not have any.</para>
///
/// <para><b>It is not a gate.</b> <see cref="ProfileStore.Observing"/> is what stops a write; this only
/// records that one happened. A writer that consults neither is caught by the source scan beside
/// <c>ObservingTray</c>, which is why both exist.</para>
/// </summary>
internal static class StoreFile
{
    private static int _writes;

    /// <summary>How many effective mutations this process has performed on the store.</summary>
    public static int Writes => System.Threading.Volatile.Read(ref _writes);

    /// <summary>Start counting from zero — called by the check before it drives the writers, so the
    /// figure is about the drive and not about whatever the process did on startup.</summary>
    public static void ResetWrites() => System.Threading.Volatile.Write(ref _writes, 0);

    public static void CreateDirectory(string path)
    {
        if (Directory.Exists(path)) { Directory.CreateDirectory(path); return; }
        Count();
        Directory.CreateDirectory(path);
    }

    public static void WriteAllText(string path, string contents)
    {
        Count();
        File.WriteAllText(path, contents);
    }

    public static void WriteAllLines(string path, IEnumerable<string> lines)
    {
        Count();
        File.WriteAllLines(path, lines);
    }

    public static void AppendAllText(string path, string contents)
    {
        Count();
        File.AppendAllText(path, contents);
    }

    public static void AppendAllLines(string path, IEnumerable<string> lines)
    {
        Count();
        File.AppendAllLines(path, lines);
    }

    public static void Move(string from, string to, bool overwrite = false)
    {
        Count();
        File.Move(from, to, overwrite);
    }

    public static void Delete(string path)
    {
        if (!File.Exists(path)) return;
        Count();
        File.Delete(path);
    }

    private static void Count() => System.Threading.Interlocked.Increment(ref _writes);
}
