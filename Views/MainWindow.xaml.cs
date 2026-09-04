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

      // Кнопки в заголовке открывают модальные окна (Настройки/Справка),
      // блокирующие главное до закрытия. Контент берётся из тех же UserControl.
      OpenSettingsCommand = new RelayCommand(() => OpenSettings());
      OpenHelpCommand = new RelayCommand(() => OpenHelp());

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

    // ==================== Поля команд в заголовке ====================

    private RelayCommand? OpenSettingsCommand;
    private RelayCommand? OpenHelpCommand;

    private void OpenSettings()
    {
      var vm = DataContext as MainViewModel;
      new SettingsWindow(vm).ShowDialog();
    }

    private void OpenHelp()
    {
      var vm = DataContext as MainViewModel;
      new HelpWindow(vm).ShowDialog();
    }
  }
}
