using System.Net.Http;
using System.Text.Json;
namespace SharpLensAI.Services;

public static class OpenAiErrorMessage
{
    public static string ForRateLimit(string body, TimeSpan? retryAfter)
    {
        string? code = null, type = null;
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                if (error.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String) code = c.GetString();
                if (error.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String) type = t.GetString();
            }
        }
        catch (JsonException) { }
        // Only our fixed messages are shown; never echo server text, keys or request content.
        string? detail = code switch
        {
            "credit_balance_exhausted" => "Баланс API исчерпан. Проверьте Billing в OpenAI Platform и пополните баланс при необходимости.",
            "billing_not_active" => "Оплата API не активна. Проверьте Billing в OpenAI Platform.",
            "project_spend_limit_exceeded" => "Достигнут лимит расходов проекта. Проверьте лимит проекта в OpenAI Platform или дождитесь его сброса.",
            "organization_spend_limit_exceeded" => "Достигнут лимит расходов организации. Проверьте лимит организации в OpenAI Platform или дождитесь его сброса.",
            "organization_usage_limit_exceeded" => "Достигнут лимит использования организации. Запросите повышение лимита в OpenAI Platform.",
            "insufficient_quota" or "usage_limit_exceeded" => "Недостаточно квоты API. Проверьте баланс, доступ к API и лимиты проекта в OpenAI Platform. Изменение тайм-аута в SharpLens эту ошибку не устраняет.",
            "rate_limit_exceeded" or "slow_down" => Temporary(retryAfter),
            _ => null
        };
        if (detail == null && type == "insufficient_quota")
            detail = "Недостаточно квоты API. Проверьте Billing и Limits в OpenAI Platform. Изменение настроек SharpLens не увеличивает квоту аккаунта.";
        if (detail == null && type == "rate_limit_error") detail = Temporary(retryAfter);
        return "OpenAI (HTTP 429): " + (detail ?? "Причина ограничения не уточнена. Проверьте баланс и лимиты API в OpenAI Platform; если они доступны, повторите запрос позже.");
    }
    private static string Temporary(TimeSpan? delay)
    {
        string wait = delay.HasValue && delay.Value > TimeSpan.Zero
            ? $"Повторите не раньше чем через {Math.Ceiling(delay.Value.TotalSeconds):0} сек."
            : "Подождите перед повторной попыткой.";
        return "Превышена частота запросов или объём токенов. " + wait + " При необходимости сократите исходный текст и «Макс. токенов» в настройках.";
    }
}
