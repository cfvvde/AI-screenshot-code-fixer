using System.Net;
using System.Net.Http;
using System.Text.Json;
using SharpLensAI.Services;
internal static class GeminiRouterTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var s = new AppSettings { Provider = "Gemini", Model = "models/gemini-3.5-flash-lite", CustomApiKey = "test-secret", MaxTokens = 1234 };
        using (var request = new GeminiProvider().CreateRequest(s, "instruction", "selected text"))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            check(request.RequestUri!.AbsoluteUri == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent" && !request.RequestUri.Query.Contains("test-secret") && request.Headers.GetValues("x-goog-api-key").Single() == "test-secret", "Gemini endpoint and header-only secret");
            check(body.RootElement.GetProperty("systemInstruction").GetProperty("parts")[0].GetProperty("text").GetString() == "instruction" && body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString() == "selected text", "Gemini instruction and text separated");
            check(body.RootElement.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32() == 1234, "Gemini output limit");
        }
        using (var client = Client(HttpStatusCode.OK, "{\"candidates\":[{\"finishReason\":\"STOP\",\"content\":{\"parts\":[{\"thought\":true,\"text\":\"internal\"},{\"text\":\"hello \"},{\"text\":\"world\"}]}}]}"))
            check(await new LlmService(client).AskAsync(s, "p", "t") == "hello world", "Gemini text assembled without thought parts");
        foreach (string body in new[] { "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}", "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}", "{\"candidates\":[]}", "{\"candidates\":[{\"finishReason\":\"SAFETY\"}]}" })
            await Reject(s, HttpStatusCode.OK, body, check, "Gemini blocked, truncated or empty response");
        await Reject(s, HttpStatusCode.TooManyRequests, "private error", check, "Gemini quota");
        s.Model = "../invalid?key=leak";
        await Reject(s, HttpStatusCode.OK, "{}", check, "Gemini invalid model rejected");
        s.Provider = "OpenRouter"; s.Model = "openai/gpt-4.1-mini";
        using (var request = new OpenRouterProvider().CreateRequest(s, "instruction", "selected text"))
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            check(request.RequestUri!.AbsoluteUri == "https://openrouter.ai/api/v1/chat/completions" && request.Headers.Authorization!.ToString() == "Bearer test-secret", "OpenRouter endpoint and key");
            check(body.RootElement.GetProperty("model").GetString() == s.Model && body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == "selected text", "OpenRouter full model ID and text");
        }
        using (var client = Client(HttpStatusCode.OK, "{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"content\":\"router answer\"}}]}"))
            check(await new LlmService(client).AskAsync(s, "p", "t") == "router answer", "OpenRouter response parsed");
        await Reject(s, HttpStatusCode.PaymentRequired, "private error", check, "OpenRouter budget error");
        await Reject(s, HttpStatusCode.OK, "{\"error\":{\"message\":\"private error\"}}", check, "OpenRouter HTTP 200 error");
        await Reject(s, HttpStatusCode.OK, "{\"choices\":[{\"finish_reason\":\"length\"}]}", check, "OpenRouter truncated answer");
        foreach (string provider in new[] { "Gemini", "OpenRouter" })
        { s.Provider = provider; s.CustomApiKey = ""; await Reject(s, HttpStatusCode.OK, "{}", check, provider + " missing key"); }
    }
    static async Task Reject(AppSettings s, HttpStatusCode status, string body, Action<bool, string> check, string name)
    {
        using var client = Client(status, body);
        try { await new LlmService(client).AskAsync(s, "p", "t"); throw new Exception("Expected error: " + name); }
        catch (InvalidOperationException ex) { check(!ex.Message.Contains("test-secret") && !ex.Message.Contains("private error"), name); }
    }
    static HttpClient Client(HttpStatusCode status, string body) => new(new Handler(status, body));
    sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }); }
}
