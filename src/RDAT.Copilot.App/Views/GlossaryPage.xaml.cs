using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RDAT.Copilot.App.Views;

public sealed partial class GlossaryPage : Page
{
    // Use a distinct display model to avoid collision with Core.Models.GlossaryEntry
    public ObservableCollection<GlossaryDisplayItem> GlossaryEntries { get; } = new();

    public GlossaryPage()
    {
        this.InitializeComponent();
        LoadSampleData();
        GlossaryListView.ItemsSource = GlossaryEntries;
    }

    private void LoadSampleData()
    {
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Translation Memory", Target = "ذاكرة الترجمة", Direction = "En-Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Glossary", Target = "المسرد", Direction = "En-Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Machine Translation", Target = "الترجمة الآلية", Direction = "En-Ar", Domain = "NLP" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Quality Assurance", Target = "ضمان الجودة", Direction = "En-Ar", Domain = "General" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Source Language", Target = "لغة المصدر", Direction = "En-Ar", Domain = "Linguistics" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Target Language", Target = "اللغة الهدف", Direction = "En-Ar", Domain = "Linguistics" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Segment", Target = "القطعة", Direction = "En-Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Alignment", Target = "المحاذاة", Direction = "En-Ar", Domain = "NLP" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Termbase", Target = "قاعدة المصطلحات", Direction = "En-Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "Leverage", Target = "الاستفادة", Direction = "En-Ar", Domain = "Localization" });
    }

    private void AddEntryButton_Click(object sender, RoutedEventArgs e)
    {
        GlossaryEntries.Add(new GlossaryDisplayItem { Source = "New Term", Target = "مصطلح جديد", Direction = "En-Ar", Domain = "General" });
    }

    private void EditEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (GlossaryListView.SelectedItem is GlossaryDisplayItem entry)
        {
            entry.Direction = entry.Direction == "En-Ar" ? "Ar-En" : "En-Ar";
            // Refresh the item display
            var idx = GlossaryEntries.IndexOf(entry);
            GlossaryEntries[idx] = entry with { Direction = entry.Direction };
        }
    }

    private void DeleteEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (GlossaryListView.SelectedItem is GlossaryDisplayItem entry)
        {
            GlossaryEntries.Remove(entry);
        }
    }
}

/// <summary>
/// Display-only model for the glossary DataGrid.
/// Separately named to avoid collision with RDAT.Copilot.Core.Models.GlossaryEntry.
/// </summary>
public sealed record GlossaryDisplayItem
{
    public string Source { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;
    public string Direction { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
}
