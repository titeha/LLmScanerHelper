using Avalonia.Controls;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Views;

public partial class HelpWindow : Window
{
  // Дефолтный конструктор доступен загрузчику ресурсов (AVLN3001); на практике окно создаётся через VM.
  public HelpWindow() { }

  public HelpWindow(MainViewModel vm)
  {
    InitializeComponent();
    DataContext = vm;
  }
}
