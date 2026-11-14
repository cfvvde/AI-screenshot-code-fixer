using System.Net;
using System.Net.Http;
using System.Text.Json;
using SharpLensAI.Services;
using SharpLensAI.Models;
using System.Windows.Documents;
using System.IO;

internal static class Program
{
    static int passed;
    [STAThread] static int Main()
    {
        try { Run().GetAwaiter().GetResult(); Console.WriteLine($"PASS: {passed} checks"); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); passed++; Console.WriteLine("PASS: " + name); }
    static async Task Run()
    {
        ProviderUiTests.Run(Check);
        HotkeyTests.Run(Check);
        Check(!new AppSettings().AutoCopyAnswer, "Auto-copy disabled by default");
        string secret = "test-secret-ключ";
        string encrypted = SecretProtection.Protect(secret);
        Check(encrypted != secret && SecretProtection.Unprotect(encrypted) == secret, "Windows DPAPI round trip");
        Check(!JsonSerializer.Serialize(new AppSettings { CustomApiKey = secret }).Contains(secret), "Plaintext key excluded from JSON");
        Check(PromptCatalog.Names.Length == 5 && Enumerable.Range(0, 4).Select(i => PromptCatalog.Build(i, "")).Distinct().Count() == 4, "Five distinct modes");
        Check(PromptCatalog.Build(4, "  custom question  ") == "custom question", "Custom prompt preserved");
        var document = MarkdownRenderer.Render("# Header\n**bold** and `inline`\n```cs\n    var x = 1;\n```\n- list");
        Check(new TextRange(document.ContentStart, document.ContentEnd).Text.Contains("    var x = 1;"), "Markdown code indentation preserved");
        await ProviderTests.Run(Check);
        await GeminiRouterTests.Run(Check);
        var settings = new AppSettings { CustomApiKey = "fake-key", FolderId = "test-folder" };
        using (var request = new YandexProvider().CreateRequest(settings, "instruction", "private text"))
        {
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Check(request.Headers.Authorization!.Scheme == "Api-Key", "Yandex API-key authorization");
            Check(json.RootElement.GetProperty("modelUri").GetString() == "gpt://test-folder/yandexgpt/latest", "Yandex model URI");
            Check(json.RootElement.GetProperty("messages")[1].GetProperty("text").GetString() == "private text", "Only supplied text sent");
            Check(!json.RootElement.TryGetProperty("image", out _), "No screenshot in request");
        }
        settings.UseIamToken = true;
        using (var request = new YandexProvider().CreateRequest(settings, "a", "b")) Check(request.Headers.Authorization!.Scheme == "Bearer", "IAM authorization");
        using (var client = Client(HttpStatusCode.OK, "{\"result\":{\"alternatives\":[{\"message\":{\"text\":\"answer\"}}]}}"))
            Check(await new LlmService(client).AskAsync(settings, "p", "t") == "answer", "Yandex response parsed");
        settings.Provider = "Compatible"; settings.CustomApiKey = ""; settings.Model = "local-model";
        using (var request = new CompatibleProvider().CreateRequest(settings, "p", "t")) Check(request.Headers.Authorization == null && request.RequestUri!.IsLoopback, "Local model without key");
        using (var client = Client(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"local answer\"}}]}"))
            Check(await new LlmService(client).AskAsync(settings, "p", "t") == "local answer", "Compatible response parsed");
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError, HttpStatusCode.NotFound })
        {
            using var client = Client(status, "secret provider details");
            await ExpectError(() => new LlmService(client).AskAsync(settings, "p", "t"), "HTTP " + (int)status);
        }
        foreach (string body in new[] { "broken json", "{}", "{\"choices\":[]}", "{\"choices\":[{\"message\":{\"content\":\"\"}}]}" })
        {
            using var client = Client(HttpStatusCode.OK, body);
            await ExpectError(() => new LlmService(client).AskAsync(settings, "p", "t"), "Malformed/empty response");
        }
        using (var client = new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("network failure"))))
            await ExpectError(() => new LlmService(client).AskAsync(settings, "p", "t"), "Network error");
        using (var client = new HttpClient(new FakeHandler((_, _) => throw new TaskCanceledException())))
            await ExpectError(() => new LlmService(client).AskAsync(settings, "p", "t"), "Timeout");
        using (var cts = new CancellationTokenSource())
        using (var client = new HttpClient(new FakeHandler(async (_, token) => { await Task.Delay(10000, token); return new(HttpStatusCode.OK); })))
        {
            cts.Cancel();
            try { await new LlmService(client).AskAsync(settings, "p", "t", cts.Token); throw new Exception("No cancellation"); }
            catch (OperationCanceledException) { Check(true, "User cancellation preserved"); }
        }
        settings.Endpoint = "http://example.com/v1/chat/completions";
        await ExpectError(() => new LlmService().AskAsync(settings, "p", "t"), "Remote plaintext HTTP blocked");
        await ExpectError(() => new LlmService().AskAsync(settings, "p", ""), "Empty input blocked");
        using var bitmap = new System.Drawing.Bitmap(1000, 200);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        using (var font = new System.Drawing.Font("Arial", 28))
        { g.Clear(System.Drawing.Color.White); g.DrawString("Hello world 123\nПривет мир 456", font, System.Drawing.Brushes.Black, 20, 20); }
        var ocr = await new OcrService().RecognizeTextAsync(bitmap, "auto");
        Console.WriteLine(ocr.Text); Console.WriteLine("OCR language: " + ocr.UsedLanguage + "; " + ocr.Warning);
        Check(ocr.Text.Contains("123") && ocr.Text.Contains("456") && ocr.Text.Contains('\n'), "Windows OCR two lines");
        if (ocr.UsedLanguage.StartsWith("ru")) Check(ocr.Text.Contains("Привет", StringComparison.OrdinalIgnoreCase) && ocr.Text.Contains("Hello", StringComparison.OrdinalIgnoreCase), "Mixed Russian and English OCR");
        Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "TestResults"));
        string temp = Path.Combine(AppContext.BaseDirectory, "TestResults", "SharpLensAI-tests-" + Guid.NewGuid() + ".json");
        try
        {
            var history = new HistoryService(temp);
            await history.AddAsync(new HistoryEntry { Prompt = "p", RecognizedText = "text", Answer = "answer" });
            var entries = await new HistoryService(temp).GetRecentAsync();
            Check(entries.Count == 1 && entries[0].Answer == "answer", "History survives service recreation");
            await File.WriteAllTextAsync(temp, "invalid");
            await ExpectError(async () => { await history.AddAsync(new()); return ""; }, "Corrupt history handled");
            Check(await File.ReadAllTextAsync(temp) == "invalid", "Corrupt history not overwritten");
            await history.ClearAsync(); Check((await history.GetRecentAsync()).Count == 0, "Clear history");
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static HttpClient Client(HttpStatusCode status, string body) => new(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) })));
    static async Task ExpectError(Func<Task<string>> action, string name)
    {
        try { await action(); throw new Exception("Expected error: " + name); }
        catch (InvalidOperationException ex) { Check(!ex.Message.Contains("secret provider details"), name); }
    }
    sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> action) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => action(request, token); }
}
