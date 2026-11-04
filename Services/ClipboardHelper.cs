using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace SharpLensAI.Services;

public static class ClipboardHelper
{
    public static async Task<bool> SetTextWithRetryAsync(
        string text,
        int attempts = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(text)) return false;

        for (var attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher is not null && !dispatcher.CheckAccess())
                    await dispatcher.InvokeAsync(() => Clipboard.SetText(text));
                else
                    Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException) when (attempt < attempts - 1)
            {
                await Task.Delay(80, cancellationToken);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }
}
