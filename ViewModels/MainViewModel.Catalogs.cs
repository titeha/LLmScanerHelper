using System.Collections.ObjectModel;

using LlmScanHelper.Models;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Частичный класс: корневые каталоги с моделями (список, выбор, добавление/удаление).
  /// </summary>
  public sealed partial class MainViewModel
  {
    // ==================== Каталоги ====================

    // Список корневых каталогов с моделями (ранее — одна строка ModelsRoot).
    public ObservableCollection<string> Catalogs { get; } = new();

    private string _selectedCatalog = AppDefaults.ModelsRoot;
    public string SelectedCatalog
    {
      get => _selectedCatalog;
      set { if (Set(ref _selectedCatalog, value)) { OnPropertyChanged(nameof(SelectedCatalogText)); SaveSoon(); } }
    }

    // Read-only текст активного каталога для индикатора на панели.
    public string SelectedCatalogText =>
      string.IsNullOrEmpty(SelectedCatalog) ? "Каталог не выбран" : SelectedCatalog;

    private void ApplyCatalogsFromStore()
    {
      _suppressSave = true;
      try
      {
        Catalogs.Clear();
        foreach (var c in _store.Settings.Catalogs)
          Catalogs.Add(c);
        if (Catalogs.Count == 0)
        {
          Catalogs.Add(AppDefaults.ModelsRoot);
        }
        SelectedCatalog = Catalogs.Count > 0
          ? Catalogs[Math.Clamp(_store.Settings.SelectedCatalogIndex, 0, Catalogs.Count - 1)]
          : AppDefaults.ModelsRoot;
      }
      finally { _suppressSave = false; }
    }

    private string? PromptForCatalogPath()
    {
      var dlg = new Microsoft.Win32.OpenFolderDialog
      {
        Title = "Выберите папку с моделями"
      };
      if (dlg.ShowDialog() == true)
        return dlg.FolderName;
      return null;
    }

    private void AddCatalog()
    {
      var path = PromptForCatalogPath();
      if (string.IsNullOrEmpty(path)) return;
      if (!Catalogs.Contains(path, StringComparer.OrdinalIgnoreCase))
      {
        Catalogs.Add(path);
        SelectedCatalog = path;
        SaveSoon();
      }
    }

    private void RemoveCatalog()
    {
      if (Catalogs.Count <= 1) return;
      var idx = Catalogs.IndexOf(SelectedCatalog);
      if (idx < 0) return;
      Catalogs.RemoveAt(idx);
      SelectedCatalog = Catalogs[Math.Clamp(idx, 0, Catalogs.Count - 1)];
      SaveSoon();
    }

    private void EditCatalog()
    {
      var path = PromptForCatalogPath();
      if (string.IsNullOrEmpty(path)) return;
      if (!Catalogs.Contains(path, StringComparer.OrdinalIgnoreCase))
      {
        var idx = Catalogs.IndexOf(SelectedCatalog);
        if (idx >= 0)
        {
          Catalogs[idx] = path;
          SelectedCatalog = path;
          SaveSoon();
        }
      }
    }
  }
}
