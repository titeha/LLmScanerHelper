using System.Windows;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Views
{
  public partial class MainWindow : Window
  {
    public MainWindow()
    {
      InitializeComponent();
      DataContext = new MainViewModel();

      // Инициализация после показа окна: сканирование моделей + восстановление последней
      Loaded += async (_, _) =>
      {
        if (DataContext is not MainViewModel vm) return;
        try
        {
          await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
          MessageBox.Show("Ошибка инициализации: " + ex.Message, "LLStudio Bench",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        }
      };

      // Финальное сохранение при закрытии (дополнительно к дебаунсу и App.OnExit)
      Closing += (_, _) =>
      {
        if (DataContext is MainViewModel vm)
          vm.FlushPendingSave();
      };
    }
  }
}
