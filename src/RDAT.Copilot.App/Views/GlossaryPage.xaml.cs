using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace RDAT.Copilot.App.Views;

public sealed partial class GlossaryPage : Page
{
    public ObservableCollection<GlossaryEntry> GlossaryEntries { get; } = new();

    public GlossaryPage()
    {
        this.InitializeComponent();
        LoadSampleData();
        GlossaryDataGrid.ItemsSource = GlossaryEntries;
    }

    private void LoadSampleData()
    {
        GlossaryEntries.Add(new GlossaryEntry { Source = "Translation Memory", Target = "ذاكرة الترجمة", Direction = "En→Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Glossary", Target = "المسرد", Direction = "En→Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Machine Translation", Target = "الترجمة الآلية", Direction = "En→Ar", Domain = "NLP" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Quality Assurance", Target = "ضمان الجودة", Direction = "En→Ar", Domain = "General" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Source Language", Target = "لغة المصدر", Direction = "En→Ar", Domain = "Linguistics" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Target Language", Target = "اللغة الهدف", Direction = "En→Ar", Domain = "Linguistics" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Segment", Target = "القطعة", Direction = "En→Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Alignment", Target = "المحاذاة", Direction = "En→Ar", Domain = "NLP" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Termbase", Target = "قاعدة المصطلحات", Direction = "En→Ar", Domain = "Localization" });
        GlossaryEntries.Add(new GlossaryEntry { Source = "Leverage", Target = "الاستفادة", Direction = "En→Ar", Domain = "Localization" });
    }

    private void AddEntryButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Open ContentDialog to add new glossary entry
        GlossaryEntries.Add(new GlossaryEntry { Source = "New Term", Target = "مصطلح جديد", Direction = "En→Ar", Domain = "General" });
    }

    private void EditEntryButton_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Open ContentDialog to edit selected glossary entry
        if (GlossaryDataGrid.SelectedItem is GlossaryEntry entry)
        {
            // Placeholder: cycle direction as a demo of interactivity
            entry.Direction = entry.Direction == "En→Ar" ? "Ar→En" : "En→Ar";
        }
    }

    private void DeleteEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (GlossaryDataGrid.SelectedItem is GlossaryEntry entry)
        {
            GlossaryEntries.Remove(entry);
        }
    }
}

public class GlossaryEntry
{
    public string Source { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Direction { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}
