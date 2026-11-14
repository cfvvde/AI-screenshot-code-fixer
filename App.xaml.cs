using System.Windows;
namespace SharpLensAI;
public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            MessageBox.Show("Не удалось завершить действие. Повторите операцию; если ошибка повторяется, перезапустите приложение.", "SharpLens AI", MessageBoxButton.OK, MessageBoxImage.Warning);
            e.Handled = true;
        };
    }
}
