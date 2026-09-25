using System.Threading.Tasks;

namespace LlmScanHelper.UI.Services;

/// <summary>Диалог выбора папки (платформенная реализация — в UI-проекте).</summary>
public interface IFolderPicker
{
  /// <returns>Выбранный путь или null, если отмена.</returns>
  Task<string?> PickFolderAsync(string title);
}
