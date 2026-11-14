using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using SharpLensAI.Services;

namespace SharpLensAI;

public partial class LocalModelsWindow : Window
{
    private readonly LocalModelStore _store;
    private CancellationTokenSource? _download;

    public LocalModelsWindow(LocalModelStore store)
    {
        InitializeComponent();
        _store = store;
        FolderPath.Text = _store.Root;
        RefreshModels();
        Activated += (_, _) => RefreshModels();
    }

    private void RefreshModels()
    {
        bool engineInstalled = _store.RuntimeInstalled;
        ModelsList.ItemsSource = LocalModelCatalog.All.Select(model =>
        {
            bool downloaded = _store.IsDownloaded(model);
            return new
            {
                Model = model,
                Details = $"{model.SizeLabel} · {model.Description}",
                Status = !downloaded ? "Не скачана" : engineInstalled ? "Скачана" : "Скачана · нужен движок",
                Action = !downloaded ? "Скачать" : engineInstalled ? "Установлена" : "Установить движок",
                CanInstall = _download == null && (!downloaded || !engineInstalled)
            };
        }).ToArray();
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_download != null || sender is not Button { Tag: LocalModel model }) return;

        using var download = new CancellationTokenSource();
        _download = download;
        RefreshModels();
        CloseButton.IsEnabled = false;
        CancelDownloadButton.IsEnabled = true;
        CancelDownloadButton.Visibility = DownloadBar.Visibility = Visibility.Visible;
        DownloadBar.IsIndeterminate = true;
        DownloadStatus.Text = $"Подготовка к установке {model.Title}…";
        var progress = new Progress<DownloadProgress>(update =>
        {
            if (_download != download || download.IsCancellationRequested) return;
            DownloadBar.IsIndeterminate = update.Total == 0;
            DownloadBar.Value = update.Percent;
            DownloadStatus.Text = $"{model.Title} · {update.Message}";
        });
        try
        {
            await _store.InstallAsync(model, progress, download.Token);
            DownloadStatus.Text = $"{model.Title} доступна в списке ИИ-провайдеров.";
        }
        catch (OperationCanceledException) when (download.IsCancellationRequested)
        {
            DownloadStatus.Text = "Загрузка отменена.";
        }
        catch (OperationCanceledException)
        {
            DownloadStatus.Text = "Сервер загрузки не ответил вовремя. Попробуйте ещё раз.";
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = $"Не удалось установить {model.Title}. {ex.Message}";
        }
        finally
        {
            _download = null;
            CancelDownloadButton.Visibility = DownloadBar.Visibility = Visibility.Collapsed;
            CloseButton.IsEnabled = true;
            RefreshModels();
        }
    }

    private void CancelDownload_Click(object sender, RoutedEventArgs e) => CancelDownload();

    private void CancelDownload()
    {
        _download?.Cancel();
        CancelDownloadButton.IsEnabled = false;
        DownloadStatus.Text = "Отмена загрузки…";
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_download != null)
        {
            e.Cancel = true;
            CancelDownload();
        }
        base.OnClosing(e);
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(_store.Root);
            Process.Start(new ProcessStartInfo(_store.Root) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            DownloadStatus.Text = $"Не удалось открыть папку моделей. {ex.Message}";
        }
    }
}
