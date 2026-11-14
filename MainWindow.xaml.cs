using System.Windows;
using System.Windows.Controls;
using SharpLensAI.Services;
using SharpLensAI.Models;

namespace SharpLensAI;
public partial class MainWindow : Window
{
    private readonly GlobalHotkeyManager _hotkey = new();
    private readonly OcrService _ocr = new();
    private readonly LlmService _llm = new();
    private readonly HistoryService _history = new();
    private CancellationTokenSource? _request;
    private bool _busy;
    private bool _dialogOpen;
    private string _answer = "";
    private string _ocrLanguage = "ручной ввод";
    public MainWindow()
    {
        InitializeComponent();
        AiTaskComboBox.ItemsSource = PromptCatalog.Names;
        AiTaskComboBox.SelectedIndex = 0;
        var settings = SettingsManager.Load();
        if (SettingsManager.LoadWarning != null) StatusText.Text = SettingsManager.LoadWarning;
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey.HotkeyPressed += (_, _) => Dispatcher.BeginInvoke(new Action(async () => await CaptureAsync()));
        ConfigureHotkey(SettingsManager.Load());
    }
    public bool ConfigureHotkey(AppSettings settings)
    {
        if (!settings.EnableGlobalHotkey) _hotkey.Unregister();
        bool ok = !settings.EnableGlobalHotkey || _hotkey.Register(this, settings.HotkeyModifiers, settings.HotkeyKey);
        HotkeyLabel.Text = !settings.EnableGlobalHotkey ? "Горячая клавиша выключена" : ok ? HotkeyGesture.Format(settings.HotkeyModifiers, settings.HotkeyKey) : "Сочетание занято — измените настройки";
        return ok;
    }
    public void SuspendHotkey() => _hotkey.Unregister();
    protected override void OnClosed(EventArgs e) { _request?.Cancel(); _hotkey.Dispose(); base.OnClosed(e); }
    private void SetBusy(bool value, string status)
    {
        _busy = value;
        CaptureButton.IsEnabled = AskAiButton.IsEnabled = SettingsButton.IsEnabled = HistoryButton.IsEnabled = !value;
        Progress.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
        StatusText.Text = status;
    }
    private async void CaptureButton_Click(object sender, RoutedEventArgs e) => await CaptureAsync();
    private async Task CaptureAsync()
    {
        if (_busy || _dialogOpen)
        {
            StatusText.Text = "Захват пока недоступен: завершите текущую операцию или закройте открытое окно.";
            return;
        }
        SetBusy(true, "Выделите область. Отпустите кнопку мыши — захватить, Esc — отменить.");
        var previousState = WindowState;
        try
        {
            Hide();
            await Task.Delay(200);
            using var screen = ScreenCapture.CaptureDesktop();
            var overlay = new SnippingWindow(screen);
            bool accepted = overlay.ShowDialog() == true;
            Show(); WindowState = previousState == WindowState.Minimized ? WindowState.Normal : previousState; Activate();
            if (!accepted || overlay.CapturedBitmap == null) { StatusText.Text = "Захват отменён."; return; }
            using var bitmap = overlay.CapturedBitmap;
            StatusText.Text = "Распознавание текста…";
            string language = LanguageBox.SelectedIndex switch { 1 => "ru", 2 => "en", _ => "auto" };
            var result = await _ocr.RecognizeTextAsync(bitmap, language);
            _ocrLanguage = result.UsedLanguage;
            if (string.IsNullOrWhiteSpace(result.Text)) { StatusText.Text = "Текст не найден. Выберите более чёткую область и повторите захват."; return; }
            OcrResultTextBox.Text = result.Text;
            StatusText.Text = $"Распознано: {result.UsedLanguage}. Проверьте текст, особенно отступы и символы кода. {result.Warning}";
        }
        catch (Exception ex) { StatusText.Text = $"Не удалось распознать: {ex.Message}"; }
        finally { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; SetBusy(false, StatusText.Text); }
    }
    private async void AskAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        string text = OcrResultTextBox.Text;
        int mode = AiTaskComboBox.SelectedIndex;
        string prompt = PromptCatalog.Build(mode, CustomPromptTextBox.Text);
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(prompt)) { StatusText.Text = "Введите текст и инструкцию для обработки."; return; }
        var settings = SettingsManager.Load();
        SetBusy(true, "Ожидание ответа ИИ…");
        _request = new(); CancelButton.IsEnabled = true;
        try
        {
            string answer = await _llm.AskAsync(settings, prompt, text, _request.Token);
            _answer = answer;
            AnswerViewer.Document = MarkdownRenderer.Render(answer);
            StatusText.Text = "Ответ готов.";
            if (settings.SaveHistory)
            {
                try { await _history.AddAsync(new HistoryEntry { Prompt = prompt, RecognizedText = text, Answer = answer, OcrLanguage = _ocrLanguage, Mode = mode, Provider = settings.Provider, Model = settings.Model }); }
                catch { StatusText.Text += " Не удалось сохранить историю. Ответ доступен для копирования."; }
            }
            if (settings.AutoCopyAnswer && !await ClipboardHelper.SetTextWithRetryAsync(answer)) StatusText.Text += " Буфер обмена занят; скопируйте ответ вручную.";
        }
        catch (OperationCanceledException) { StatusText.Text = "Запрос отменён. Можно повторить обработку."; }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { _request.Dispose(); _request = null; CancelButton.IsEnabled = false; SetBusy(false, StatusText.Text); }
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e) => _request?.Cancel();
    private async void CopyButton_Click(object sender, RoutedEventArgs e) => StatusText.Text = await ClipboardHelper.SetTextWithRetryAsync(_answer) ? "Ответ скопирован." : "Нет ответа или буфер обмена занят.";
    private void AiTaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (CustomPromptTextBox != null) CustomPromptTextBox.Visibility = AiTaskComboBox.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed; }
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true;
        var original = SettingsManager.Load();
        _hotkey.Unregister(); // The editor must receive the currently assigned chord too.
        bool saved = false;
        try { saved = new SettingsWindow { Owner = this }.ShowDialog() == true; }
        finally
        {
            _dialogOpen = false;
            if (!ConfigureHotkey(saved ? SettingsManager.Load() : original))
                StatusText.Text = "Не удалось включить горячую клавишу: сочетание занято. Выберите другое в настройках.";
        }
    }
    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true;
        try
        {
            var window = new HistoryWindow { Owner = this };
            if (window.ShowDialog() == true && window.SelectedEntry is { } entry)
            {
                OcrResultTextBox.Text = entry.RecognizedText; _answer = entry.Answer; _ocrLanguage = entry.OcrLanguage;
                AnswerViewer.Document = MarkdownRenderer.Render(_answer);
                AiTaskComboBox.SelectedIndex = Math.Clamp(entry.Mode, 0, 4); CustomPromptTextBox.Text = entry.Prompt;
                StatusText.Text = "Запрос восстановлен из истории. Можно обработать повторно.";
            }
        }
        finally { _dialogOpen = false; }
    }
}
