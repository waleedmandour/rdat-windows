using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.Views;

namespace RDAT.Copilot.Desktop;

/// <summary>
/// The main application window. Sets up the WinUI 3 chrome with
/// Mica backdrop, custom title bar, and frame-based navigation.
/// Phase 5: Added multi-window support via OpenNewWindow() and Ctrl+N shortcut.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigation;
    private readonly ILogger<MainWindow> _logger;
    private static int _windowCounter;

    public MainWindow()
    {
        this.InitializeComponent();

        // Configure window properties
        AppWindow appWindow = GetAppWindow();
        if (appWindow is not null)
        {
            appWindow.Title = "RDAT Copilot — مساعد الترجمة الذكي";
            appWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900));
            appWindow.SetIcon(null); // Use exe icon
        }

        // Set Mica backdrop
        this.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop()
        {
            Kind = Microsoft.UI.Xaml.Media.MicaKind.Base
        };

        // Extend content into title bar
        this.ExtendsContentIntoTitleBar = true;
        this.SetTitleBar(null); // Let the Grid handle it

        // Get navigation service and navigate to workspace
        _navigation = App.Services.GetRequiredService<NavigationService>();
        _logger = App.Services.GetRequiredService<ILogger<MainWindow>>();
        _navigation.Frame = RootFrame;
        _navigation.NavigateTo<WorkspacePage>();

        // Phase 5: Register Ctrl+N keyboard shortcut for new window
        RegisterKeyboardShortcuts();
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }

    /// <summary>
    /// Opens a new MainWindow instance as a separate window.
    /// Each window gets its own Frame and navigation context.
    /// </summary>
    public void OpenNewWindow()
    {
        Interlocked.Increment(ref _windowCounter);
        var windowNumber = _windowCounter;

        _logger.LogInformation("[RDAT] Opening new window #{Number}", windowNumber);

        var newWindow = new MainWindow
        {
            Title = $"RDAT Copilot — Window {windowNumber}"
        };

        var appWindow = newWindow.GetAppWindow();
        if (appWindow is not null)
        {
            appWindow.Title = $"RDAT Copilot — Window {windowNumber} — مساعد الترجمة الذكي";
            // Offset new windows slightly
            var offset = (windowNumber * 30) % 200;
            appWindow.Move(new Windows.Graphics.PointInt32(100 + offset, 100 + offset));
        }

        newWindow.Activate();
        _logger.LogInformation("[RDAT] New window #{Number} activated", windowNumber);
    }

    /// <summary>
    /// Registers keyboard shortcuts for the main window.
    /// Phase 5: Ctrl+N opens a new window.
    /// </summary>
    private void RegisterKeyboardShortcuts()
    {
        this.Content.KeyDown += OnKeyDown;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Ctrl+N: New Window
        if (e.Key == Windows.System.VirtualKey.N &&
            Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Input.Core.VirtualKeyStates.Down))
        {
            OpenNewWindow();
            e.Handled = true;
        }
    }
}
