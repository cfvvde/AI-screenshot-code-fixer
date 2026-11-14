using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace SharpLensAI.Services;

internal sealed class LocalModelRunner
{
    private static readonly HttpClient Client = new(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
    {
        Timeout = Timeout.InfiniteTimeSpan
    };

    private readonly LocalModelStore _store = new();

    public async Task<string> AskAsync(AppSettings settings, string prompt, string text, CancellationToken token)
    {
        var model = LocalModelCatalog.Get(settings.Model);
        if (!_store.IsDownloaded(model))
            throw new InvalidOperationException($"Модель {model.Title} отсутствует. Скачайте её через «Установить локальный ИИ» в настройках.");
        await _store.VerifyRuntimeAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();

        int port;
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var start = new ProcessStartInfo(_store.RuntimeExe)
        {
            WorkingDirectory = _store.RuntimeDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (string variable in start.Environment.Keys.Where(name => name.StartsWith("LLAMA_", StringComparison.OrdinalIgnoreCase)).ToArray())
            start.Environment.Remove(variable);
        start.Environment["LLAMA_API_KEY"] = key;
        long contextSize = Math.Clamp(settings.MaxTokens + 4096L, 8192, 32768);
        foreach (string argument in new[] {
            "--model", _store.ModelPath(model), "--alias", model.Id,
            "--host", "127.0.0.1", "--port", port.ToString(CultureInfo.InvariantCulture),
            "--ctx-size", contextSize.ToString(CultureInfo.InvariantCulture),
            "--parallel", "1", "--reasoning", "off", "--no-webui"
        })
            start.ArgumentList.Add(argument);

        using var server = new Process { StartInfo = start };
        try
        {
            if (!server.Start()) throw new InvalidOperationException("Не удалось запустить движок llama.cpp.");
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Не удалось запустить движок llama.cpp. Проверьте установку локальной модели.", ex);
        }

        try
        {
            var address = new Uri($"http://127.0.0.1:{port}");
            await WaitUntilReadyAsync(server, new Uri(address, "/health"), token).ConfigureAwait(false);
            var localSettings = new AppSettings
            {
                Provider = LocalModelCatalog.ProviderId,
                Model = model.Id,
                Endpoint = new Uri(address, "/v1/chat/completions").AbsoluteUri,
                CustomApiKey = key,
                MaxTokens = settings.MaxTokens
            };
            return await new LlmService(Client).SendAsync(localSettings, new CompatibleProvider(), prompt, text, token).ConfigureAwait(false);
        }
        finally
        {
            if (!server.HasExited)
            {
                try { server.Kill(); }
                catch (Win32Exception) when (server.HasExited) { }
            }
            await server.WaitForExitAsync();
        }
    }

    private static async Task WaitUntilReadyAsync(Process server, Uri health, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (server.HasExited)
                throw new InvalidOperationException($"Движок llama.cpp завершился при загрузке модели (код {server.ExitCode}). Проверьте установку и доступную память.");
            using var probe = CancellationTokenSource.CreateLinkedTokenSource(token);
            probe.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                using var response = await Client.GetAsync(health, HttpCompletionOption.ResponseHeadersRead, probe.Token);
                if (response.IsSuccessStatusCode && !server.HasExited) return;
            }
            catch (HttpRequestException) { }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            await Task.Delay(250, token);
        }
    }
}
