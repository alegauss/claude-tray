using System.Text.Json;

namespace ClaudeTray;

/// <summary>
/// Observability over the local Claude Code session transcripts
/// (~/.claude/projects/**/*.jsonl). Aggregates the last 24h of assistant
/// requests — weighting tokens by per-model price — to reproduce the usage
/// insights Claude Code shows: how much usage came from subagent-heavy
/// sessions, how much ran at large context, and the per-model split.
/// Reads only usage/model/flags — never message content.
/// </summary>
internal sealed class InsightsData
{
    public double SubagentPct;        // share of cost from subagent (sidechain) requests
    public double HeavyContextPct;    // share of cost from requests with >150k token context
    public int Sessions;
    public int Requests;
    public List<(string model, double pct)> ByModel = new();
    public DateTime ComputedUtc;
    public string? Error;
}

internal static class UsageInsights
{
    private static readonly string ProjectsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");

    private const double ContextThreshold = 150_000;   // "large context" line, in tokens

    // Price per 1M tokens: (input, output, cacheWrite5m, cacheRead). cacheWrite = 1.25x input,
    // cacheRead = 0.1x input. Matched by substring of the model id; default = Opus-tier.
    // Internal because the context scanner prices its session-zero measurement the same way — one
    // table for the whole app, so a rate change can't be applied in one place and missed in another.
    internal static (double inp, double outp, double cw, double cr) Price(string model)
    {
        if (model.Contains("haiku")) return (1, 5, 1.25, 0.10);
        if (model.Contains("sonnet")) return (3, 15, 3.75, 0.30);
        if (model.Contains("fable") || model.Contains("mythos")) return (10, 50, 12.5, 1.0);
        return (5, 25, 6.25, 0.50); // opus / unknown
    }

    public static InsightsData Compute(DateTime nowUtc)
    {
        var data = new InsightsData { ComputedUtc = nowUtc };
        try
        {
            if (!Directory.Exists(ProjectsDir))
            {
                data.Error = L.T("insights.noTranscripts");
                return data;
            }

            DateTime cutoff = nowUtc.AddHours(-24);
            var byModel = new Dictionary<string, double>();
            var sessions = new HashSet<string>();
            double total = 0, heavyCtx = 0, subagent = 0;
            int requests = 0;

            foreach (string file in SafeWalk.Paths(ProjectsDir, "*.jsonl"))
            {
                // Skip files untouched in the window — bounds the work to recent sessions.
                if (File.GetLastWriteTimeUtc(file) < cutoff) continue;

                foreach (string line in ReadLinesSafe(file))
                {
                    if (line.Length == 0) continue;
                    if (!TryParseRecord(line, cutoff, out string model, out bool sidechain,
                                        out double cost, out double context, out string session))
                        continue;

                    total += cost;
                    requests++;
                    if (session.Length > 0) sessions.Add(session);
                    string key = ShortModel(model);
                    byModel[key] = byModel.GetValueOrDefault(key) + cost;

                    if (context > ContextThreshold) heavyCtx += cost;
                    if (sidechain) subagent += cost;
                }
            }

            data.Requests = requests;
            data.Sessions = sessions.Count;

            if (total > 0)
            {
                data.SubagentPct = subagent / total;
                data.HeavyContextPct = heavyCtx / total;
                data.ByModel = byModel
                    .Select(kv => (kv.Key, kv.Value / total))
                    .OrderByDescending(t => t.Item2)
                    .ToList();
            }

            return data;
        }
        catch (Exception e)
        {
            data.Error = e.Message;
            return data;
        }
    }

    private static IEnumerable<string> ReadLinesSafe(string file)
    {
        // A session being written to can throw on open; skip it rather than failing the whole scan.
        try { return File.ReadLines(file); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>Parse one transcript line; true only for in-window assistant records with usage.</summary>
    private static bool TryParseRecord(string line, DateTime cutoff, out string model,
        out bool sidechain, out double cost, out double context, out string session)
    {
        model = ""; sidechain = false; cost = 0; context = 0; session = "";
        try
        {
            using var doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("type", out var t) || t.GetString() != "assistant")
                return false;

            if (root.TryGetProperty("timestamp", out var ts) &&
                ts.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(ts.GetString(), null,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out DateTime when) && when < cutoff)
                return false;

            if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
                return false;
            if (!msg.TryGetProperty("usage", out var u) || u.ValueKind != JsonValueKind.Object)
                return false;

            model = msg.TryGetProperty("model", out var m) ? m.GetString() ?? "" : "";
            // "<synthetic>" marks CLI-generated messages (errors, interrupts) — not real API usage.
            if (model == "<synthetic>") return false;

            sidechain = root.TryGetProperty("isSidechain", out var sc) &&
                        sc.ValueKind == JsonValueKind.True;
            session = root.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? "" : "";

            double inp = Num(u, "input_tokens");
            double outp = Num(u, "output_tokens");
            double cw = Num(u, "cache_creation_input_tokens");
            double cr = Num(u, "cache_read_input_tokens");

            context = inp + cw + cr;
            var (pin, pout, pcw, pcr) = Price(model);
            cost = (inp * pin + outp * pout + cw * pcw + cr * pcr) / 1_000_000.0;
            return true;
        }
        catch { return false; }
    }

    private static double Num(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble() : 0;

    // "claude-opus-4-8" -> "opus-4-8"; keeps the menu compact.
    private static string ShortModel(string model)
    {
        if (string.IsNullOrEmpty(model)) return "unknown";
        return model.StartsWith("claude-") ? model[7..] : model;
    }
}
