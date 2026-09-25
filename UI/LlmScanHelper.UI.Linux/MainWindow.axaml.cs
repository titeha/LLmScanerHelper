using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using LlmScanHelper.Platform;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Views;

public partial class MainWindow : Window
{
  public MainWindow()
  {
    InitializeComponent();
    DataContext = new MainViewModel(new AvaloniaClipboard(), new AvaloniaUiWindows(), new AvaloniaFolderPicker());

    // Инициализация после показа окна (аналог WPF Loaded → InitializeAsync).
    Opened += async (_, _) =>
    {
      if (DataContext is not MainViewModel vm) return;
      try { await vm.InitializeAsync(); }
      catch (Exception ex)
      {
        var box = new Window { Content = new TextBlock { Text = "Ошибка инициализации: " + ex.Message, Margin = new Avalonia.Thickness(16) }, Width = 420, Height = 160 };
        await box.ShowDialog(this);
      }
    };

    // Финный сброс сохранения при закрытии (аналог WPF Closing → FlushPendingSave).
    Closed += (_, _) =>
    {
      if (DataContext is MainViewModel vm)
        vm.FlushPendingSave();
    };
  }
}