using System.Net;
using System.Net.Http;
using System.Text.Json;
using SharpLensAI.Services;
internal static class ProviderTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var settings = new AppSettings { Provider = "OpenAI", Model = "gpt-4.1-mini", CustomApiKey = "test-key", FolderId = "not-for-openai" };
        using (var request = new OpenAiProvider().CreateRequest(settings, "instruction", "selected text"))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            check(request.RequestUri!.AbsoluteUri == "https://api.openai.com/v1/responses" && request.Headers.Authorization!.Scheme == "Bearer", "OpenAI endpoint and authorization");
            check(body.RootElement.GetProperty("input").GetString() == "selected text" && body.RootElement.GetProperty("instructions").GetString() == "instruction" && !body.RootElement.GetProperty("store").GetBoolean(), "OpenAI input, instruction and storage disabled");
            check(!body.RootElement.TryGetProperty("folderId", out _), "OpenAI excludes Yandex fields");
        }
        using (var client = new HttpClient(new Handler((_, _) => Task.FromResult(Json("{\"status\":\"completed\",\"output\":[{\"type\":\"reasoning\"},{\"content\":[{\"type\":\"output_text\",\"text\":\"answer\"}]}]}")))))
            check(await new LlmService(client).AskAsync(settings, "p", "t") == "answer", "OpenAI response parsed after reasoning item");
        using (var doc = JsonDocument.Parse("{\"status\":\"incomplete\"}"))
        {
            try { new OpenAiProvider().ReadAnswer(doc.RootElement); throw new Exception("Incomplete response accepted"); }
            catch (InvalidOperationException) { check(true, "Incomplete OpenAI answer reported"); }
        }
        foreach (var sample in new[] {
            ("insufficient_quota", "Недостаточно квоты"),
            ("credit_balance_exhausted", "Баланс API исчерпан"),
            ("billing_not_active", "Оплата API не активна"),
            ("project_spend_limit_exceeded", "лимит расходов проекта"),
            ("organization_spend_limit_exceeded", "лимит расходов организации"),
            ("organization_usage_limit_exceeded", "лимит использования организации"),
            ("rate_limit_exceeded", "через 42 сек."),
            ("slow_down", "через 42 сек.") })
        {
            int attempts = 0;
            using var client = new HttpClient(new Handler((_, _) =>
            {
                attempts++;
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) {
                    Content = new StringContent(JsonSerializer.Serialize(new { error = new { code = sample.Item1, message = "private-test-key-and-text" } })) };
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(42));
                return Task.FromResult(response);
            }));
            try { await new LlmService(client).AskAsync(settings, "p", "t"); throw new Exception("Expected 429"); }
            catch (InvalidOperationException ex) { check(ex.Message.Contains(sample.Item2) && !ex.Message.Contains("private-test-key-and-text") && attempts == 1, "OpenAI 429: " + sample.Item1); }
        }
        check(OpenAiErrorMessage.ForRateLimit("{\"error\":{\"type\":\"insufficient_quota\",\"code\":null}}", null).Contains("Недостаточно квоты"), "Quota type fallback");
        check(OpenAiErrorMessage.ForRateLimit("<html>private-test-key</html>", null).Contains("Причина ограничения не уточнена"), "Malformed error remains safe and unspecified");
        check(OpenAiErrorMessage.ForRateLimit("{\"error\":{\"code\":\"private-test-key\"}}", null).Contains("Причина ограничения не уточнена"), "Unknown error code not exposed");
        settings.Provider = "GigaChat"; settings.Model = "GigaChat";
        int calls = 0;
        using (var client = new HttpClient(new Handler(async (request, token) =>
        {
            calls++;
            string body = await request.Content!.ReadAsStringAsync(token);
            if (calls % 2 == 1)
            {
                check(request.RequestUri!.AbsoluteUri == "https://ngw.devices.sberbank.ru:9443/api/v2/oauth" && request.Headers.Authorization!.Scheme == "Basic", "GigaChat key sent only to OAuth");
                check(body == "scope=GIGACHAT_API_PERS" && Guid.TryParse(request.Headers.GetValues("RqUID").Single(), out _), "GigaChat scope and request ID");
                return Json("{\"access_token\":\"temporary-token\"}");
            }
            check(request.RequestUri!.AbsoluteUri == "https://api.giga.chat/v1/chat/completions" && request.Headers.Authorization!.ToString() == "Bearer temporary-token", "GigaChat completion uses temporary token");
            using var json = JsonDocument.Parse(body);
            check(json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "selected text" && !body.Contains("test-key"), "GigaChat request text excludes authorization key");
            return Json("{\"choices\":[{\"message\":{\"content\":\"giga answer\"}}]}");
        })))
        {
            var service = new LlmService(client);
            check(await service.AskAsync(settings, "p", "selected text") == "giga answer", "GigaChat full OAuth and completion flow");
            await service.AskAsync(settings, "p", "selected text");
            check(calls == 4, "Fresh GigaChat token for subsequent request");
        }
        int failures = 0;
        using (var client = new HttpClient(new Handler((_, _) => { failures++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)); })))
        {
            try { await new LlmService(client).AskAsync(settings, "p", "t"); throw new Exception("OAuth failure ignored"); }
            catch (InvalidOperationException) { check(failures == 1, "OAuth failure prevents text transmission"); }
        }
        check(JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings { Provider = "GigaChat", GigaChatScope = "GIGACHAT_API_CORP" }))!.GigaChatScope == "GIGACHAT_API_CORP", "GigaChat scope persists");
    }
    static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text) };
    sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token); }
}
