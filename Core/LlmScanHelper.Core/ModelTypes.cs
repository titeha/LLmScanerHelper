namespace LlmScanHelper.Models
{
  /// <summary>Роль GGUF-файла: основная модель или draft-модель (для `--model-draft`).</summary>
  public enum ModelKind
  {
    Main = 0,
    Draft = 1
  }

  /// <summary>
  /// Модель в списке (найденный .gguf, не mmproj). Шарды одного набора — одна запись:
  /// `FullPath`/`FileName` указывают на шард `00001` и stem соответственно.
  /// </summary>
  public sealed class ModelEntry
  {
    public string FullPath { get; init; } = "";   // для split — шард 00001 (или минимальный найденный)
    public string FileName { get; init; } = "";   // для split — stem без суффикса шарда
    public string Publisher { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public List<string> LocalMmproj { get; init; } = new(); // пути-представители mmproj рядом с моделью

    // S3: группировка шардов (работает только по именам и размерам, содержимое не читается)
    public bool IsSplit { get; init; }
    public IReadOnlyList<string> ShardPaths { get; init; } = Array.Empty<string>(); // упорядочены по номеру
    public int ShardCount { get; init; } = 1;                  // из имени файла, не из метаданных
    public IReadOnlyList<int> MissingShards { get; init; } = Array.Empty<int>(); // 1-based номера
    public long TotalSizeBytes { get; init; }                  // сумма размеров найденных файлов
    public ModelKind Kind { get; init; } = ModelKind.Main;
    // Полный список mmproj-файлов рядом (включая все шарды проекторов); LocalMmproj — по одному на набор.
    public List<string> MmprojShardPaths { get; init; } = new();

    public override string ToString() => DisplayName;
  }

  /// <summary>mmproj-файл (мультимодальный проектор).</summary>
  public sealed class MmprojEntry
  {
    public string FullPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public long FileSize { get; init; }

    public override string ToString() => DisplayName;
  }

  /// <summary>Информация об одном GPU из llama-server --list-devices.</summary>
  public sealed class GpuDeviceInfo
  {
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int TotalMiB { get; set; }
    public int FreeMiB { get; set; }

    public override string ToString() => $"{Id}: {Name} ({FreeMiB}/{TotalMiB} MiB free)";

    public bool IsV100() => Name.Contains("V100", StringComparison.OrdinalIgnoreCase);
    public bool IsDesktopRtx() => Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                    Name.Contains("RTX", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Результат опроса llama-server --list-devices.</summary>
  public sealed class GpuQueryResult
  {
    public bool Ok { get; init; }
    public string Message { get; init; } = "";   // сообщение об ошибке / нераспарсенный вывод
    public List<GpuDeviceInfo> Devices { get; init; } = new();
  }
}
