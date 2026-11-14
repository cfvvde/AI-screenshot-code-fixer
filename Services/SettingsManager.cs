using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace SharpLensAI.Services;

public sealed class AppSettings
{
    [JsonIgnore] public string CustomApiKey { get; set; } = "";
    public string ProtectedApiKey { get; set; } = "";
    public string Provider { get; set; } = "Yandex";
    public string FolderId { get; set; } = "";
    public string Model { get; set; } = "yandexgpt/latest";
    public string Endpoint { get; set; } = "http://localhost:11434/v1/chat/completions";
    public string GigaChatScope { get; set; } = "GIGACHAT_API_PERS";
    public bool UseIamToken { get; set; }
    public bool AutoCopyAnswer { get; set; }
    public bool SaveHistory { get; set; } = true;
    public bool EnableGlobalHotkey { get; set; } = true;
    public ModifierKeys HotkeyModifiers { get; set; } = HotkeyGesture.DefaultModifiers;
    public Key HotkeyKey { get; set; } = HotkeyGesture.DefaultKey;
    public int TimeoutSeconds { get; set; } = 90;
    public int MaxTokens { get; set; } = 2000;
}

public static class SettingsManager
{
    public static string? LoadWarning { get; private set; }
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharpLensAI", "appsettings.json");
    public static AppSettings Load()
    {
        LoadWarning = null;
        try
        {
            if (!File.Exists(SettingsPath)) return new();
            var json = File.ReadAllText(SettingsPath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            if (!string.IsNullOrEmpty(settings.ProtectedApiKey))
                settings.CustomApiKey = SecretProtection.Unprotect(settings.ProtectedApiKey);
            else
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("CustomApiKey", out var oldKey))
                {
                    settings.CustomApiKey = oldKey.GetString() ?? "";
                    settings.Provider = "Compatible";
                    settings.AutoCopyAnswer = false;
                    Save(settings);
                    LoadWarning = "Старый ключ зашифрован. Выберите провайдера и модель в настройках.";
                }
            }
            return settings;
        }
        catch { LoadWarning = "Не удалось прочитать настройки или расшифровать ключ. Откройте настройки и введите данные заново."; return new(); }
    }
    public static void Save(AppSettings settings)
    {
        settings.ProtectedApiKey = string.IsNullOrWhiteSpace(settings.CustomApiKey) ? "" : SecretProtection.Protect(settings.CustomApiKey);
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(SettingsPath + ".tmp", SettingsPath, true);
    }
}

public static class SecretProtection
{
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CryptProtectData(ref Blob input, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static string Protect(string value) => Convert.ToBase64String(Transform(Encoding.UTF8.GetBytes(value), true));
    public static string Unprotect(string value) => Encoding.UTF8.GetString(Transform(Convert.FromBase64String(value), false));
    private static byte[] Transform(byte[] bytes, bool protect)
    {
        var input = new Blob { Size = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            bool ok = protect ? CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!ok) throw new Win32Exception(Marshal.GetLastWin32Error());
            var result = new byte[output.Size];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally { Marshal.FreeHGlobal(input.Data); if (output.Data != IntPtr.Zero) LocalFree(output.Data); Array.Clear(bytes); }
    }
}
