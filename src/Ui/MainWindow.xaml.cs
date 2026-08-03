using System.Windows;
using System.Windows.Controls;

namespace ClaudeTray;

/// <summary>
/// The app's single window: a nav strip over one of three destinations — the pacing report
/// (<see cref="StatisticsPage"/>), the context load inspector (<see cref="ContextPage"/>) and the
/// settings (<see cref="SettingsPage"/>).
///
/// <para>It replaces three separate windows opened from three tray-menu entries. The tray menu is for
/// what can be done *without* a window (which quota the icon shows, which profile it follows, refresh,
/// launch Claude Code); everything with a page to read is reached by opening this one and navigating
/// inside it.</para>
///
/// <para>A destination is built the first time it is selected and then kept, collapsed, for the life of
/// the window — the same thing the settings sidebar does with its six pages. That is what makes opening
/// the window cheap (the tray icon is clicked to read the numbers, and the context scan walks the whole
/// of <c>~/.claude</c>) while keeping a scan, a chart's history or a half-edited settings page intact
/// across a switch.</para>
/// </summary>
internal partial class MainWindow : Window
{
    internal const string DestStatistics = "Statistics";
    internal const string DestContext = "Context";
    internal const string DestSettings = "Settings";

    /// <summary>The live settings, read at the moment a settings page is built — never a snapshot taken
    /// when the window opened, which is how a menu pick made while the page sat open used to be written
    /// back over (T141, T155).</summary>
    private readonly Func<Settings> _currentSettings;
    private readonly Action<Settings, Settings> _onSave;

    private ContextPage? _context;
    private SettingsPage? _settings;

    /// <summary>The pacing report. Built with the window rather than on demand: it is the destination the
    /// tray icon opens on, and the tray pushes a fresh reading into it on every poll.</summary>
    internal StatisticsPage Statistics { get; }

    /// <param name="currentSettings">Reads the tray's live settings model (see the field).</param>
    /// <param name="onSave">Applied when the settings page saves — the tray's <c>ApplySettings</c>.</param>
    internal MainWindow(PaceSnapshot? snapshot, bool showRemaining, string? error,
                        Func<Settings> currentSettings, Action<Settings, Settings> onSave)
    {
        _currentSettings = currentSettings;
        _onSave = onSave;

        InitializeComponent();

        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                new Uri(Environment.ProcessPath ?? System.Windows.Forms.Application.ExecutablePath));
        }
        catch { /* fall back to the default window icon */ }

        Statistics = new StatisticsPage(snapshot, showRemaining, error);
        DestinationHost.Children.Add(Statistics);
        NavStatistics.IsChecked = true;   // the default destination, now that there is one to show
    }

    /// <summary>Show a destination, building it if this is its first visit. Ticking the nav strip is what
    /// actually navigates (see <see cref="Destination_Checked"/>), so callers and clicks take one path.</summary>
    internal void Navigate(string destination)
    {
        System.Windows.Controls.RadioButton nav = destination switch
        {
            DestContext => NavContext,
            DestSettings => NavSettings,
            _ => NavStatistics,
        };
        // Already there: Checked doesn't fire again, so show it directly. This is the tray reopening the
        // window on the destination it is already parked on.
        if (nav.IsChecked == true) Show(destination);
        else nav.IsChecked = true;
    }

    private void Destination_Checked(object sender, RoutedEventArgs e)
        => Show((string)((FrameworkElement)sender).Tag);

    private void Show(string destination)
    {
        UIElement page = destination switch
        {
            DestContext => _context ??= Add(new ContextPage()),
            DestSettings => _settings ??= Add(BuildSettings()),
            _ => Statistics,
        };

        foreach (UIElement child in DestinationHost.Children)
            child.Visibility = ReferenceEquals(child, page) ? Visibility.Visible : Visibility.Collapsed;
    }

    private T Add<T>(T page) where T : UIElement
    {
        DestinationHost.Children.Add(page);
        return page;
    }

    /// <summary>
    /// A settings page over the *live* model. Cancel discards by construction: the page is thrown away
    /// and a new one built from the model as it stands, rather than every control being walked back to
    /// where it started — the same reason the edit buffer is a whole-model clone (T141).
    /// </summary>
    private SettingsPage BuildSettings()
    {
        var page = new SettingsPage(_currentSettings(), _onSave);
        page.Cancelled += RebuildSettings;
        return page;
    }

    private void RebuildSettings()
    {
        if (_settings is { } old) DestinationHost.Children.Remove(old);
        _settings = Add(BuildSettings());
        Show(DestSettings);
    }
}
