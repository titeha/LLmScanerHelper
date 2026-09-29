using System.IO;
using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Сканер папки моделей: .gguf (кроме mmproj*) + publisher из пути + mmproj-файлы.
  /// Шарды одного набора (`stem-0000N-of-0000M.gguf` в одной папке) — одна запись каталога.
  /// Содержимое файлов не читается: только имена и размеры.
  /// </summary>
  public static class GgufScannerService
  {
    // Единственная схема имён, которую порождает llama.cpp (src/llama.cpp:544-593).
    private static readonly Regex ShardRx = new(
      @"^(?<stem>.+)-(?<no>\d{5})-of-(?<count>\d{5})\.gguf$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public sealed class ScanResult
    {
      public List<ModelEntry> Models { get; init; } = new();

      public string? Error { get; init; }

      // Нефатальные предупреждения (например, не удалось прочитать один из каталогов).
      // Модели из успешно прочитанных каталогов всё равно возвращаются.
      public List<string> Warnings { get; init; } = new();
    }

    /// <summary>Сканирует все каталоги и объединяет найденные модели (логика «и»).
    /// Пустые/пробельные каталоги пропускаются. Ошибка чтения одного каталога не скрывает
    /// модели, успешно найденные в остальных — они попадают в Warnings.
    /// <see cref="ScanResult.Error"/> устанавливается, только если не задано ни одного
    /// непустого каталога или ни один каталог не прочитался.</summary>
    public static ScanResult ScanCatalogs(IEnumerable<string>? roots)
    {
      var models = new List<ModelEntry>();
      var warnings = new List<string>();
      var errors = new List<string>();
      bool anyRoot = false;
      bool readable = false;

      foreach (var root in roots ?? Enumerable.Empty<string>())
      {
        if (string.IsNullOrWhiteSpace(root)) continue;
        anyRoot = true;
        var res = Scan(root);
        if (res.Error != null)
        {
          warnings.Add(root + ": " + res.Error);
          errors.Add(root + ": " + res.Error);
        }
        else
        {
          readable = true;
          models.AddRange(res.Models);
        }
      }

      if (!anyRoot)
        return new ScanResult { Error = "Каталог с моделями не указан" };

      models.Sort((x, y) =>
      {
        int c = string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
        return c != 0 ? c : string.Compare(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase);
      });

      if (!readable && errors.Count > 0)
        return new ScanResult { Error = "Не прочитать каталоги моделей: " + string.Join("; ", errors) };

      return new ScanResult { Models = models, Warnings = warnings };
    }

    public static ScanResult Scan(string root)
    {
      try
      {
        var models = new List<ModelEntry>();
        // папка (без регистра) -> (stem -> группа mmproj)
        var mmprojByDir = new Dictionary<string, Dictionary<string, MmprojGroup>>(StringComparer.OrdinalIgnoreCase);
        // папка + "\0" + stem (без регистра) -> группа шардов (одинаковый stem в разных папках не сливается)
        var groups = new Dictionary<string, SplitGroup>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(root, "*.gguf", SearchOption.AllDirectories))
        {
          var name = Path.GetFileName(path);
          var dir = Path.GetDirectoryName(path) ?? "";

          if (name.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase))
          {
            AddMmproj(mmprojByDir, dir, name, path);
            continue;
          }

          if (TryParseShard(name, out var stem, out int no, out int count))
          {
            string key = dir + "\0" + stem.ToLowerInvariant();
            if (!groups.TryGetValue(key, out var grp))
              groups[key] = grp = new SplitGroup { Stem = stem };
            grp.Shards[no] = path;
            grp.Counts[no] = count;
          }
          else
          {
            models.Add(MakeSingle(path, name));
          }
        }

        foreach (var grp in groups.Values)
          models.Add(grp.ToEntry());

        models.Sort((x, y) =>
        {
          int c = string.Compare(x.DisplayName, y.DisplayName, StringComparison.OrdinalIgnoreCase);
          return c != 0 ? c : string.Compare(x.FullPath, y.FullPath, StringComparison.OrdinalIgnoreCase);
        });

        // Локальные mmproj из той же папки: в LocalMmproj — по одному представителю на набор,
        // полный список шардов — в MmprojShardPaths.
        foreach (var m in models)
        {
          var dir = Path.GetDirectoryName(m.FullPath) ?? "";
          if (!mmprojByDir.TryGetValue(dir, out var gs)) continue;

          var ordered = gs.Values.OrderBy(g => g.Stem, StringComparer.OrdinalIgnoreCase).ToList();
          m.LocalMmproj.AddRange(ordered.Select(g => g.RepresentativePath));
          foreach (var g in ordered) m.MmprojShardPaths.AddRange(g.OrderedPaths);
        }

        return new ScanResult { Models = models };
      }
      catch (Exception ex)
      {
        return new ScanResult { Error = ex.Message };
      }
    }

    private static ModelEntry MakeSingle(string path, string name)
    {
      string fileName = Path.GetFileNameWithoutExtension(name);
      string publisher = DetectPublisher(path);
      string display = string.IsNullOrEmpty(publisher) ? fileName : $"{fileName} [{publisher}]";
      // Q3 (S6): draft-файлы помечаем подписью, чтобы пользователь не путал их с основной моделью.
      bool isDraft = IsDraftName(fileName);
      if (isDraft) display += " [draft]";

      return new ModelEntry
      {
        FullPath = path,
        FileName = fileName,
        Publisher = publisher,
        DisplayName = display,
        IsSplit = false,
        ShardPaths = new[] { path },
        ShardCount = 1,
        MissingShards = Array.Empty<int>(),
        TotalSizeBytes = FileSizeOrZero(path),
        Kind = isDraft ? ModelKind.Draft : ModelKind.Main
      };
    }

    private static void AddMmproj(Dictionary<string, Dictionary<string, MmprojGroup>> byDir,
      string dir, string name, string path)
    {
      if (!byDir.TryGetValue(dir, out var gs))
        byDir[dir] = gs = new Dictionary<string, MmprojGroup>(StringComparer.OrdinalIgnoreCase);

      if (TryParseShard(name, out var stem, out int no, out _))
      {
        if (!gs.TryGetValue(stem, out var g)) gs[stem] = g = new MmprojGroup { Stem = stem };
        g.Paths[no] = path;
      }
      else
      {
        string stem2 = Path.GetFileNameWithoutExtension(name);
        if (!gs.TryGetValue(stem2, out var g)) gs[stem2] = g = new MmprojGroup { Stem = stem2 };
        g.Paths[0] = path; // одиночный проектор — без номера шарда
      }
    }

    private static bool TryParseShard(string fileName, out string stem, out int no, out int count)
    {
      stem = "";
      no = 0;
      count = 0;
      var m = ShardRx.Match(fileName);
      if (!m.Success) return false;
      if (!int.TryParse(m.Groups["no"].Value, out no)) return false;
      if (!int.TryParse(m.Groups["count"].Value, out count)) return false;
      stem = m.Groups["stem"].Value;
      return true;
    }

    /// <summary>Draft-модель (`--model-draft`): `mtp-…`, `draft-…` или `…-mtp…`/`…-draft…`.</summary>
    private static bool IsDraftName(string name)
    {
      if (name.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)) return true;
      if (name.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)) return true;
      if (name.IndexOf("-draft", StringComparison.OrdinalIgnoreCase) >= 0) return true;
      if (name.IndexOf("-mtp", StringComparison.OrdinalIgnoreCase) >= 0) return true;
      return false;
    }

    private static long FileSizeOrZero(string path)
    {
      try { return new FileInfo(path).Length; } catch { return 0; }
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
          return d;
      }
      return "";
    }

    /// <summary>Набор шардов одного stem в одной папке.</summary>
    private sealed class SplitGroup
    {
      public string Stem = "";
      public readonly SortedDictionary<int, string> Shards = new();
      public readonly Dictionary<int, int> Counts = new();

      public ModelEntry ToEntry()
      {
        int firstNo = Shards.Keys.First();
        int declaredCount = Counts[firstNo];

        var shardPaths = new List<string>(Shards.Count);
        long total = 0;
        foreach (var kv in Shards)
        {
          shardPaths.Add(kv.Value);
          total += FileSizeOrZero(kv.Value);
        }

        // Полный список отсутствующих номеров 1..ShardCount. count — 5 цифр, поэтому список ограничен.
        var missing = new List<int>();
        for (int i = 1; i <= declaredCount; i++)
          if (!Shards.ContainsKey(i)) missing.Add(i);

        string fullPath = Shards.TryGetValue(1, out var p1) ? p1 : Shards[firstNo];
        string publisher = DetectPublisher(fullPath);
        string display = string.IsNullOrEmpty(publisher) ? Stem : $"{Stem} [{publisher}]";
        // Q3 (S6): draft-набор тоже помечаем подписью, чтобы пользователь не путал его с основной моделью.
        bool isDraft = IsDraftName(Stem);
        if (isDraft) display += " [draft]";

        return new ModelEntry
        {
          FullPath = fullPath,
          FileName = Stem,
          Publisher = publisher,
          DisplayName = display,
          IsSplit = true,
          ShardPaths = shardPaths,
          ShardCount = declaredCount,
          MissingShards = missing,
          TotalSizeBytes = total,
          Kind = isDraft ? ModelKind.Draft : ModelKind.Main
        };
      }
    }

    /// <summary>Набор mmproj-файлов одного stem в одной папке (может быть одиночным).</summary>
    private sealed class MmprojGroup
    {
      public string Stem = "";
      public readonly SortedDictionary<int, string> Paths = new();

      public string RepresentativePath =>
        Paths.TryGetValue(1, out var p1) ? p1 : (Paths.Count > 0 ? Paths.Values.First() : "");

      public IEnumerable<string> OrderedPaths => Paths.Values;
    }
  }
}
