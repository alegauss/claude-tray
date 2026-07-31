using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace ClaudeTray;

/// <summary>A newer release found on GitHub, ready to download and install.</summary>
internal sealed class UpdateInfo
{
    public required Version Version;   // normalized Major.Minor.Build
    public required string Tag;        // e.g. "v1.3.0"
    public required string SetupUrl;   // browser_download_url of ClaudeTray-Setup.exe
}

/// <summary>
/// Self-update via GitHub Releases: checks the repo's latest release, compares it to the
/// running version, and (on demand) downloads the Inno Setup installer to %TEMP% and runs it
/// silently. The installer shares this app's AppId, so it upgrades in place and relaunches.
/// </summary>
internal sealed class Updater
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/alegauss/claude-tray/releases/latest";
    private const string SetupAssetName = "ClaudeTray-Setup.exe";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public Updater()
    {
        // GitHub's API rejects requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClaudeTray", CurrentVersion.ToString()));
    }

    public static Version CurrentVersion
    {
        get
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    /// <summary>Query GitHub for the latest release; return it only if newer than the running build.</summary>
    public async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            using var doc = JsonDocument.Parse(await _http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false));
            JsonElement root = doc.RootElement;

            string? tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out Version latest))
                return null;
            if (latest <= CurrentVersion)
                return null;

            string? url = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                    if (a.TryGetProperty("name", out var n) &&
                        string.Equals(n.GetString(), SetupAssetName, StringComparison.OrdinalIgnoreCase) &&
                        a.TryGetProperty("browser_download_url", out var u))
                    {
                        url = u.GetString();
                        break;
                    }

            return url == null ? null : new UpdateInfo { Version = latest, Tag = tag!, SetupUrl = url };
        }
        catch
        {
            return null; // offline, rate-limited, or no release yet — silently skip this check.
        }
    }

    /// <summary>Download the installer to a temp file and return its path.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info)
    {
        string path = Path.Combine(Path.GetTempPath(), $"ClaudeTray-Setup-{info.Tag}.exe");
        byte[] bytes = await _http.GetByteArrayAsync(info.SetupUrl).ConfigureAwait(false);
        await File.WriteAllBytesAsync(path, bytes).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Launch the downloaded installer silently. The caller must exit the app immediately so the
    /// running .exe is unlocked; the installer relaunches it once the upgrade completes.
    /// </summary>
    public static void RunInstaller(string setupPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(setupPath)
        {
            // SILENT shows just a progress bar (no wizard); the installer closes the old
            // instance, replaces files, and relaunches via the [Run] section.
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true,
        };
        System.Diagnostics.Process.Start(psi);
    }

    // Accept tags like "v1.3.0", "1.3", or even a typo'd "v.1.3.8" and normalize to a 3-part
    // Major.Minor.Build version. Any leading non-digit prefix (the "v", a stray ".", whitespace) is
    // skipped so a tag-naming slip can't silently break update detection.
    private static bool TryParseVersion(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        string s = tag.Trim();
        int start = 0;
        while (start < s.Length && !char.IsDigit(s[start]))
            start++;
        if (start == s.Length)
            return false;

        string[] parts = s[start..].Split('.');
        if (!int.TryParse(parts[0], out _))
            return false;

        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out int n) && n >= 0 ? n : 0;
        version = new Version(Part(0), Part(1), Part(2));
        return true;
    }
}
