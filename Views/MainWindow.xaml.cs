using System.Windows;
using System.Windows.Input;
using LlmScanHelper.ViewModels;
using MahApps.Metro.Controls;
using MvvmUtilites;

namespace LlmScanHelper.Views
{
  public partial class MainWindow : MetroWindow
  {
    public MainWindow()
    {
      InitializeComponent();
      DataContext = new MainViewModel();

      // Команды в заголовке (Настройки/Справка) живут в MainViewModel,
      // а не в code-behind: контекст биндинга WindowCommands = DataContext окна.
      // Они открывают модальные окна, блокирующие главное до закрытия.

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
