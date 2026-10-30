using System.Runtime.InteropServices;

namespace ClaudeTray;

/// <summary>
/// Whether this process runs inside an MSIX package — the Microsoft Store build (T409). Asked of the
/// process at runtime, never set by a build flag, so the one exe `--selftest` checks is the one that ships
/// both ways.
///
/// <para>Two things change under a package. The self-updater stands down: the Store owns updates, and an
/// Inno Setup installer run over a <c>WindowsApps</c> folder would land beside the package rather than
/// replace it. And the status-line command names the package's execution alias instead of
/// <see cref="Environment.ProcessPath"/>, which is a <c>WindowsApps</c> path nothing outside the package
/// may launch.</para>
/// </summary>
internal static class PackageIdentity
{
    /// <summary>The <c>uap5:AppExecutionAlias</c> the package declares, on PATH for every shell.</summary>
    public const string Alias = "ClaudeTray.exe";

    private const int ErrorInsufficientBuffer = 122;

    public static bool IsPackaged { get; } = Detect();

    private static bool Detect()
    {
        try
        {
            // A zero-length buffer answers ERROR_INSUFFICIENT_BUFFER when there is a package to name, and
            // APPMODEL_ERROR_NO_PACKAGE when there is none. Anything else is read as unpackaged, so a
            // failure here can never switch the updater off for a copy that still needs it.
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) == ErrorInsufficientBuffer;
        }
        catch (EntryPointNotFoundException) { return false; }
    }

    /// <summary>What the status-line command runs: the alias when packaged, this exe's path otherwise.</summary>
    public static string StatusLineExe(bool packaged, string? processPath) =>
        packaged ? Alias : processPath ?? Alias;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);
}
