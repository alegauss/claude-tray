namespace ClaudeTray;

/// <summary>
/// Cheap, defensible token estimation for the markdown files Claude Code loads into context
/// (<c>CLAUDE.md</c>, <c>AGENTS.md</c>, <c>MEMORY.md</c>, memory files, skill frontmatter).
///
/// There is no tokenizer on this machine and shipping one would break the zero-dependency
/// single-exe story, so we estimate from characters — but not with one flat divisor. Markdown
/// prose, fenced code and pipe tables tokenize at visibly different densities (code and table
/// punctuation each become their own token far more often than letters do), and the files this
/// app measures are a mix of all three, so each class is classified line-by-line and divided by its
/// own ratio.
///
/// The check on all of this is <c>--context --calibrate</c>, which measures real startup contexts
/// from transcripts and reports how far the estimate plus the (unscannable) base overhead lands
/// from them. On the dev machine: within ±15% for 23 of 27 projects, median error 6%.
///
/// Every number this produces is an <em>estimate</em> and must be rendered as one ("≈4.9k"),
/// never as a precise count.
/// </summary>
internal static class TokenEstimate
{
    // Characters per token, per content class. Prose is the well-known ~3.5–4 for English
    // markdown; code fences run denser because operators, brackets and identifiers fragment;
    // table rows are the densest of all — a `|---|---|` separator is almost one token per char.
    //
    // These were swept against the transcript calibration (3.75/2.95/2.60 vs 3.40/2.70/2.40 vs
    // 3.10/2.50/2.20) and it barely moved: median error 6.2% / 6.8% / 6.5%, within-band 23 / 23 / 24
    // of 27 projects. The invisible base overhead absorbs a uniform scale change, so that fit cannot
    // pick the divisors — which is why they stay at the values with an independent justification
    // rather than at whatever the sweep happened to favour by one project.
    internal const double ProseCharsPerToken = 3.75;
    internal const double CodeCharsPerToken = 2.95;
    internal const double TableCharsPerToken = 2.60;

    /// <summary>Estimated tokens for a whole file; 0 if it can't be read.</summary>
    public static int OfFile(string path)
    {
        try { return Of(File.ReadAllText(path)); }
        catch { return 0; }
    }

    /// <summary>Estimated tokens for a string (a file body, or a single skill description).</summary>
    public static int Of(string? text) => (int)Math.Round(OfExact(text));

    /// <summary>Unrounded estimate — used when summing many small strings before rounding once.</summary>
    public static double OfExact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;

        double prose = 0, code = 0, table = 0;
        bool inFence = false;

        // Walk the text line by line over spans — no string allocated per line, which matters
        // when the scanner runs this over several hundred files on a poll thread.
        ReadOnlySpan<char> rest = text;
        while (!rest.IsEmpty)
        {
            int nl = rest.IndexOf('\n');
            ReadOnlySpan<char> line = nl < 0 ? rest : rest[..nl];
            rest = nl < 0 ? default : rest[(nl + 1)..];

            // +1 for the newline the line carries — it is a real token boundary, and over a
            // 500-line CLAUDE.md ignoring it would understate the file by a noticeable margin.
            int len = line.Length + (nl < 0 ? 0 : 1);
            ReadOnlySpan<char> trimmed = line.Trim();

            if (IsFence(trimmed))
            {
                inFence = !inFence;
                code += len;           // the fence line itself is code punctuation
                continue;
            }

            if (inFence) code += len;
            else if (IsTableRow(trimmed)) table += len;
            else prose += len;
        }

        return prose / ProseCharsPerToken + code / CodeCharsPerToken + table / TableCharsPerToken;
    }

    // ``` or ~~~ (three or more), optionally followed by a language tag.
    private static bool IsFence(ReadOnlySpan<char> t)
        => t.StartsWith("```") || t.StartsWith("~~~");

    // A pipe table row: starts with '|', or is an inner row with at least two pipes. Deliberately
    // loose — misclassifying a prose line with two pipes costs a few tokens, while missing a 40-row
    // table (this repo's AGENTS.md is mostly tables) costs a lot.
    private static bool IsTableRow(ReadOnlySpan<char> t)
    {
        if (t.Length == 0) return false;
        if (t[0] == '|') return true;
        int pipes = 0;
        foreach (char c in t)
            if (c == '|' && ++pipes >= 2) return true;
        return false;
    }

    /// <summary>
    /// Compact display form for an estimate: "≈820", "≈4.9k", "≈35k". Invariant like every other number
    /// (<see cref="Nums"/>), and with a second reason of its own — a decimal comma in "≈4,9k" reads as a
    /// thousands separator, which is the one misreading a token count can't afford, and the reason a
    /// per-surface split of the convention was refused in T216.
    /// </summary>
    public static string Format(int tokens)
    {
        if (tokens < 1000) return "≈" + Nums.Of(tokens, "0");
        if (tokens < 10_000) return "≈" + Nums.Of(tokens / 1000.0) + "k";
        return "≈" + Nums.Of(tokens / 1000.0, "0") + "k";
    }
}
