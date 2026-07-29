namespace ClaudeTray;

/// <summary>
/// A recursive directory walk that no single unreadable directory can kill.
///
/// <para>Every scan in this app sweeps <c>~/.claude</c> with
/// <see cref="SearchOption.AllDirectories"/>, and a developer's tree is full of things the framework
/// walker treats as fatal: a junction Windows refuses to traverse ("the path cannot be traversed
/// because it contains an untrusted mount point" — Redirection Guard, when the link was created by a
/// differently-trusted account), a directory whose ACL denies listing, a session folder deleted
/// mid-sweep, a path past MAX_PATH. Any one of those aborts the *whole* enumeration, so the
/// Statistics window reports "couldn't build the report" and shows nothing — one bad folder anywhere
/// under <c>projects/</c> costs the user every chart.</para>
///
/// <para>This walks directory by directory instead, so a failure is scoped to the subtree that
/// caused it. Three details matter and are easy to get wrong:</para>
/// <list type="bullet">
/// <item>Each directory's entries are <b>materialized</b> (<c>GetFiles</c>, not
/// <c>EnumerateFiles</c>). A <c>try</c> around a lazy enumerable catches nothing — the IO happens
/// later, inside the caller's <c>foreach</c>.</item>
/// <item>A reparse point is <b>replaced by the path it names</b> before anything opens it. That is
/// exactly the untrusted-junction case: traversing <i>through</i> the link is blocked while opening
/// the real directory it points at is not, so the content is still read. It also means a target
/// reachable by several junctions is walked once, not once per link.</item>
/// <item>Children come back as <see cref="DirectoryInfo"/>, whose attributes are already filled in
/// from the enumeration, so spotting those reparse points costs no extra syscall on the ordinary
/// directory — the overhead against the framework walker stays around a third
/// (~46ms vs ~34ms over 600 transcripts).</item>
/// </list>
///
/// <para>Visited paths are de-duplicated, which is what stops a junction pointing at one of its own
/// ancestors from looping forever; the depth cap is the belt to that braces.</para>
/// </summary>
internal static class SafeWalk
{
    // Deep enough for any real ~/.claude tree (plugins/<market>/<plugin>/skills/<skill>/...) and
    // shallow enough that a pathological link chain can't spin.
    private const int MaxDepth = 24;

    /// <summary>Every file under <paramref name="root"/> matching <paramref name="pattern"/>,
    /// recursively, skipping whatever can't be read.</summary>
    public static IEnumerable<FileInfo> Files(string root, string pattern)
    {
        foreach (DirectoryInfo dir in Descend(root))
        {
            FileInfo[] batch;
            try { batch = dir.GetFiles(pattern); }
            catch { continue; }
            foreach (FileInfo f in batch) yield return f;
        }
    }

    /// <inheritdoc cref="Files"/>
    public static IEnumerable<string> Paths(string root, string pattern)
    {
        foreach (DirectoryInfo dir in Descend(root))
        {
            string[] batch;
            try { batch = Directory.GetFiles(dir.FullName, pattern); }
            catch { continue; }
            foreach (string p in batch) yield return p;
        }
    }

    /// <summary>Every directory under <paramref name="root"/> whose name matches
    /// <paramref name="pattern"/>, recursively, skipping whatever can't be read.</summary>
    public static IEnumerable<string> Dirs(string root, string pattern)
    {
        foreach (DirectoryInfo dir in Descend(root))
        {
            string[] batch;
            try { batch = Directory.GetDirectories(dir.FullName, pattern); }
            catch { continue; }
            foreach (string d in batch) yield return d;
        }
    }

    /// <summary><paramref name="root"/> and every readable directory beneath it, each yielded as the
    /// real path — a junction is replaced by what it points at, so it is walked once whichever way it
    /// is reached, and the blocked traversal never happens in the first place.</summary>
    private static IEnumerable<DirectoryInfo> Descend(string root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<(DirectoryInfo dir, int depth)>();
        stack.Push((new DirectoryInfo(root), 0));

        while (stack.Count > 0)
        {
            (DirectoryInfo dir, int depth) = stack.Pop();
            dir = Resolve(dir);
            if (!seen.Add(Key(dir.FullName))) continue;

            // Listing the children doubles as the readability probe: if this throws, nothing else in
            // the directory would have worked either. The second attempt covers the case where the
            // attribute read above was itself refused, so the link was never resolved.
            DirectoryInfo[]? kids = Children(dir);
            if (kids is null)
            {
                DirectoryInfo? target = LinkTarget(dir);
                if (target is null || !seen.Add(Key(target.FullName))) continue;
                kids = Children(target);
                if (kids is null) continue;
                dir = target;
            }

            yield return dir;
            if (depth >= MaxDepth) continue;
            foreach (DirectoryInfo kid in kids) stack.Push((kid, depth + 1));
        }
    }

    private static DirectoryInfo[]? Children(DirectoryInfo dir)
    {
        try { return dir.GetDirectories(); }
        catch { return null; }
    }

    /// <summary>Follow a chain of reparse points to the directory they name. Free for the ordinary
    /// directory: the attribute was already read by the enumeration that produced it.</summary>
    private static DirectoryInfo Resolve(DirectoryInfo dir)
    {
        try
        {
            for (int hop = 0; hop < 8; hop++)
            {
                if ((dir.Attributes & FileAttributes.ReparsePoint) == 0) break;
                DirectoryInfo? target = LinkTarget(dir);
                if (target is null || Key(target.FullName) == Key(dir.FullName)) break;
                dir = target;
            }
        }
        catch { /* unreadable — hand the original back and let Children decide */ }
        return dir;
    }

    /// <summary>Where a reparse point points, read from the link itself
    /// (<c>returnFinalTarget: false</c>) — resolving the final target would traverse the very thing
    /// Windows may be refusing.</summary>
    private static DirectoryInfo? LinkTarget(DirectoryInfo dir)
    {
        try { return Directory.ResolveLinkTarget(dir.FullName, returnFinalTarget: false) as DirectoryInfo; }
        catch { return null; }
    }

    private static string Key(string dir)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir)); }
        catch { return dir; }
    }
}
