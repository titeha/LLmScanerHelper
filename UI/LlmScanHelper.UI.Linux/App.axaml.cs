using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace LlmScanHelper;

public partial class App : Application
{
  public override void Initialize() => AvaloniaXamlLoader.Load(this);

  public override void OnFrameworkInitializationCompleted()
  {
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
      desktop.MainWindow = new Views.MainWindow();

      // Финное сохранение параметров при выходе (аналог WPF App.OnExit).
      desktop.Exit += (_, _) =>
      {
        if (desktop.MainWindow?.DataContext is ViewModels.MainViewModel vm)
          vm.SaveNow();
      };
    }

    base.OnFrameworkInitializationCompleted();
  }
}