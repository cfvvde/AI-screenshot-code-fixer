using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SharpLensAI.Services;
namespace SharpLensAI;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LocalModelStore _localModels;
    private bool _refreshingProviders;
    private ModifierKeys _modifiers;
    private Key _key;
    public SettingsWindow(AppSettings? settings = null, LocalModelStore? localModels = null)
    {
        InitializeComponent();
        _settings = settings ?? SettingsManager.Load();
        _localModels = localModels ?? new();
        RefreshLocalModels();
        ProviderBox.SelectedItem = _settings.Provider == LocalModelCatalog.ProviderId
            ? ProviderBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is LocalModel model && model.Id == _settings.Model)
            : ProviderBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is string id && id == _settings.Provider) ?? ProviderBox.Items[0];
        if (ProviderBox.SelectedItem == null) ShowMissingLocalModel();
        Activated += (_, _) => RefreshLocalModels();
        ScopeBox.SelectedIndex = _settings.GigaChatScope switch { "GIGACHAT_API_B2B" => 1, "GIGACHAT_API_CORP" => 2, _ => 0 };
        ApiKeyPasswordBox.Password = _settings.CustomApiKey;
        FolderBox.Text = _settings.FolderId; ModelBox.Text = _settings.Model; EndpointBox.Text = _settings.Endpoint;
        IamBox.IsChecked = _settings.UseIamToken; AutoCopyCheckBox.IsChecked = _settings.AutoCopyAnswer;
        HistoryBox.IsChecked = _settings.SaveHistory; EnableHotkeyCheckBox.IsChecked = _settings.EnableGlobalHotkey;
        TimeoutBox.Text = _settings.TimeoutSeconds.ToString(); TokensBox.Text = _settings.MaxTokens.ToString();
        _modifiers = _settings.HotkeyModifiers; _key = _settings.HotkeyKey; UpdateHotkey();
    }
    private LocalModel? SelectedLocalModel => (ProviderBox.SelectedItem as ComboBoxItem)?.Tag as LocalModel;
    private string SelectedProvider => SelectedLocalModel != null
        ? LocalModelCatalog.ProviderId
        : (ProviderBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "";

    private void RefreshLocalModels()
    {
        var installed = _localModels.Installed.ToArray();
        var entries = ProviderBox.Items.OfType<ComboBoxItem>().Where(item => item.Tag is LocalModel).ToArray();
        if (installed.SequenceEqual(entries.Select(item => (LocalModel)item.Tag))) return;
        var previous = ProviderBox.SelectedItem;
        _refreshingProviders = true;
        try
        {
            foreach (var item in entries)
                ProviderBox.Items.Remove(item);
            foreach (var model in installed)
                ProviderBox.Items.Insert(ProviderBox.Items.IndexOf(InstallLocalItem), new ComboBoxItem { Content = model.Title + " · локально", Tag = model });
            ProviderBox.SelectedItem = previous is ComboBoxItem { Tag: LocalModel selected }
                ? ProviderBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag is LocalModel model && model.Id == selected.Id)
                : previous;
        }
        finally { _refreshingProviders = false; }
        if (previous != null && ProviderBox.SelectedItem == null) ShowMissingLocalModel();
    }

    private void ShowMissingLocalModel()
    {
        UpdateProviderFields();
        StatusText.Text = "Выбранная локальная модель удалена. Скачайте её заново или выберите другую модель.";
    }

    private void InstallLocal_Click(object sender, RoutedEventArgs e) => OpenLocalModels();

    private void ProvidersOpened(object sender, EventArgs e) => RefreshLocalModels();

    private void OpenLocalModels()
    {
        new LocalModelsWindow(_localModels) { Owner = this }.ShowDialog();
        RefreshLocalModels();
    }

    private void ProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingProviders || StatusText == null) return;
        if (ReferenceEquals(ProviderBox.SelectedItem, InstallLocalItem))
        {
            ProviderBox.IsDropDownOpen = false;
            _refreshingProviders = true;
            try { ProviderBox.SelectedItem = e.RemovedItems.Count > 0 ? e.RemovedItems[0] : null; }
            finally { _refreshingProviders = false; }
            Dispatcher.BeginInvoke(new Action(OpenLocalModels));
            return;
        }
        UpdateProviderFields();
        if (IsLoaded && ProviderBox.SelectedItem != null)
        {
            ApiKeyPasswordBox.Clear();
            ModelBox.Text = SelectedProvider switch { "Yandex" => "yandexgpt/latest", "OpenAI" => "gpt-4.1-mini", "GigaChat" => "GigaChat", "Gemini" => "gemini-3.5-flash-lite", "OpenRouter" => "openai/gpt-4.1-mini", _ => "" };
            StatusText.Text = SelectedLocalModel != null ? "" : "Провайдер изменён. Введите его ключ и проверьте модель.";
        }
    }

    private void UpdateProviderFields()
    {
        string provider = SelectedProvider;
        bool showRemoteFields = provider.Length > 0 && provider != LocalModelCatalog.ProviderId;
        KeyPanel.Visibility = ModelPanel.Visibility = KeyStorageHint.Visibility = showRemoteFields ? Visibility.Visible : Visibility.Collapsed;
        OpenAiAccountPanel.Visibility = provider == "OpenAI" ? Visibility.Visible : Visibility.Collapsed;
        YandexPanel.Visibility = provider == "Yandex" ? Visibility.Visible : Visibility.Collapsed;
        GigaChatPanel.Visibility = provider == "GigaChat" ? Visibility.Visible : Visibility.Collapsed;
        EndpointPanel.Visibility = provider == "Compatible" ? Visibility.Visible : Visibility.Collapsed;
        ProviderHelpButton.Visibility = showRemoteFields && provider != "Compatible" ? Visibility.Visible : Visibility.Collapsed;
        KeyLabel.Text = provider switch { "Yandex" => "API-ключ или IAM-токен Яндекса", "GigaChat" => "Ключ авторизации GigaChat (Authorization Key)", "OpenAI" => "API-ключ OpenAI", "Gemini" => "API-ключ Google Gemini", "OpenRouter" => "API-ключ OpenRouter", _ => "API-ключ (необязателен для локальной модели)" };
        ProviderHint.Text = provider switch
        {
            "" => "",
            LocalModelCatalog.ProviderId => "Модель запускается на этом компьютере. После установки ей не нужны интернет и API-ключ.",
            "Gemini" => "Введите ключ из Google AI Studio и ID модели, например gemini-3.5-flash-lite. Модель можно изменить на доступную вашему проекту. Квоты проверяются в AI Studio.",
            "OpenRouter" => "Введите ключ OpenRouter и полный ID модели из каталога, например openai/gpt-4.1-mini. Используется баланс и лимиты OpenRouter, а не ключ исходного провайдера.",
            "Yandex" => "Укажите ключ сервисного аккаунта и ID каталога. Пример модели: yandexgpt/latest.",
            "OpenAI" => "Введите API-ключ из OpenAI Platform и имя доступной вам модели, например gpt-4.1-mini. Пароль от ChatGPT здесь не используется.",
            "GigaChat" => "Вставьте готовый ключ авторизации из кабинета GigaChat API, без префикса Basic. Временный токен приложение получает автоматически. Модель: GigaChat или другая доступная вашему аккаунту.",
            _ => "Укажите полный адрес совместимого API и имя установленной модели. Для локального сервера ключ может быть пустым."
        };
    }
    private void UpdateHotkey() => HotkeyBox.Text = HotkeyGesture.Format(_modifiers, _key);
    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHint.Text = "● Ожидаю нажатия… Удерживайте Ctrl / Alt / Shift и нажмите клавишу. Tab — выйти.";
        HotkeyBox.SelectAll();
    }
    private void HotkeyBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyHint.Text = "Нажмите на поле, чтобы задать сочетание. Изменения применятся после сохранения.";
    }
    private void ResetHotkey_Click(object sender, RoutedEventArgs e)
    {
        _modifiers = HotkeyGesture.DefaultModifiers;
        _key = HotkeyGesture.DefaultKey;
        EnableHotkeyCheckBox.IsChecked = true;
        UpdateHotkey();
        HotkeyHint.Text = "Выбрано Ctrl + Shift + X. Нажмите «Сохранить», чтобы применить.";
        StatusText.Text = "";
    }
    private void HotkeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return; // Keep normal keyboard focus navigation.
        e.Handled = true;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        if (e.IsRepeat) return;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (!HotkeyGesture.IsValid(Keyboard.Modifiers, key))
        {
            HotkeyHint.Text = "Нужны Ctrl, Alt, Shift или Win и буква, цифра либо F1–F11. F12 зарезервирована Windows.";
            return;
        }
        _modifiers = Keyboard.Modifiers; _key = key; UpdateHotkey(); StatusText.Text = "";
        HotkeyHint.Text = "✓ Сочетание принято. Нажмите «Сохранить» или введите другое.";
    }
    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProvider.Length == 0) { StatusText.Text = "Выберите ИИ-провайдера или локальную модель."; return; }
        if (!int.TryParse(TimeoutBox.Text, out int timeout) || timeout < 10 || timeout > 300 || !int.TryParse(TokensBox.Text, out int tokens) || tokens < 100 || tokens > 32000)
        { StatusText.Text = "Тайм-аут: 10–300 секунд. Токены: 100–32000 (в пределах лимита модели)."; return; }
        var local = SelectedLocalModel;
        if (local != null && !_localModels.IsDownloaded(local)) { RefreshLocalModels(); return; }
        string model = local?.Id ?? ModelBox.Text.Trim();
        if (model.Length == 0) { StatusText.Text = "Введите название модели."; return; }
        var next = new AppSettings { Provider = SelectedProvider, GigaChatScope = (ScopeBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "GIGACHAT_API_PERS", CustomApiKey = local == null ? ApiKeyPasswordBox.Password.Trim() : "", FolderId = FolderBox.Text.Trim(), Model = model, Endpoint = EndpointBox.Text.Trim(), UseIamToken = IamBox.IsChecked == true, AutoCopyAnswer = AutoCopyCheckBox.IsChecked == true, SaveHistory = HistoryBox.IsChecked == true, EnableGlobalHotkey = EnableHotkeyCheckBox.IsChecked == true, HotkeyModifiers = _modifiers, HotkeyKey = _key, TimeoutSeconds = timeout, MaxTokens = tokens };
        if (next.Provider == "Compatible" && (!Uri.TryCreate(next.Endpoint, UriKind.Absolute, out var uri) || !(uri.Scheme == "https" || uri.Scheme == "http" && uri.IsLoopback))) { StatusText.Text = "Используйте HTTPS или HTTP на localhost."; return; }
        var main = (MainWindow)Owner;
        if (!main.ConfigureHotkey(next)) { main.SuspendHotkey(); StatusText.Text = "Сочетание занято другой программой. Выберите другое."; return; }
        try { SettingsManager.Save(next); DialogResult = true; }
        catch { main.SuspendHotkey(); StatusText.Text = "Не удалось сохранить настройки. Проверьте доступ к профилю пользователя."; }
    }
    private void OpenAiAccount_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url }) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { StatusText.Text = "Не удалось открыть браузер. Откройте OpenAI Platform и раздел Billing или Limits."; }
    }
    private void Help_Click(object sender, RoutedEventArgs e)
    {
        string url = SelectedProvider switch {
            "OpenAI" => "https://platform.openai.com/api-keys",
            "Gemini" => "https://aistudio.google.com/apikey",
            "OpenRouter" => "https://openrouter.ai/settings/keys",
            "GigaChat" => "https://developers.sber.ru/docs/ru/gigachat/api/reference/rest/gigachat-api",
            _ => "https://aistudio.yandex.ru/docs/ai-studio/operations/generation/async-request"
        };
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { StatusText.Text = "Не удалось открыть браузер. Инструкция также указана в README.md."; }
    }
}
