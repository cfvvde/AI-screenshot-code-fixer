using System;
using System.Windows;
using SharpLensAI.Services;

namespace SharpLensAI;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        var settings = SettingsManager.Load();
        ApiKeyPasswordBox.Password = settings.CustomApiKey ?? string.Empty;
        AutoCopyCheckBox.IsChecked = settings.AutoCopyAnswer;
        EnableHotkeyCheckBox.IsChecked = settings.EnableGlobalHotkey;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = SettingsManager.Load();
            settings.CustomApiKey = ApiKeyPasswordBox.Password.Trim();
            settings.AutoCopyAnswer = AutoCopyCheckBox.IsChecked == true;
            settings.EnableGlobalHotkey = EnableHotkeyCheckBox.IsChecked == true;
            SettingsManager.Save(settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Не удалось сохранить настройки: {ex.Message}";
        }
    }
}
