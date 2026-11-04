using System;
using System.Windows;

namespace SharpLensAI
{
    public partial class App : Application
    {
        public App()
        {
            this.DispatcherUnhandledException += (sender, e) =>
            {
                MessageBox.Show(
                    $"Произошла ошибка в UI-потоке:\n\n{e.Exception.Message}\n\n{e.Exception.StackTrace}",
                    "Ошибка приложения",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                e.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                {
                    MessageBox.Show(
                        $"Критическая ошибка:\n\n{ex.Message}\n\n{ex.StackTrace}",
                        "Критический сбой",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                }
            };
        }
    }
}