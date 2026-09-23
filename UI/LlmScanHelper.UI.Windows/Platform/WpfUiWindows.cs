using LlmScanHelper.UI.Services;
using LlmScanHelper.ViewModels;
using LlmScanHelper.Views;

namespace LlmScanHelper.Platform;

public sealed class WpfUiWindows : IUiWindows
{
  public void OpenSettings(MainViewModel vm) => new SettingsWindow(vm).ShowDialog();
  public void OpenHelp(MainViewModel vm) => new HelpWindow(vm).ShowDialog();
}
