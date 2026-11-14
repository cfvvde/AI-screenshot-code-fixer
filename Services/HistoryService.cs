using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using SharpLensAI.Models;

namespace SharpLensAI.Services;

public sealed class HistoryService
{
    private readonly string? _path;
    public HistoryService(string? path = null) => _path = path;
    private const int MaxEntries = 300;
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string HistoryPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SharpLensAI");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "history.json");
        }
    }

    public async Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAllCoreAsync(cancellationToken).ConfigureAwait(false);
            if (entry.Id == Guid.Empty) entry.Id = Guid.NewGuid();
            if (entry.CreatedAt == default) entry.CreatedAt = DateTime.Now;
            entries.Insert(0, entry);
            if (entries.Count > MaxEntries)
                entries = entries.Take(MaxEntries).ToList();
            await WriteAllCoreAsync(entries, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task<IReadOnlyList<HistoryEntry>> GetRecentAsync(int count = 100, CancellationToken cancellationToken = default)
    {
        await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var entries = await ReadAllCoreAsync(cancellationToken).ConfigureAwait(false);
            return entries.OrderByDescending(entry => entry.CreatedAt)
                          .Take(Math.Clamp(count, 1, MaxEntries))
                          .ToList();
        }
        finally
        {
            FileLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await FileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await WriteAllCoreAsync(new List<HistoryEntry>(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            FileLock.Release();
        }
    }

    private async Task<List<HistoryEntry>> ReadAllCoreAsync(CancellationToken cancellationToken)
    {
        var path = _path ?? HistoryPath;
        if (!File.Exists(path)) return new List<HistoryEntry>();

        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<List<HistoryEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<HistoryEntry>();
        }
        catch (JsonException)
        {
            // Не затираем пользовательский файл автоматически, если JSON повреждён.
            throw new InvalidOperationException($"Файл истории повреждён: {path}");
        }
    }

    private async Task WriteAllCoreAsync(List<HistoryEntry> entries, CancellationToken cancellationToken)
    {
        var path = _path ?? HistoryPath;
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, path, overwrite: true);
    }
}
