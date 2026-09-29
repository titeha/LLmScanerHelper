using System.IO;
using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Читатель GGUF с агрегацией multi-file (splits): метаданные — из шарда <c>split.no == 0</c>,
  /// тензоры — со всех найденных шардов, размеры — по агрегату. За один вызов читаются только
  /// заголовки шардов (data section не читается). Одиночная модель проходит без изменений.
  /// </summary>
  public static class GgufModelReader
  {
    // Именование шардов llama.cpp: "%s-%05d-of-%05d.gguf" (src/llama.cpp:544-593).
    private static readonly Regex ShardRx = new(
      @"^(?<stem>.+)-(?<no>\d{5})-of-(?<count>\d{5})\.gguf$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Одиночная модель или агрегат по найденным рядом шардам.</summary>
    public static GgufInfo Read(string path) => Read(path, null);

    /// <summary>Агрегат с использованием уже известного списка шардов (из S3-сканера).</summary>
    public static GgufInfo Read(ModelEntry entry)
    {
      IReadOnlyList<string>? shards = entry.IsSplit ? entry.ShardPaths : null;
      return Read(entry.FullPath, shards);
    }

    private static GgufInfo Read(string path, IReadOnlyList<string>? knownShards)
    {
      var primary = GgufInfo.ParseSingle(path);
      // Не split (split.count отсутствует или <= 1) — поведение одиночной модели не меняется.
      if (!primary.HasSplitKeys || primary.SplitCount <= 1)
        return primary;

      string fileName = Path.GetFileName(path);
      string stem = TryParseShard(fileName, out var s, out _, out _) ? s : Path.GetFileNameWithoutExtension(fileName);
      string dir = Path.GetDirectoryName(path) ?? "";

      // Группа: тот же stem в той же папке (регистронезависимо).
      var found = new SortedDictionary<int, string>();
      if (knownShards is { Count: > 0 })
      {
        foreach (var sp in knownShards)
          if (TryParseShard(Path.GetFileName(sp), out var s2, out int no, out _) &&
              string.Equals(s2, stem, StringComparison.OrdinalIgnoreCase))
            found[no] = sp;
      }
      else
      {
        foreach (var f in Directory.EnumerateFiles(dir, "*.gguf"))
          if (TryParseShard(Path.GetFileName(f), out var s2, out int no, out _) &&
              string.Equals(s2, stem, StringComparison.OrdinalIgnoreCase))
            found[no] = f;
      }

      // Открытый файл точно входит в набор.
      if (TryParseShard(fileName, out _, out int primaryNo, out _))
        found[primaryNo] = path;

      return Aggregate(primary, path, stem, dir, found);
    }

    private static GgufInfo Aggregate(GgufInfo primary, string primaryPath, string stem, string dir,
      SortedDictionary<int, string> found)
    {
      int declared = primary.SplitCount;
      int declaredTensors = primary.SplitTensorsCount;

      var notes = new List<string>();
      var missing = new List<string>();
      var tensors = new List<GgufTensorInfo>();
      var seenNames = new HashSet<string>(StringComparer.Ordinal);
      bool mismatch = false;
      long fileSize = 0;
      GgufInfo? shard0 = null;

      for (int no = 1; no <= declared; no++)
      {
        if (!found.TryGetValue(no, out var shardPath))
        {
          missing.Add(Path.Combine(dir, $"{stem}-{no:D5}-of-{declared:D5}.gguf"));
          continue;
        }

        // Открытый файл уже разобран; повторно не читаем.
        GgufInfo shard = SamePath(shardPath, primaryPath) ? primary : GgufInfo.ParseSingle(shardPath);
        fileSize += shard.FileSize;
        if (no == 1) shard0 = shard;

        // split.no — 0-based и должен соответствовать номеру шарда в имени (llama-model-loader.cpp:639-643).
        if (shard.HasSplitKeys && shard.SplitNo >= 0 && shard.SplitNo != no - 1)
        {
          mismatch = true;
          notes.Add($"файл {Path.GetFileName(shardPath)}: split.no = {shard.SplitNo}, ожидалось {no - 1}");
        }

        if (shard.IntegrityNote.Length > 0)
          notes.Add($"{Path.GetFileName(shardPath)}: {shard.IntegrityNote}");

        foreach (var t in shard.Tensors)
        {
          if (!seenNames.Add(t.Name))
          {
            mismatch = true;
            notes.Add($"тензор '{t.Name}' дублируется между шардами");
          }
          tensors.Add(t);
        }
      }

      if (shard0 == null)
        notes.Add("шард 00001 отсутствует — метаданные модели (arch/context/tool-calls) недоступны");

      // Метаданные — только из шарда split.no == 0.
      var agg = new GgufInfo
      {
        HasSplitKeys = true,
        SplitNo = 0,
        SplitCount = declared,
        SplitTensorsCount = declaredTensors,
        FileType = shard0?.FileType ?? -1,
        MissingShardPaths = missing,
        FileSize = fileSize,
        Tensors = tensors,
        Arch = shard0?.Arch ?? "llama",
        BlockCount = shard0?.BlockCount ?? 0,
        ContextLength = shard0?.ContextLength ?? 0,
        KvHeads = shard0?.KvHeads ?? 0,
        HeadDim = shard0?.HeadDim ?? 0,
        HasReasoning = shard0?.HasReasoning ?? false,
        HasChatTemplate = shard0?.HasChatTemplate ?? false,
        ToolSupport = shard0?.ToolSupport ?? ToolSupportKind.Unknown,
        ToolEvidence = shard0?.ToolEvidence ?? ""
      };

      // Классификация и Σ nbytes по объединённому списку.
      GgufInfo.ClassifyTensors(agg, tensors);

      // Контрольное число split.tensors.count.
      if (declaredTensors >= 0 && tensors.Count != declaredTensors)
      {
        notes.Add($"тензоров найдено {tensors.Count}, split.tensors.count = {declaredTensors}");
        // При нехватке шардов это ожидаемо (Incomplete); на полном наборе — порча (Mismatch).
        if (missing.Count == 0) mismatch = true;
      }

      agg.SplitStatus = mismatch
        ? SplitStatus.Mismatch
        : missing.Count > 0 ? SplitStatus.Incomplete : SplitStatus.Complete;
      agg.IntegrityNote = string.Join("; ", notes);
      return agg;
    }

    private static bool SamePath(string a, string b)
    {
      try
      {
        return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
      }
      catch
      {
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
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
  }
}
