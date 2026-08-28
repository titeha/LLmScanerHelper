using System.Windows;

namespace LlmScanHelper
{
    public partial class App : Application
    {
        protected override void OnExit(ExitEventArgs e)
        {
            // Финальное сохранение параметров (JSON рядом с exe)
            if (MainWindow?.DataContext is ViewModels.MainViewModel vm)
                vm.SaveNow();

            base.OnExit(e);
        }
    }
}
