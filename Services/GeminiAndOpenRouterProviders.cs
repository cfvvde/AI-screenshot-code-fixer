using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace SharpLensAI.Services;

public sealed class GeminiProvider : IAiProvider
{
    public HttpRequestMessage CreateRequest(AppSettings settings, string instruction, string text)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomApiKey)) throw new InvalidOperationException("Введите API-ключ Gemini из Google AI Studio.");
        string model = settings.Model.Trim();
        if (model.StartsWith("models/", StringComparison.Ordinal)) model = model[7..];
        if (!Regex.IsMatch(model, @"\A[a-zA-Z0-9._-]+\z")) throw new InvalidOperationException("Введите ID модели Gemini, например gemini-3.5-flash-lite, без URL.");
        var request = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", settings.CustomApiKey);
        request.Content = LlmService.JsonContent(new {
            systemInstruction = new { parts = new[] { new { text = instruction } } },
            contents = new[] { new { role = "user", parts = new[] { new { text } } } },
            generationConfig = new { maxOutputTokens = settings.MaxTokens }
        });
        return request;
    }
    public string ReadAnswer(JsonElement root)
    {
        if (root.TryGetProperty("promptFeedback", out var feedback) && feedback.TryGetProperty("blockReason", out var blocked) && blocked.GetString() is { } reason && reason != "BLOCK_REASON_UNSPECIFIED")
            throw new InvalidOperationException("Gemini заблокировал запрос. Измените текст или инструкцию.");
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0) return "";
        var candidate = candidates[0];
        if (candidate.TryGetProperty("finishReason", out var finish))
        {
            if (finish.GetString() == "MAX_TOKENS") throw new InvalidOperationException("Gemini достиг лимита ответа. Увеличьте «Макс. токенов» или сократите запрос.");
            if (finish.GetString() is { } status && status != "STOP" && status != "FINISH_REASON_UNSPECIFIED")
                throw new InvalidOperationException("Gemini не завершил ответ или ограничил его. Измените запрос и повторите.");
        }
        if (!candidate.TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return "";
        return string.Concat(parts.EnumerateArray()
            .Where(p => !p.TryGetProperty("thought", out var thought) || thought.ValueKind != JsonValueKind.True)
            .Where(p => p.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            .Select(p => p.GetProperty("text").GetString()));
    }
}

public sealed class OpenRouterProvider : IAiProvider
{
    public HttpRequestMessage CreateRequest(AppSettings settings, string instruction, string text)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomApiKey)) throw new InvalidOperationException("Введите API-ключ OpenRouter.");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CustomApiKey);
        request.Content = LlmService.JsonContent(new { model = settings.Model, stream = false, max_tokens = settings.MaxTokens,
            messages = new[] { new { role = "system", content = instruction }, new { role = "user", content = text } } });
        return request;
    }
    public string ReadAnswer(JsonElement root)
    {
        if (root.TryGetProperty("error", out _)) throw new InvalidOperationException("OpenRouter сообщил об ошибке модели. Проверьте её доступность и повторите позже.");
        var choice = root.GetProperty("choices")[0];
        if (choice.TryGetProperty("finish_reason", out var finish))
        {
            if (finish.GetString() == "length") throw new InvalidOperationException("OpenRouter: ответ обрезан по лимиту. Увеличьте «Макс. токенов» или сократите запрос.");
            if (finish.GetString() is "error" or "content_filter") throw new InvalidOperationException("OpenRouter не завершил ответ или заблокировал запрос. Измените запрос и повторите.");
        }
        return new CompatibleProvider().ReadAnswer(root);
    }
}
