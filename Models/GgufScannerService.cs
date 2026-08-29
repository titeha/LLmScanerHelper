using System.IO;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Сканер папки моделей: .gguf (кроме mmproj*) + publisher из пути + mmproj-файлы.
  /// </summary>
  public static class GgufScannerService
  {
    public sealed class ScanResult
    {
      public List<ModelEntry> Models { get; init; } = new();
      public string? Error { get; init; }
    }

    public static ScanResult Scan(string root)
    {
      try
      {
        var models = new List<ModelEntry>();
        // папка (без регистра) -> mmproj-файлы в ней
        var mmprojByDir = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(root, "*.gguf", SearchOption.AllDirectories))
        {
          var name = Path.GetFileName(path);
          if (name.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase))
          {
            var dir = Path.GetDirectoryName(path) ?? "";
            if (!mmprojByDir.TryGetValue(dir, out var list))
              mmprojByDir[dir] = list = [];
            list.Add(path);
            continue;
          }

          string fileName = Path.GetFileNameWithoutExtension(path);
          string publisher = DetectPublisher(path);

          string display = string.IsNullOrEmpty(publisher) ? fileName : $"{fileName} [{publisher}]";
          models.Add(new ModelEntry
          {
            FullPath = path,
            FileName = fileName,
            Publisher = publisher,
            DisplayName = display
          });
        }

        models.Sort((x, y) => string.Compare(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase));

        // Локальные mmproj для каждой модели (та же папка) — только они и нужны
        foreach (var m in models)
        {
          var dir = Path.GetDirectoryName(m.FullPath) ?? "";
          if (mmprojByDir.TryGetValue(dir, out var list))
          {
            list.Sort(StringComparer.OrdinalIgnoreCase);
            m.LocalMmproj.AddRange(list);
          }
        }

        return new ScanResult { Models = models };
      }
      catch (Exception ex)
      {
        return new ScanResult { Error = ex.Message };
      }
    }

    private static string DetectPublisher(string path)
    {
      var dirs = Path.GetDirectoryName(path)?.Split(Path.DirectorySeparatorChar);
      if (dirs == null) return "";

      foreach (var d in dirs.Reverse())
      {
        if (d.Equals("models", StringComparison.OrdinalIgnoreCase) ||
          d.Equals("llstudio", StringComparison.OrdinalIgnoreCase))
          continue;

        if (d.Contains("unsloth", StringComparison.OrdinalIgnoreCase) ||
          d.Contains("lmstudio", StringComparison.OrdinalIgnoreCase) ||
          d.Contains("community", StringComparison.OrdinalIgnoreCase) ||
          d.Contains("mradermacher", StringComparison.OrdinalIgnoreCase) ||
          d.Contains("ornith", StringComparison.OrdinalIgnoreCase))
        {
          return d;
        }
      }
      return "";
    }
  }
}
