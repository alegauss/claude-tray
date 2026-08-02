using System.Text.Json;

namespace ClaudeTray;

/// <summary>One captured reading: when it was taken, and the rate-limit headers exactly as sent.</summary>
internal readonly record struct ProbeEntry(double T, IReadOnlyDictionary<string, string> Headers)
{
    public string? Get(string name) => Headers.TryGetValue(name, out string? v) ? v : null;
}

/// <summary>
/// The instrument behind T181: a verbatim record of the <c>anthropic-ratelimit-*</c> headers, written to
/// <c>header-probe.jsonl</c> in the profile's own store.
///
/// <para><b>Why a log and not a command.</b> The question is what the overage percentage denominates, and
/// the reading that settles it can only be taken from an account that is actually in overage — a state
/// that arrives unannounced, usually while nobody is running a CLI flag. A one-shot dump answers "what do
/// the headers say right now", which for an account inside its quota is the uninteresting case. So the
/// tray records instead, and the transition is caught whenever it happens.</para>
///
/// <para><b>Why only on a change.</b> At the default cadence a week of polling is thousands of identical
/// lines. A line is written only when the <em>shape</em> moves — a header name appears or disappears, the
/// status string changes, or the overage figure crosses zero — which is precisely the set of moments the
/// question is about. The first reading is always kept, as the baseline the rest are changes from.</para>
///
/// <para><b>What it may hold.</b> Quota metadata: header names and values. No message content, no token —
/// a credential never appears in a response header. This is the same material the tooltip already shows,
/// kept unparsed. Bounded at <see cref="MaxEntries"/> lines, oldest dropped, so it cannot grow without
/// limit on a machine where something flaps.</para>
/// </summary>
internal static class HeaderProbe
{
    /// <summary>Enough to hold every transition of a long overage spell and still be read in one screen.</summary>
    public const int MaxEntries = 500;

    private static string FilePath(string profileKey) =>
        ProfileStore.PathFor(profileKey, "header-probe.jsonl");

    /// <summary>The last shape written, per profile — so the common case (nothing changed) costs no IO
    /// after the first poll of the process.</summary>
    private static readonly Dictionary<string, string> LastShape = new();

    /// <summary>Record this reading if it differs in shape from the previous one. Best-effort: a failure
    /// here must never disturb a poll.</summary>
    /// <returns>true when a line was written.</returns>
    public static bool Record(string profileKey, long nowUnix, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0) return false;   // a header-less response is an error, not a reading
        try
        {
            string shape = Shape(headers);
            if (!LastShape.TryGetValue(profileKey, out string? known))
            {
                // First poll of this process: the file, not memory, says what was last seen — otherwise
                // every restart writes a duplicate baseline and the log becomes a restart counter.
                known = LastRecordedShape(profileKey);
                if (known != null) LastShape[profileKey] = known;
            }
            if (known == shape) return false;

            string path = FilePath(profileKey);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, Render(nowUnix, headers) + Environment.NewLine);
            LastShape[profileKey] = shape;
            Trim(path);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Every captured reading, oldest first.</summary>
    public static List<ProbeEntry> Load(string profileKey)
    {
        var list = new List<ProbeEntry>();
        try
        {
            string path = FilePath(profileKey);
            if (!File.Exists(path)) return list;
            foreach (string line in File.ReadLines(path))
                if (line.Length > 0 && TryParse(line, out ProbeEntry e))
                    list.Add(e);
        }
        catch { /* a partial read just yields fewer readings */ }
        list.Sort((a, b) => a.T.CompareTo(b.T));
        return list;
    }

    /// <summary>What makes one reading materially different from the one before it. Not the values: the
    /// utilizations move on every poll and would make every line a "change", which is the log this is
    /// designed not to be. The names, the status and whether overage is being consumed at all.</summary>
    public static string Shape(IReadOnlyDictionary<string, string> headers)
    {
        var names = new List<string>(headers.Keys);
        names.Sort(StringComparer.OrdinalIgnoreCase);

        string status = headers.TryGetValue("anthropic-ratelimit-unified-5h-status", out string? s) ? s : "";
        bool overage = headers.TryGetValue("anthropic-ratelimit-unified-overage-utilization", out string? o)
                       && double.TryParse(o, System.Globalization.NumberStyles.Float,
                                          System.Globalization.CultureInfo.InvariantCulture, out double v)
                       && v > 0;

        return string.Join(",", names) + "|" + status + "|" + (overage ? "1" : "0");
    }

    private static string? LastRecordedShape(string profileKey)
    {
        List<ProbeEntry> all = Load(profileKey);
        return all.Count == 0 ? null : Shape(all[^1].Headers);
    }

    private static string Render(long nowUnix, IReadOnlyDictionary<string, string> headers)
    {
        var buf = new System.Text.StringBuilder();
        buf.Append(FormattableString.Invariant($"{{\"t\":{nowUnix},\"h\":{{"));
        bool first = true;
        foreach (var kv in headers)
        {
            if (!first) buf.Append(',');
            first = false;
            buf.Append(JsonSerializer.Serialize(kv.Key)).Append(':').Append(JsonSerializer.Serialize(kv.Value));
        }
        return buf.Append("}}").ToString();
    }

    private static bool TryParse(string line, out ProbeEntry e)
    {
        e = default;
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (!root.TryGetProperty("t", out var t) || t.ValueKind != JsonValueKind.Number) return false;
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("h", out var h) && h.ValueKind == JsonValueKind.Object)
                foreach (JsonProperty p in h.EnumerateObject())
                    map[p.Name] = p.Value.GetString() ?? "";
            e = new ProbeEntry(t.GetDouble(), map);
            return e.T > 0;
        }
        catch { return false; }
    }

    private static void Trim(string path)
    {
        var lines = new List<string>(File.ReadLines(path));
        if (lines.Count <= MaxEntries) return;
        File.WriteAllLines(path, lines.GetRange(lines.Count - MaxEntries, MaxEntries));
    }
}
