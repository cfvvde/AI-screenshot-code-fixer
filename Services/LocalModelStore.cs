using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace SharpLensAI.Services;

public sealed record LocalModel(string Id, string Title, string FileName, string Revision, long Size, string Sha256, string Description)
{
    public Uri DownloadUri => new($"https://huggingface.co/Qwen/{Id}-GGUF/resolve/{Revision}/{FileName}");
    public string SizeLabel => $"{Size / 1_000_000_000.0:0.00} ГБ";
}
public sealed record DownloadProgress(string Message, long Received = 0, long Total = 0)
{
    public double Percent => Total > 0 ? Math.Min(100, 100.0 * Received / Total) : 0;
}
public static class LocalModelCatalog
{
    public const string ProviderId = "Local";

    public static IReadOnlyList<LocalModel> All { get; } = Array.AsReadOnly(new[] {
        new LocalModel("Qwen3-0.6B", "Qwen3 0.6B", "Qwen3-0.6B-Q8_0.gguf", "23749fefcc72300e3a2ad315e1317431b06b590a", 639446688, "9465e63a22add5354d9bb4b99e90117043c7124007664907259bd16d043bb031", "Q8_0 · самая лёгкая модель для простых задач."),
        new LocalModel("Qwen3-1.7B", "Qwen3 1.7B", "Qwen3-1.7B-Q8_0.gguf", "90862c4b9d2787eaed51d12237eafdfe7c5f6077", 1834426016, "061b54daade076b5d3362dac252678d17da8c68f07560be70818cace6590cb1a", "Q8_0 · промежуточный вариант по размеру и качеству."),
        new LocalModel("Qwen3-4B", "Qwen3 4B", "Qwen3-4B-Q4_K_M.gguf", "bc640142c66e1fdd12af0bd68f40445458f3869b", 2497280256, "7485fe6f11af29433bc51cab58009521f205840f5b4ae3a32fa7f92e8534fdf5", "Q4_K_M · требует больше памяти и времени на обработку.")
    });
    public static LocalModel Get(string id) => All.FirstOrDefault(m => m.Id == id)
        ?? throw new InvalidOperationException("Эта локальная модель не входит в каталог SharpLens AI.");
}

public sealed class LocalModelStore
{
    private static readonly HttpClient Downloads = new() { Timeout = Timeout.InfiniteTimeSpan };
    public const string RuntimeVersion = "b8500";
    public const long RuntimeSize = 38451022;
    public const string RuntimeHash = "a23f493d8dd06de7032af0630f609b0356e7505c6597aa0bcfa06a632f6c2777";
    public static readonly Uri RuntimeUri = new("https://github.com/ggml-org/llama.cpp/releases/download/b8500/llama-b8500-bin-win-cpu-x64.zip");
    public string Root { get; }
    public string RuntimeDirectory => Path.Combine(Root, "runtime", RuntimeVersion);
    public string RuntimeExe => Path.Combine(RuntimeDirectory, "llama-server.exe");
    public LocalModelStore(string? root = null) =>
        Root = Path.GetFullPath(root ?? Path.Combine(Path.GetDirectoryName(SettingsManager.SettingsPath)!, "LocalAI"));
    public string ModelPath(LocalModel model) => Path.Combine(Root, LocalModelCatalog.Get(model.Id).Id, model.FileName);
    public bool IsDownloaded(LocalModel model)
    {
        try { var file = new FileInfo(ModelPath(model)); return file.Exists && file.Length == model.Size; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
    public bool RuntimeInstalled => File.Exists(RuntimeExe) && File.Exists(Path.Combine(RuntimeDirectory, "verified-files.json"));
    public IEnumerable<LocalModel> Installed => LocalModelCatalog.All.Where(IsDownloaded);
    public async Task InstallAsync(LocalModel model, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        model = LocalModelCatalog.Get(model.Id);
        if (System.Runtime.InteropServices.RuntimeInformation.OSArchitecture != System.Runtime.InteropServices.Architecture.X64)
            throw new InvalidOperationException("Локальный движок в этой версии рассчитан на Windows x64.");
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Root);
        using var installLock = OpenInstallLock();
        var drive = new DriveInfo(Path.GetPathRoot(Root)!);
        long required = (IsDownloaded(model) ? 0 : model.Size) + (RuntimeInstalled ? 0 : RuntimeSize * 5) + 100_000_000;
        if (drive.AvailableFreeSpace < required) throw new IOException("Недостаточно свободного места для модели и движка.");
        await InstallRuntimeAsync(progress, token);
        if (!IsDownloaded(model))
            await DownloadVerifiedAsync(Downloads, model.DownloadUri, ModelPath(model), model.Size, model.Sha256, progress, token);
    }

    private FileStream OpenInstallLock()
    {
        try { return File.Open(Path.Combine(Root, "install.lock"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None); }
        catch (IOException ex) when ((ex.HResult & 0xffff) is 32 or 33)
        {
            throw new IOException("Другая модель уже скачивается. Дождитесь завершения загрузки.", ex);
        }
    }
    private async Task InstallRuntimeAsync(IProgress<DownloadProgress> progress, CancellationToken token)
    {
        if (RuntimeInstalled) return;
        string archive = Path.Combine(Root, "runtime", "llama-" + RuntimeVersion + ".zip");
        string staging = RuntimeDirectory + ".extracting-" + Guid.NewGuid().ToString("N");
        try
        {
            progress.Report(new("Скачивание движка llama.cpp (около 38 МБ)…"));
            await DownloadVerifiedAsync(Downloads, RuntimeUri, archive, RuntimeSize, RuntimeHash, progress, token);
            progress.Report(new("Распаковка и проверка движка…"));
            await Task.Run(async () =>
            {
                ZipFile.ExtractToDirectory(archive, staging); // .NET rejects paths outside the target directory.
                token.ThrowIfCancellationRequested();
                if (!File.Exists(Path.Combine(staging, "llama-server.exe"))) throw new IOException("В архиве нет llama-server.exe.");
                var hashes = new Dictionary<string, string>();
                foreach (string file in Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories))
                {
                    token.ThrowIfCancellationRequested();
                    await using var stream = File.OpenRead(file);
                    hashes[Path.GetRelativePath(staging, file)] = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                }
                await File.WriteAllTextAsync(Path.Combine(staging, "verified-files.json"), JsonSerializer.Serialize(hashes), token);
                // A previous incomplete installation must be manually removed; never overwrite a running binary.
                if (Directory.Exists(RuntimeDirectory)) throw new IOException("Папка движка неполная. Закройте приложение, удалите LocalAI/runtime/b8500 и повторите скачивание.");
                Directory.Move(staging, RuntimeDirectory);
            }, token);
        }
        finally
        {
            if (File.Exists(archive)) File.Delete(archive);
            // Only this operation's generated staging folder is eligible for recursive cleanup.
            if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(Path.Combine(Root, "runtime")) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Directory.Exists(staging))
                Directory.Delete(staging, true);
        }
    }
    public async Task VerifyRuntimeAsync(CancellationToken token)
    {
        if (!RuntimeInstalled) throw new InvalidOperationException("Движок локального ИИ отсутствует. Откройте «Установить локальный ИИ» и нажмите «Установить движок».");
        var hashes = JsonSerializer.Deserialize<Dictionary<string, string>>(await File.ReadAllTextAsync(Path.Combine(RuntimeDirectory, "verified-files.json"), token).ConfigureAwait(false));
        if (hashes == null || !hashes.ContainsKey("llama-server.exe")) throw new IOException("Повреждена установка движка.");
        foreach (var entry in hashes)
        {
            string path = Path.GetFullPath(Path.Combine(RuntimeDirectory, entry.Key));
            if (!path.StartsWith(RuntimeDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Некорректный путь в установке движка.");
            await using var stream = File.OpenRead(path);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(stream, token)).Equals(entry.Value, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Файлы движка изменены или повреждены. Удалите LocalAI/runtime/b8500 и скачайте движок заново.");
        }
    }
    public static async Task DownloadVerifiedAsync(HttpClient client, Uri uri, string destination, long expectedSize, string expectedHash, IProgress<DownloadProgress> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = destination + ".part";
        try
        {
            using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            headersTimeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != expectedSize) throw new IOException("Размер загрузки отличается от каталога. Файл не установлен.");
            await using (var source = await response.Content.ReadAsStreamAsync(token))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                byte[] buffer = new byte[131072]; long total = 0; var clock = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(TimeSpan.FromSeconds(60));
                    int count = await source.ReadAsync(buffer, idle.Token);
                    if (count == 0) break;
                    total += count;
                    if (total > expectedSize) throw new IOException("Получен файл неожиданного размера.");
                    await target.WriteAsync(buffer.AsMemory(0, count), token); hash.AppendData(buffer, 0, count);
                    if (clock.ElapsedMilliseconds >= 150) { progress.Report(new($"Скачивание: {total / 1_000_000.0:0} / {expectedSize / 1_000_000.0:0} МБ", total, expectedSize)); clock.Restart(); }
                }
                if (total != expectedSize || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) throw new IOException("Проверка SHA-256 не пройдена. Повторите скачивание.");
            }
            token.ThrowIfCancellationRequested();
            File.Move(partial, destination, true);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}
