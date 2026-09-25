using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;             // DataTransfer, DataTransferItem
using Avalonia.Input.Platform;    // IClipboard

namespace LlmScanHelper.Platform;

/// <summary>Буфер обмена на Avalonia (асинхронный; VM не жёт результата — fire-and-forget).</summary>
public sealed class AvaloniaClipboard : LlmScanHelper.UI.Services.IClipboard
{
  public void SetText(string text)
  {
    var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    if (main is null) return;

    var clipboard = TopLevel.GetTopLevel(main)?.Clipboard;
    if (clipboard is not null)
    {
      var data = new DataTransfer();
      data.Add(DataTransferItem.CreateText(text));
      _ = clipboard.SetDataAsync(data);   // fire-and-forget; не блокируем UI-поток
    }
  }
}
