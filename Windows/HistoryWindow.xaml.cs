using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using SharpLensAI.Models;
using SharpLensAI.Services;

namespace SharpLensAI;

public partial class HistoryWindow : Window
{
    private readonly HistoryService _historyService = new();
    private readonly ObservableCollection<HistoryEntry> _entries = new();

    public HistoryWindow()
    {
        InitializeComponent();
        HistoryList.ItemsSource = _entries;
        Loaded += HistoryWindow_Loaded;
    }

    private async void HistoryWindow_Loaded(object sender, RoutedEventArgs e) => await RefreshHistoryAsync();

    private async Task RefreshHistoryAsync()
    {
        try
        {
            var selectedId = (HistoryList.SelectedItem as HistoryEntry)?.Id;
            var entries = await _historyService.GetRecentAsync(300);
            _entries.Clear();
            foreach (var entry in entries) _entries.Add(entry);

            if (_entries.Count == 0)
            {
                RecognizedTextBox.Clear();
                HistoryAnswerViewer.Markdown = "История пока пуста.";
                return;
            }

            var selectedIndex = selectedId.HasValue
                ? _entries.IndexOf(FindById(selectedId.Value)!)
                : -1;
            HistoryList.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось загрузить историю", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private HistoryEntry? FindById(Guid id)
    {
        foreach (var item in _entries)
            if (item.Id == id) return item;
        return null;
    }

    private void HistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryList.SelectedItem is not HistoryEntry entry) return;
        RecognizedTextBox.Text = entry.RecognizedText;
        HistoryAnswerViewer.Markdown = string.IsNullOrWhiteSpace(entry.Answer) ? "(Пустой ответ)" : entry.Answer;
    }

    private async void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(this, "Удалить всю локальную историю запросов?", "Очистить историю",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _historyService.ClearAsync();
            _entries.Clear();
            RecognizedTextBox.Clear();
            HistoryAnswerViewer.Markdown = "История пока пуста.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось очистить историю", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
