using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
namespace SharpLensAI.Services;

public sealed class OpenAiProvider : IAiProvider
{
    public HttpRequestMessage CreateRequest(AppSettings settings, string instruction, string text)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomApiKey)) throw new InvalidOperationException("Введите API-ключ OpenAI в настройках.");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.CustomApiKey);
        request.Content = LlmService.JsonContent(new { model = settings.Model, instructions = instruction, input = text, max_output_tokens = settings.MaxTokens, store = false });
        return request;
    }
    public string ReadAnswer(JsonElement root)
    {
        if (root.TryGetProperty("status", out var status) && status.GetString() is "incomplete" or "failed")
            throw new InvalidOperationException("OpenAI не завершил ответ. Увеличьте лимит токенов или сократите текст и повторите запрос.");
        var parts = new List<string>();
        foreach (var item in root.GetProperty("output").EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content)) continue;
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("type", out var type) && type.GetString() == "output_text") parts.Add(part.GetProperty("text").GetString() ?? "");
        }
        return string.Join("\n", parts);
    }
}

public sealed class GigaChatProvider : IAiProvider
{
    private string? _accessToken;
    public async Task PrepareAsync(HttpClient client, AppSettings settings, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomApiKey)) throw new InvalidOperationException("Введите ключ авторизации GigaChat в настройках.");
        if (settings.GigaChatScope is not ("GIGACHAT_API_PERS" or "GIGACHAT_API_B2B" or "GIGACHAT_API_CORP"))
            throw new InvalidOperationException("Выберите тип доступа GigaChat в настройках.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://ngw.devices.sberbank.ru:9443/api/v2/oauth");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", settings.CustomApiKey);
        request.Headers.Add("RqUID", Guid.NewGuid().ToString());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["scope"] = settings.GigaChatScope });
        using var response = await client.SendAsync(request, token);
        LlmService.EnsureSuccess(response, "GigaChat: получение токена");
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        _accessToken = doc.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(_accessToken)) throw new InvalidOperationException("GigaChat вернул пустой токен доступа. Проверьте ключ авторизации.");
    }
    public HttpRequestMessage CreateRequest(AppSettings settings, string instruction, string text)
    {
        if (string.IsNullOrWhiteSpace(_accessToken)) throw new InvalidOperationException("Не получен токен GigaChat.");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.giga.chat/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Content = LlmService.JsonContent(new { model = settings.Model, stream = false, max_tokens = settings.MaxTokens,
            messages = new[] { new { role = "system", content = instruction }, new { role = "user", content = text } } });
        return request;
    }
    public string ReadAnswer(JsonElement root) => new CompatibleProvider().ReadAnswer(root);
}
