using Avalonia.Controls;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Views;

public partial class SettingsWindow : Window
{
  // Дефолтный конструктор доступен загрузчику ресурсов (AVLN3001); на практике окно создаётся через VM.
  public SettingsWindow() { }

  public SettingsWindow(MainViewModel vm)
  {
    InitializeComponent();
    DataContext = vm;
  }
}
