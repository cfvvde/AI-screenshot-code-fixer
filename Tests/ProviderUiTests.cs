using System.Windows;
using System.Windows.Controls;
using System.IO;
using SharpLensAI;
using SharpLensAI.Services;
internal static class ProviderUiTests
{
    public static void Run(Action<bool, string> check)
    {
        // Instantiate WPF controls without showing windows or touching user settings.
        var app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.InitializeComponent();
        var window = new SettingsWindow(new AppSettings());
        try
        {
            var provider = (ComboBox)window.FindName("ProviderBox");
            var yandex = (StackPanel)window.FindName("YandexPanel");
            var giga = (StackPanel)window.FindName("GigaChatPanel");
            var account = (StackPanel)window.FindName("OpenAiAccountPanel");
            var endpoint = (StackPanel)window.FindName("EndpointPanel");
            check(yandex.Visibility == Visibility.Visible && giga.Visibility == Visibility.Collapsed && endpoint.Visibility == Visibility.Collapsed, "Yandex settings shown exclusively");
            provider.SelectedIndex = 1;
            check(yandex.Visibility == Visibility.Collapsed && giga.Visibility == Visibility.Collapsed && endpoint.Visibility == Visibility.Collapsed && account.Visibility == Visibility.Visible, "OpenAI account links shown, unrelated fields hidden");
            provider.SelectedIndex = 2;
            check(yandex.Visibility == Visibility.Collapsed && giga.Visibility == Visibility.Visible && endpoint.Visibility == Visibility.Collapsed && account.Visibility == Visibility.Collapsed, "GigaChat shows only its scope");
            provider.SelectedIndex = 3;
            check(yandex.Visibility == Visibility.Collapsed && giga.Visibility == Visibility.Collapsed && endpoint.Visibility == Visibility.Visible, "Compatible API shows endpoint only");
        }
        finally { window.Close(); }
        foreach (string name in new[] { "Gemini", "OpenRouter" })
        {
            var restored = new SettingsWindow(new AppSettings { Provider = name, Model = "saved-model", CustomApiKey = "test-secret" });
            try
            {
                check(((ComboBoxItem)((ComboBox)restored.FindName("ProviderBox")).SelectedItem).Tag.ToString() == name && ((TextBox)restored.FindName("ModelBox")).Text == "saved-model", name + " saved selection restored");
                check(new[] { "YandexPanel", "GigaChatPanel", "EndpointPanel", "OpenAiAccountPanel" }.All(id => ((FrameworkElement)restored.FindName(id)).Visibility == Visibility.Collapsed), name + " hides unrelated settings");
            }
            finally { restored.Close(); }
        }
        CheckLocalModels(check);
    }

    private static void CheckLocalModels(Action<bool, string> check)
    {
        string root = Path.Combine(AppContext.BaseDirectory, "TestResults", Guid.NewGuid().ToString("N"));
        var store = new LocalModelStore(root);
        var model = LocalModelCatalog.Get("Qwen3-0.6B");
        string path = store.ModelPath(model);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var settings = new AppSettings { Provider = LocalModelCatalog.ProviderId, Model = model.Id };
        try
        {
            // Exercise the settings list using the expected file length without downloading model weights.
            using (var file = File.Create(path)) file.SetLength(model.Size);
            File.WriteAllText(Path.Combine(root, "OtherModel.gguf"), "outside the catalog");
            var installed = new SettingsWindow(settings, store);
            try
            {
                var provider = (ComboBox)installed.FindName("ProviderBox");
                check(((ComboBoxItem)provider.SelectedItem).Tag is LocalModel local && local.Id == model.Id &&
                    provider.Items.OfType<ComboBoxItem>().Count(item => item.Tag is LocalModel) == 1,
                    "Settings restore only installed catalog models");
                check(((FrameworkElement)installed.FindName("KeyPanel")).Visibility == Visibility.Collapsed &&
                    ((FrameworkElement)installed.FindName("EndpointPanel")).Visibility == Visibility.Collapsed,
                    "Local model hides cloud credentials and endpoint");
            }
            finally { installed.Close(); }

            File.Delete(path);
            var missing = new SettingsWindow(settings, store);
            try
            {
                var provider = (ComboBox)missing.FindName("ProviderBox");
                check(provider.SelectedItem == null && provider.Items.Contains(missing.FindName("InstallLocalItem")),
                    "Deleted model is no longer selectable and installer stays available");
            }
            finally { missing.Close(); }
        }
        finally
        {
            File.Delete(path);
            File.Delete(Path.Combine(root, "OtherModel.gguf"));
            Directory.Delete(Path.GetDirectoryName(path)!);
            Directory.Delete(root);
        }
    }
}
