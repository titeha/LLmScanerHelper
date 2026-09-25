using LlmScanHelper.ViewModels;

namespace LlmScanHelper.UI.Services;

/// <summary>Открытие служебных окон (реализация — в UI-проекте).</summary>
public interface IUiWindows
{
  void OpenSettings(MainViewModel vm);
  void OpenHelp(MainViewModel vm);
  void OpenReasonBudgetMessagesEditor(MainViewModel vm);
}
