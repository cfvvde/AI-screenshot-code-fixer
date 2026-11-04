using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SharpLensAI.Services;

public sealed class AppSettings
{
    public string CustomApiKey { get; set; } = string.Empty;
    public bool AutoCopyAnswer { get; set; } = true;
    public bool EnableGlobalHotkey { get; set; } = true;
}

public static class SettingsManager
{
    private const string SettingsFileName = "appsettings.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Не встраивайте настоящий секрет в публичный репозиторий/дистрибутив.
    // При необходимости замените placeholder для локальной сборки; пользовательский ключ имеет приоритет.
    public const string DEFAULT_KEY = "СЮДА_ВСТАВЬТЕ_ВАШ_КЛЮЧ";

    public static string SettingsPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SharpLensAI");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, SettingsFileName);
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return ReadSettings(SettingsPath);

            // Одноразовая миграция старого appsettings.json, лежавшего рядом с приложением.
            var legacyPaths = new[]
            {
                Path.Combine(AppContext.BaseDirectory, SettingsFileName),
                Path.Combine(Environment.CurrentDirectory, SettingsFileName)
            }.Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var legacyPath in legacyPaths)
            {
                if (!File.Exists(legacyPath) ||
                    string.Equals(Path.GetFullPath(legacyPath), Path.GetFullPath(SettingsPath), StringComparison.OrdinalIgnoreCase))
                    continue;

                var settings = ReadSettings(legacyPath);
                try { Save(settings); } catch { /* чтение старого файла всё ещё полезно */ }
                return settings;
            }
        }
        catch
        {
            // Повреждённый/недоступный файл настроек не должен останавливать запуск приложения.
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var path = SettingsPath;
        var temporaryPath = path + ".tmp";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(temporaryPath, json);

        if (File.Exists(path))
            File.Move(temporaryPath, path, overwrite: true);
        else
            File.Move(temporaryPath, path);
    }

    public static string GetEffectiveApiKey()
    {
        var customKey = Load().CustomApiKey?.Trim();
        if (!string.IsNullOrWhiteSpace(customKey))
            return customKey;

        return IsUsableKey(DEFAULT_KEY) ? DEFAULT_KEY.Trim() : string.Empty;
    }

    // Сохранено для совместимости с прежним кодом проекта.
    public static string GetApiKey() => GetEffectiveApiKey();

    private static AppSettings ReadSettings(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppSettings>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();
    }

    private static bool IsUsableKey(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        !value.StartsWith("СЮДА_", StringComparison.OrdinalIgnoreCase) &&
        !value.Contains("ВАШ_КЛЮЧ", StringComparison.OrdinalIgnoreCase);
}
