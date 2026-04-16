using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RDAT.Copilot.App.Views;

public sealed partial class ShellPage : Page
{
    public ShellPage()
    {
        this.InitializeComponent();
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        NavView.SelectedItem = NavView.MenuItems[0];
        ContentFrame.Navigate(typeof(TranslationWorkspacePage));

        if (App.IsFirstLaunch)
        {
            WelcomeTip.IsOpen = true;
            App.IsFirstLaunch = false;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            ContentFrame.Navigate(tag switch
            {
                "workspace" => typeof(TranslationWorkspacePage),
                "tm" => typeof(TmPanelPage),
                "glossary" => typeof(GlossaryPage),
                "about" => typeof(AboutPage),
                _ => typeof(TranslationWorkspacePage),
            });
        }
    }

    private void UndockTmButton_Click(object sender, RoutedEventArgs e)
    {
        // Undock TM panel to separate window via AppWindow API
        var undockWindow = new Window
        {
            Title = "RDAT Copilot - Translation Memory",
            Width = 450, Height = 700,
            Content = new TmPanelPage()
        };
        undockWindow.Activate();
    }
}
