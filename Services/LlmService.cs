using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SharpLensAI.Services;

public sealed class LlmService
{
    private const string ModelName = "gemini-3.8-flash";

    private static readonly HttpClient HttpClient = new(new HttpClientHandler
    {
        UseProxy = true,
        DefaultProxyCredentials = CredentialCache.DefaultCredentials
    })
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public async Task<string> AskGeminiAsync(
        string apiKey,
        string prompt,
        string recognizedText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException("API-ключ не указан. Откройте настройки и добавьте ключ Gemini.", nameof(apiKey));
        if (string.IsNullOrWhiteSpace(recognizedText))
            throw new ArgumentException("Не найден текст для обработки.", nameof(recognizedText));

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{ModelName}:generateContent?key={Uri.EscapeDataString(apiKey)}";
        var combinedPrompt = $"{prompt?.Trim()}\n\nТекст для обработки:\n\"\"\"\n{recognizedText}\n\"\"\"";
        var requestBody = new
        {
            contents = new[]
            {
                new { parts = new[] { new { text = combinedPrompt } } }
            }
        };

        var json = JsonSerializer.Serialize(requestBody);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await HttpClient.PostAsync(url, content, cancellationToken).ConfigureAwait(false);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Не включаем URL целиком в сообщение: в нём передаётся API-ключ.
            var safeBody = responseText.Length > 1800 ? responseText[..1800] + "…" : responseText;
            throw new HttpRequestException($"Ошибка Gemini API [{(int)response.StatusCode} {response.StatusCode}]: {safeBody}");
        }

        using var document = JsonDocument.Parse(responseText);
        var root = document.RootElement;
        if (root.TryGetProperty("candidates", out var candidates) &&
            candidates.ValueKind == JsonValueKind.Array && candidates.GetArrayLength() > 0)
        {
            var candidate = candidates[0];
            if (candidate.TryGetProperty("content", out var responseContent) &&
                responseContent.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                var answer = string.Join("\n", parts.EnumerateArray()
                    .Where(part => part.TryGetProperty("text", out _))
                    .Select(part => part.GetProperty("text").GetString())
                    .Where(text => !string.IsNullOrWhiteSpace(text)));

                if (!string.IsNullOrWhiteSpace(answer))
                    return answer.Trim();
            }
        }

        var blockReason = root.TryGetProperty("promptFeedback", out var feedback) &&
                          feedback.TryGetProperty("blockReason", out var reason)
            ? reason.GetString()
            : null;
        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(blockReason)
                ? "Gemini вернул пустой ответ. Попробуйте изменить запрос."
                : $"Запрос не обработан фильтрами безопасности Gemini: {blockReason}.");
    }
}
