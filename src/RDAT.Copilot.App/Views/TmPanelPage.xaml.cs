using System;
using System.Collections.ObjectModel;
using System.Linq; // CRITICAL: Fixes the 'Where' error
using Microsoft.UI.Xaml.Controls;

namespace RDAT.Copilot.App.Views;

public sealed partial class TmPanelPage : Page
{
    public ObservableCollection<TmEntry> TmEntries { get; } = new();

    public TmPanelPage()
    {
        this.InitializeComponent();
        LoadDummyData();
        TmResultsList.ItemsSource = TmEntries;
    }

    private void LoadDummyData()
    {
        TmEntries.Add(new TmEntry { Source = "The quick brown fox jumps over the lazy dog.", Target = "الثعلب البني السريع يقفز فوق الكلب الكسول.", Score = "98%" });
        TmEntries.Add(new TmEntry { Source = "Translation memory is a database of previous translations.", Target = "ذاكرة الترجمة هي قاعدة بيانات للترجمات السابقة.", Score = "95%" });
        TmEntries.Add(new TmEntry { Source = "Settings have been saved.", Target = "تم حفظ الإعدادات.", Score = "85%" });
    }

    private void TmSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            var query = sender.Text.ToLowerInvariant();
            var filtered = TmEntries.Where(e =>
                e.Source.ToLowerInvariant().Contains(query) ||
                e.Target.Contains(query)).ToList();

            TmResultsList.ItemsSource = filtered;
        }
    }

    private void TmResultsList_ItemClick(object sender, ItemClickEventArgs e)
    {
        // Handled in Workspace
    }
}

public class TmEntry
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Score { get; set; } = string.Empty;
}