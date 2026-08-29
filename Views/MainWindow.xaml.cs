using System.Windows;
using LlmScanHelper.ViewModels;
using MahApps.Metro.Controls;

namespace LlmScanHelper.Views
{
  public partial class MainWindow : MetroWindow
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
          MessageBox.Show("Ошибка инициализации: " + ex.Message, "LLM Scan Helper",
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
