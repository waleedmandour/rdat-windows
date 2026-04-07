using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using RDAT.Copilot.Desktop.Services;
using RDAT.Copilot.Desktop.Views;

namespace RDAT.Copilot.Desktop;

/// <summary>
/// The main application window. Sets up the WinUI 3 chrome with
/// Mica backdrop, custom title bar, and frame-based navigation.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly NavigationService _navigation;

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
        _navigation.Frame = RootFrame;
        _navigation.NavigateTo<WorkspacePage>();
    }

    private AppWindow GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(windowId);
    }
}
