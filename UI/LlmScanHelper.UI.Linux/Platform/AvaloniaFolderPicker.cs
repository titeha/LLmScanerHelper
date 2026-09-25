using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using LlmScanHelper.UI.Services;

namespace LlmScanHelper.Platform;

/// <summary>Вор папки через StorageProvider (асинхронно, на UI-потоке).</summary>
public sealed class AvaloniaFolderPicker : IFolderPicker
{
  public async Task<string?> PickFolderAsync(string title)
  {
    var main = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
    var provider = main is null ? null : TopLevel.GetTopLevel(main)?.StorageProvider;
    if (provider is null) return null;

    var folders = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
    {
      Title = title,
      AllowMultiple = false,
    });
    return folders.Count > 0 ? folders[0].Path?.LocalPath : null;   // IStorageFolder.Path — Uri → LocalPath на desktop
  }
}
