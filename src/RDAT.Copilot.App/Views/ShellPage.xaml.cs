using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;

namespace RDAT.Copilot.App.Views;

public sealed partial class ShellPage : Page
{
    // Local variable instead of relying on the App.xaml.cs
    private static bool _isFirstLaunch = true;

    public ShellPage()
    {
        this.InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(TranslationWorkspacePage));

        if (_isFirstLaunch)
        {
            WelcomeTip.IsOpen = true;
            _isFirstLaunch = false;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            
            // Switch statement based on the tags in your XAML
            switch (tag)
            {
                case "workspace":
                    ContentFrame.Navigate(typeof(TranslationWorkspacePage));
                    break;
                case "tm":
                    ContentFrame.Navigate(typeof(TmPanelPage));
                    break;
                case "glossary":
                    ContentFrame.Navigate(typeof(GlossaryPage));
                    break;
                case "about":
                    ContentFrame.Navigate(typeof(AboutPage));
                    break;
                default:
                    ContentFrame.Navigate(typeof(TranslationWorkspacePage));
                    break;
            }
        }
    }

    private void UndockTmButton_Click(object sender, RoutedEventArgs e)
    {
        // Safe WinUI 3 Window creation (No Width/Height properties here)
        var undockWindow = new Window
        {
            Title = "RDAT Copilot - Translation Memory",
            Content = new TmPanelPage()
        };
        
        undockWindow.Activate();
    }
}