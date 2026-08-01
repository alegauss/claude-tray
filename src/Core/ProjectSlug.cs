namespace ClaudeTray;

/// <summary>
/// The one reader — and writer — of Claude Code's <c>~/.claude/projects/&lt;slug&gt;</c> encoding.
///
/// <para>A slug is the session's root path with <b>every non-alphanumeric character replaced by
/// <c>-</c></b>: <c>d:\Git\acme\claude-tray</c> → <c>d--Git-acme-claude-tray</c>. That is lossy in a
/// way that matters — a separator and a literal hyphen encode identically — so a slug on its own
/// <b>cannot</b> be split back into a path. <c>claude-tray</c> and <c>claude\tray</c> have the same
/// slug, and <c>…-shio-2026-3</c> naïvely reads as the folder "3".</para>
///
/// <para>There are therefore only two honest ways to answer "which directory is this?", and this class
/// owns both so they cannot drift apart (T105 — three call sites had grown their own, and one of them
/// encoded a different character set):</para>
/// <list type="number">
/// <item><see cref="RootFor"/> — <b>exact</b>. Given a <c>cwd</c> the session recorded, walk it up to
/// the ancestor whose encoding <em>equals</em> the slug. This verifies rather than guesses, needs no
/// filesystem at all (so it still answers for a directory that has since been deleted or unplugged),
/// and is immune to the trap that the recorded <c>cwd</c> is the working directory <em>of that
/// turn</em> — one <c>cd</c> into a subfolder does not move the answer.</item>
/// <item><see cref="TryProbe"/> — a <b>guess</b>, for when no <c>cwd</c> is available at all. Tries
/// every split against the real filesystem, shortest segment first, with backtracking. It can only
/// return a directory that exists, and when two readings both exist it returns the first one found,
/// which is why it is the fallback and not the answer.</item>
/// </list>
/// </summary>
internal static class ProjectSlug
{
    /// <summary>How far up from a <c>cwd</c> the root is looked for. Deep enough for any real
    /// checkout, bounded so a pathological path cannot walk forever.</summary>
    private const int MaxDepth = 24;

    /// <summary>The encoding itself — what Claude Code names the directory after. Kept here so the
    /// fixture writes exactly what the real thing writes, rather than an approximation that happens to
    /// agree for the paths it was tested on.</summary>
    public static string Encode(string path)
    {
        var sb = new System.Text.StringBuilder(path.Length);
        foreach (char c in path) sb.Append(char.IsAsciiLetterOrDigit(c) ? c : '-');
        return sb.ToString();
    }

    /// <summary>The session's <b>root</b> directory, recovered exactly by verifying candidates against
    /// the slug. Null when no ancestor of <paramref name="cwd"/> encodes to it — a transcript copied
    /// from another machine, or a cwd on a different drive.</summary>
    public static string? RootFor(string slug, string cwd)
    {
        if (slug.Length == 0 || cwd.Length == 0) return null;
        try
        {
            var dir = new DirectoryInfo(cwd.TrimEnd('\\', '/'));
            for (int up = 0; dir is not null && up < MaxDepth; up++, dir = dir.Parent)
                if (string.Equals(Encode(dir.FullName), slug, StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;
        }
        catch { /* an unusable cwd is simply no answer */ }
        return null;
    }

    /// <summary>The folder <em>name</em> of that root — what a chart legend or a project list shows.
    /// Falls back to <see cref="Tail"/> when the slug cannot be verified against this cwd.</summary>
    public static string NameFor(string slug, string cwd)
    {
        if (RootFor(slug, cwd) is not { Length: > 0 } root) return Tail(slug);
        string name = Path.GetFileName(root.TrimEnd('\\', '/'));
        return name.Length > 0 ? name : root;
    }

    /// <summary>
    /// Resolve a slug back to a real directory by <b>probing the filesystem</b>, for when there is no
    /// recorded <c>cwd</c> to verify against. Tries every split of the ambiguous hyphens, shortest
    /// segment first, and lets <see cref="Directory.Exists(string)"/> decide — with backtracking, so
    /// <c>viglet-model-catalog</c> still resolves when <c>viglet\model</c> happens to exist too.
    /// </summary>
    public static bool TryProbe(string slug, out string path)
    {
        path = "";
        if (!IsDrivePrefixed(slug)) return false;

        string[] tokens = slug[3..].Split('-', StringSplitOptions.RemoveEmptyEntries);
        string root = slug[0] + ":\\";
        if (tokens.Length == 0)
        {
            path = root;
            return Directory.Exists(path);
        }
        return Directory.Exists(root) && Descend(root, tokens, 0, out path);
    }

    private static bool Descend(string current, string[] tokens, int i, out string result)
    {
        result = current;
        if (i >= tokens.Length) return true;

        for (int k = i; k < tokens.Length; k++)
        {
            string name = string.Join('-', tokens[i..(k + 1)]);
            string next = Path.Combine(current, name);
            if (Directory.Exists(next) && Descend(next, tokens, k + 1, out result)) return true;
        }
        result = "";
        return false;
    }

    /// <summary>
    /// The naive reading of a slug, used <b>only to report an unresolvable one</b>: every <c>-</c>
    /// after the drive becomes a separator. Empty when the slug isn't drive-prefixed at all (a shared
    /// or virtual project dir, which is not a path and must not be reported as a missing one).
    /// </summary>
    public static string Literal(string slug)
        => IsDrivePrefixed(slug)
            ? slug[0] + ":\\" + string.Join('\\', slug[3..].Split('-', StringSplitOptions.RemoveEmptyEntries))
            : "";

    /// <summary>Last resort for a <em>display</em> name when no cwd was ever recorded: the slug's last
    /// segment. Ambiguous by construction — the encoding cannot tell a separator from a hyphen in the
    /// folder's own name — so it is only ever used when the authoritative answer is missing.</summary>
    public static string Tail(string slug)
    {
        int dash = slug.LastIndexOf('-');
        return dash >= 0 && dash < slug.Length - 1 ? slug[(dash + 1)..] : slug;
    }

    // "d--…": a drive letter, its colon, and the separator that followed it.
    private static bool IsDrivePrefixed(string slug)
        => slug.Length >= 4 && slug[1] == '-' && slug[2] == '-' && char.IsLetter(slug[0]);
}
