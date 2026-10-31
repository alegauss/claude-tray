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

    /// <summary><c>ActivationKind.StartupTask</c>: the manifest's startup task launched us at sign-in.</summary>
    public const int StartupTaskKind = 1020;

    /// <summary>
    /// Whether this launch should open the window rather than start silently in the tray.
    ///
    /// <para>The Store's "Open" button, the Start menu tile and the alias all start the package the same
    /// way the startup task does, and a tray icon that appears in the overflow is invisible: the user
    /// clicked, nothing happened. Only the startup task is meant to be silent, and the package is the one
    /// place that says which it was. An unpackaged copy keeps starting silently — the Run key, and the
    /// installer relaunching after a self-update, both pass through here — and a kind that cannot be read
    /// is treated the same, so a failure never opens a window at sign-in.</para>
    /// </summary>
    public static bool OpensWindow(bool packaged, int? activationKind) =>
        packaged && activationKind is { } kind && kind != StartupTaskKind;

    /// <summary>How the package was activated, or null when it is not packaged or the answer is unreadable.</summary>
    public static int? ActivationKind()
    {
        if (!IsPackaged) return null;
        IntPtr name = IntPtr.Zero;
        try
        {
            const string AppInstance = "Windows.ApplicationModel.AppInstance";
            if (WindowsCreateString(AppInstance, AppInstance.Length, out name) != 0) return null;
            Guid iid = typeof(IAppInstanceStatics).GUID;
            if (RoGetActivationFactory(name, ref iid, out IAppInstanceStatics statics) != 0) return null;
            if (statics.GetActivatedEventArgs(out IActivatedEventArgs? args) != 0 || args is null) return null;
            return args.GetKind(out int kind) == 0 ? kind : null;
        }
        catch { return null; }
        finally { if (name != IntPtr.Zero) WindowsDeleteString(name); }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, char[]? packageFullName);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid,
        [MarshalAs(UnmanagedType.Interface)] out IAppInstanceStatics factory);

    // The two WinRT interfaces this needs, declared by hand rather than taking on the Windows SDK
    // projection for one call. IInspectable's three methods are placeholders that keep the slots aligned.
    [ComImport, Guid("9d11e77f-9ea6-47af-a6ec-46784c5ba254"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAppInstanceStatics
    {
        void GetIids();
        void GetRuntimeClassName();
        void GetTrustLevel();
        void GetRecommendedInstance();
        [PreserveSig] int GetActivatedEventArgs([MarshalAs(UnmanagedType.Interface)] out IActivatedEventArgs? args);
    }

    [ComImport, Guid("cf651713-cd08-4fd8-b697-a281b6544e2e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivatedEventArgs
    {
        void GetIids();
        void GetRuntimeClassName();
        void GetTrustLevel();
        [PreserveSig] int GetKind(out int kind);
    }
}
