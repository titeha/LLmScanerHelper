using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using LlmScanHelper.UI.Services;
using LlmScanHelper.Views;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Platform;

/// <summary>Модальные окна настроек/справки поверх главного окна (аналог WpfUiWindows).</summary>
public sealed class AvaloniaUiWindows : IUiWindows
{
  public void OpenSettings(MainViewModel vm)
  {
    var owner = Owner();
    if (owner is not null) _ = new SettingsWindow(vm).ShowDialog(owner);
  }

  public void OpenHelp(MainViewModel vm)
  {
    var owner = Owner();
    if (owner is not null) _ = new HelpWindow(vm).ShowDialog(owner);
  }

  public void OpenReasonBudgetMessagesEditor(MainViewModel vm)
  {
    var owner = Owner();
    if (owner is not null) _ = new ReasonBudgetMessagesEditorWindow(vm).ShowDialog(owner);
  }

  private static Window? Owner() =>
    Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
      ? desktop.MainWindow
      : null;
}
