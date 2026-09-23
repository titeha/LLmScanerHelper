namespace LlmScanHelper.UI.Services;

/// <summary>Буфер обмена (платформенная реализация — в UI-проекте).</summary>
public interface IClipboard
{
  void SetText(string text);
}
