using Microsoft.Win32;
using LlmScanHelper.UI.Services;

namespace LlmScanHelper.Platform;

public sealed class WpfFolderPicker : IFolderPicker
{
  public string? PickFolder(string title)
  {
    var dlg = new OpenFolderDialog { Title = title };
    return dlg.ShowDialog() == true ? dlg.FolderName : null;
  }
}
