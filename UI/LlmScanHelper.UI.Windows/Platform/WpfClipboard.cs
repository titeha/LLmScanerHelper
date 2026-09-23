using System.Windows;
using LlmScanHelper.UI.Services;

namespace LlmScanHelper.Platform;

public sealed class WpfClipboard : IClipboard
{
  public void SetText(string text) => Clipboard.SetText(text);
}
