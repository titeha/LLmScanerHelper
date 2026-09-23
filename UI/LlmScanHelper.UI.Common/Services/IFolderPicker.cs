namespace LlmScanHelper.UI.Services;

/// <summary>Диалог выбора папки (платформенная реализация — в UI-проекте).</summary>
public interface IFolderPicker
{
  /// <returns>Выбранный путь или null, если отмена.</returns>
  string? PickFolder(string title);
}
