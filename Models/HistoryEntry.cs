using System;

namespace SharpLensAI.Models;

public sealed class HistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Prompt { get; set; } = string.Empty;
    public string RecognizedText { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public string OcrLanguage { get; set; } = string.Empty;

    public string Title
    {
        get
        {
            var task = string.IsNullOrWhiteSpace(Prompt) ? "Запрос к ИИ" : Prompt.Trim().Replace('\r', ' ').Replace('\n', ' ');
            if (task.Length > 64) task = task[..61] + "…";
            return $"{CreatedAt:dd.MM.yyyy HH:mm} — {task}";
        }
    }

    public string Summary
    {
        get
        {
            var text = (RecognizedText ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Trim();
            return text.Length > 110 ? text[..107] + "…" : text;
        }
    }
}
