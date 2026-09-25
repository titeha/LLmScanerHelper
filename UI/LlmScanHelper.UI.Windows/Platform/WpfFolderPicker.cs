using System.Threading.Tasks;
using Microsoft.Win32;
using LlmScanHelper.UI.Services;

namespace LlmScanHelper.Platform;

public sealed class WpfFolderPicker : IFolderPicker
{
  public Task<string?> PickFolderAsync(string title)
  {
    var dlg = new OpenFolderDialog { Title = title };
    return Task.FromResult(dlg.ShowDialog() == true ? dlg.FolderName : (string?)null);
  }
}
