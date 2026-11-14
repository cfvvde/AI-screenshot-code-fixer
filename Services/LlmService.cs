using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace SharpLensAI.Services;

public interface IAiProvider
{
    Task PrepareAsync(HttpClient client, AppSettings settings, CancellationToken token) => Task.CompletedTask;
    HttpRequestMessage CreateRequest(AppSettings settings, string instruction, string text);
    string ReadAnswer(JsonElement root);
}

public sealed class YandexProvider : IAiProvider
{
    public HttpRequestMessage CreateRequest(AppSettings s, string instruction, string text)
    {
        if (string.IsNullOrWhiteSpace(s.CustomApiKey) || string.IsNullOrWhiteSpace(s.FolderId))
            throw new InvalidOperationException("Укажите API-ключ (или IAM-токен) и ID каталога Яндекса в настройках.");
        var request = new HttpRequestMessage(HttpMethod.Post, "https://ai.api.cloud.yandex.net/foundationModels/v1/completion");
        request.Headers.Authorization = new AuthenticationHeaderValue(s.UseIamToken ? "Bearer" : "Api-Key", s.CustomApiKey);
        request.Headers.Add("x-folder-id", s.FolderId);
        request.Headers.Add("x-data-logging-enabled", "false");
        request.Content = LlmService.JsonContent(new {
            modelUri = s.Model.StartsWith("gpt://", StringComparison.Ordinal) ? s.Model : $"gpt://{s.FolderId}/{s.Model}",
            completionOptions = new { stream = false, temperature = 0.3, maxTokens = s.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            messages = new[] { new { role = "system", text = instruction }, new { role = "user", text } }
        });
        return request;
    }
    public string ReadAnswer(JsonElement root) => root.GetProperty("result").GetProperty("alternatives")[0].GetProperty("message").GetProperty("text").GetString() ?? "";
}

public sealed class CompatibleProvider : IAiProvider
{
    public HttpRequestMessage CreateRequest(AppSettings s, string instruction, string text)
    {
        if (!Uri.TryCreate(s.Endpoint, UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "https" || (uri.Scheme == "http" && uri.IsLoopback)))
            throw new InvalidOperationException("Адрес API должен использовать HTTPS; HTTP допустим только для localhost.");
        var request = new HttpRequestMessage(HttpMethod.Post, uri);
        if (!string.IsNullOrWhiteSpace(s.CustomApiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.CustomApiKey);
        request.Content = LlmService.JsonContent(new { model = s.Model, stream = false, temperature = 0.3, max_tokens = s.MaxTokens,
            messages = new[] { new { role = "system", content = instruction }, new { role = "user", content = text } } });
        return request;
    }
    public string ReadAnswer(JsonElement root) => root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
}

public sealed class LlmService
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
    private readonly HttpClient _client;
    public LlmService(HttpClient? client = null) => _client = client ?? SharedClient;
    internal static StringContent JsonContent(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    public async Task<string> AskAsync(AppSettings settings, string prompt, string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(prompt)) throw new InvalidOperationException("Введите текст и инструкцию.");
        if (string.IsNullOrWhiteSpace(settings.Model)) throw new InvalidOperationException("Укажите модель в настройках.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 300)));
        try
        {
            if (settings.Provider == LocalModelCatalog.ProviderId)
                return await new LocalModelRunner().AskAsync(settings, prompt, text, timeout.Token);
            IAiProvider provider = settings.Provider switch { "Yandex" => new YandexProvider(), "Compatible" => new CompatibleProvider(), "OpenAI" => new OpenAiProvider(), "GigaChat" => new GigaChatProvider(), "Gemini" => new GeminiProvider(), "OpenRouter" => new OpenRouterProvider(), _ => throw new InvalidOperationException("Неизвестный провайдер.") };
            return await SendAsync(settings, provider, prompt, text, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { throw new InvalidOperationException("Истекло время ожидания. Повторите запрос или увеличьте тайм-аут."); }
        catch (HttpRequestException ex) when (ex.InnerException is System.Security.Authentication.AuthenticationException) { throw new InvalidOperationException("Не удалось проверить сертификат сервиса. Проверьте доверенные сертификаты Windows по официальной инструкции провайдера. Проверка TLS не отключается."); }
        catch (HttpRequestException) { throw new InvalidOperationException("Нет соединения с ИИ-сервисом. Проверьте интернет или запуск локальной модели."); }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException) { throw new InvalidOperationException("ИИ-сервис вернул неожиданный формат ответа. Проверьте модель и API."); }
    }
    internal async Task<string> SendAsync(AppSettings settings, IAiProvider provider, string prompt, string text, CancellationToken token)
    {
        await provider.PrepareAsync(_client, settings, token);
        using var request = provider.CreateRequest(settings, prompt, text);
        using var response = await _client.SendAsync(request, token);
        if (settings.Provider == "OpenAI" && response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            var retry = response.Headers.RetryAfter;
            TimeSpan? delay = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            throw new InvalidOperationException(OpenAiErrorMessage.ForRateLimit(await response.Content.ReadAsStringAsync(token), delay));
        }
        EnsureSuccess(response, settings.Provider);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var answer = provider.ReadAnswer(document.RootElement);
        if (string.IsNullOrWhiteSpace(answer)) throw new InvalidOperationException("ИИ вернул пустой ответ. Измените запрос и повторите.");
        return answer.Trim();
    }

    internal static void EnsureSuccess(HttpResponseMessage response, string provider)
    {
        if (response.IsSuccessStatusCode) return;
        if (provider == "OpenRouter" && (int)response.StatusCode == 402)
            throw new InvalidOperationException("OpenRouter: запрос ограничен бюджетом или балансом. Проверьте Credits, лимит ключа и «Макс. токенов»; при наличии активных запросов дождитесь их завершения.");
        if (provider == "Gemini" && (int)response.StatusCode == 429)
            throw new InvalidOperationException("Gemini: достигнут лимит запросов или квота проекта. Проверьте Usage/Rate limits и Billing в Google AI Studio. При временном ограничении повторите позже.");
        throw new InvalidOperationException(provider + ": " + ((int)response.StatusCode switch {
            401 or 403 => "Доступ запрещён. Проверьте ключ, срок действия и права аккаунта.",
            429 => "Превышен лимит запросов или квота. Попробуйте позже.",
            400 or 404 => "Проверьте модель и параметры выбранного провайдера.",
            >= 500 => "Сервис временно недоступен. Повторите позже.",
            _ => $"Сервис вернул HTTP {(int)response.StatusCode}. Проверьте настройки." }));
    }
}
