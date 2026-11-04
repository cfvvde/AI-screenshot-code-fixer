using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SharpLensAI.Models;
using SharpLensAI.Services;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SharpLensAI
{
    public partial class MainWindow : Window
    {
        private readonly LlmService _llmService = new();
        private readonly HistoryService _historyService = new();
        private readonly GlobalHotkeyManager _globalHotkeyManager = new();

        private bool _captureInProgress;
        private bool _aiRequestInProgress;

        public MainWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// SourceInitialized вызывается после создания HWND, поэтому здесь уже можно
        /// зарегистрировать глобальную комбинацию Ctrl+Shift+X.
        /// </summary>
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _globalHotkeyManager.HotkeyPressed += GlobalHotkeyManager_HotkeyPressed;
            ConfigureGlobalHotkey(showError: true);
        }

        private void GlobalHotkeyManager_HotkeyPressed(object? sender, EventArgs e)
        {
            // RegisterHotKey доставляет WM_HOTKEY потоку окна. Начинаем захват через Dispatcher.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _ = CaptureScreenAsync();
            }));
        }

        private void ConfigureGlobalHotkey(bool showError)
        {
            _globalHotkeyManager.Unregister();

            var settings = SettingsManager.Load();
            if (!settings.EnableGlobalHotkey)
                return;

            bool registered = _globalHotkeyManager.Register(
                this,
                ModifierKeys.Control | ModifierKeys.Shift,
                Key.X);

            if (!registered && showError)
            {
                Dispatcher.BeginInvoke(new Action(() => MessageBox.Show(
                    this,
                    "Не удалось зарегистрировать Ctrl+Shift+X. Возможно, сочетание уже занято другой программой. " +
                    "Попробуйте закрыть программу, которая использует эту комбинацию, и перезапустить SharpLens AI.",
                    "Глобальная горячая клавиша",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning)));
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _globalHotkeyManager.Dispose();
            base.OnClosed(e);
        }

        // Ключ берём из сохранённых настроек. В коде не хранится опубликованный ранее секрет.
        private string GetEffectiveApiKey()
        {
            var settings = SettingsManager.Load();
            if (!string.IsNullOrWhiteSpace(settings.CustomApiKey))
                return settings.CustomApiKey.Trim();

            // Совместимость с PasswordBox, если он присутствует в главном окне.
            if (ApiKeyBox != null && !string.IsNullOrWhiteSpace(ApiKeyBox.Password))
                return ApiKeyBox.Password.Trim();

            return SettingsManager.GetEffectiveApiKey();
        }

        // Показ/скрытие поля «Свой промпт».
        private void AiTaskComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomPromptTextBox == null || AiTaskComboBox == null)
                return;

            bool isCustomPrompt = AiTaskComboBox.SelectedItem is ComboBoxItem selectedItem &&
                                  string.Equals(selectedItem.Content?.ToString(), "Свой промпт", StringComparison.Ordinal);

            CustomPromptTextBox.Visibility = isCustomPrompt ? Visibility.Visible : Visibility.Collapsed;
        }

        // Обработчик кнопки «Скрин» использует ту же логику, что и глобальный хоткей.
        private async void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            await CaptureScreenAsync();
        }

        private async Task CaptureScreenAsync()
        {
            if (_captureInProgress)
                return;

            _captureInProgress = true;
            try
            {
                // Сворачиваем окно и даём Windows перерисовать рабочий стол.
                WindowState = WindowState.Minimized;
                await Task.Delay(300);

                int width = (int)SystemParameters.VirtualScreenWidth;
                int height = (int)SystemParameters.VirtualScreenHeight;
                int left = (int)SystemParameters.VirtualScreenLeft;
                int top = (int)SystemParameters.VirtualScreenTop;

                if (width <= 0 || height <= 0)
                    throw new InvalidOperationException("Не удалось определить размеры рабочего стола.");

                using var screenBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                using (var graphics = Graphics.FromImage(screenBitmap))
                {
                    graphics.CopyFromScreen(left, top, 0, 0, screenBitmap.Size);
                }

                // Оверлей выбора области. Конструктор соответствует текущему SnippingWindow.
                var snippingWindow = new SnippingWindow(screenBitmap);
                bool? result = snippingWindow.ShowDialog();

                RestoreMainWindow();

                if (result == true && snippingWindow.CapturedBitmap != null)
                {
                    using var croppedBitmap = snippingWindow.CapturedBitmap;
                    string recognizedText = await RecognizeBitmapAsync(croppedBitmap);
                    OcrResultTextBox.Text = recognizedText;
                }
            }
            catch (Exception ex)
            {
                RestoreMainWindow();
                MessageBox.Show(
                    this,
                    $"Ошибка при выделении области или распознавании текста: {ex.Message}",
                    "SharpLens AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _captureInProgress = false;
            }
        }

        private void RestoreMainWindow()
        {
            WindowState = WindowState.Normal;
            Show();
            Activate();
        }

        /// <summary>Распознавание текста из выделенного Bitmap через Windows.Media.Ocr.</summary>
        private async Task<string> RecognizeBitmapAsync(Bitmap bitmap)
        {
            string languageCode = RadioRu.IsChecked == true ? "ru" : "en";
            var language = new Windows.Globalization.Language(languageCode);

            if (!OcrEngine.IsLanguageSupported(language))
            {
                var availableLanguages = OcrEngine.AvailableRecognizerLanguages;
                if (availableLanguages.Count == 0)
                {
                    throw new InvalidOperationException(
                        "В Windows не установлены языковые пакеты для OCR. Установите русский или английский языковой пакет распознавания текста.");
                }

                language = availableLanguages[0];
            }

            var ocrEngine = OcrEngine.TryCreateFromLanguage(language);
            if (ocrEngine == null)
                throw new InvalidOperationException("Не удалось инициализировать Windows OCR для выбранного языка.");

            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            stream.Position = 0;

            var randomAccessStream = stream.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(randomAccessStream);
            using var softwareBitmap = await decoder.GetSoftwareBitmapAsync();
            var ocrResult = await ocrEngine.RecognizeAsync(softwareBitmap);
            return ocrResult.Text ?? string.Empty;
        }

        // Обработчик кнопки «Запросить ИИ».
        private async void AskAiButton_Click(object sender, RoutedEventArgs e)
        {
            if (_aiRequestInProgress)
                return;

            string recognizedText = OcrResultTextBox.Text;
            if (string.IsNullOrWhiteSpace(recognizedText))
            {
                MessageBox.Show(
                    this,
                    "Сначала распознайте текст со скриншота!",
                    "Предупреждение",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            string prompt = BuildPrompt();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                MessageBox.Show(
                    this,
                    "Введите текст своего промпта.",
                    "Пустой промпт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _aiRequestInProgress = true;
            try
            {
                string apiKey = GetEffectiveApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    throw new InvalidOperationException(
                        "API-ключ Gemini не указан. Откройте настройки (⚙) и сохраните свой ключ.");
                }

                AiResultTextBox.Text = "Загрузка ответа...";
                if (AiResponseTextBox != null)
                    AiResponseTextBox.Text = "Загрузка ответа...";

                string response = await _llmService.AskGeminiAsync(apiKey, prompt, recognizedText);

                AiResultTextBox.Text = response;
                if (AiResponseTextBox != null)
                    AiResponseTextBox.Text = response;

                // Сохраняем ответ в историю. Ошибка записи истории не должна скрывать ответ ИИ.
                try
                {
                    await _historyService.AddAsync(new HistoryEntry
                    {
                        CreatedAt = DateTime.Now,
                        Prompt = prompt,
                        RecognizedText = recognizedText,
                        Answer = response,
                        OcrLanguage = RadioRu.IsChecked == true ? "ru" : "en"
                    });
                }
                catch (Exception historyException)
                {
                    System.Diagnostics.Debug.WriteLine($"Не удалось сохранить историю SharpLens AI: {historyException}");
                }

                // Автокопирование происходит только после успешного получения полного ответа.
                var settings = SettingsManager.Load();
                if (settings.AutoCopyAnswer)
                {
                    bool copied = await ClipboardHelper.SetTextWithRetryAsync(response, attempts: 8);
                    if (!copied)
                    {
                        MessageBox.Show(
                            this,
                            "Ответ получен, но Windows не дала записать его в буфер обмена. " +
                            "Попробуйте скопировать текст вручную.",
                            "Не удалось скопировать ответ",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                AiResultTextBox.Text = "Не удалось получить ответ.";
                if (AiResponseTextBox != null)
                    AiResponseTextBox.Text = "Не удалось получить ответ.";

                MessageBox.Show(
                    this,
                    $"Ошибка: {ex.Message}",
                    "Ошибка SharpLens AI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _aiRequestInProgress = false;
            }
        }

        private string BuildPrompt()
        {
            if (AiTaskComboBox.SelectedItem is not ComboBoxItem selectedItem)
                return "Переведи и объясни текст:";

            string task = selectedItem.Content?.ToString() ?? string.Empty;
            return task switch
            {
                "Сделать краткое резюме" => "Сделай краткое резюме следующего текста:",
                "Найти ошибки" => "Найди ошибки в следующем тексте/коде:",
                "Свой промпт" => CustomPromptTextBox.Text?.Trim() ?? string.Empty,
                _ => "Переведи и объясни текст:"
            };
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            bool? dialogResult = settingsWindow.ShowDialog();
            if (dialogResult == true)
                ConfigureGlobalHotkey(showError: true);
        }

        // Можно назначить этому обработчику кнопку истории, если она есть в MainWindow.xaml.
        private void HistoryButton_Click(object sender, RoutedEventArgs e)
        {
            var historyWindow = new HistoryWindow { Owner = this };
            historyWindow.ShowDialog();
        }
    }
}
