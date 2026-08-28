namespace LlmScanHelper.Models
{
  /// <summary>Модель в списке (найденный .gguf, не mmproj).</summary>
  public sealed class ModelEntry
  {
    public string FullPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Publisher { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public List<string> LocalMmproj { get; init; } = new(); // полные пути mmproj рядом с моделью

    public override string ToString() => DisplayName;
  }

  /// <summary>mmproj-файл (мультимодальный проектор).</summary>
  public sealed class MmprojEntry
  {
    public string FullPath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public long FileSize { get; init; }
    public bool IsLocal { get; init; } // найден в папке самой модели

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

    public bool IsV100() => Name.IndexOf("V100", StringComparison.OrdinalIgnoreCase) >= 0;
    public bool IsDesktopRtx() => Name.IndexOf("GeForce", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    Name.IndexOf("RTX", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  /// <summary>Результат опроса llama-server --list-devices.</summary>
  public sealed class GpuQueryResult
  {
    public bool Ok { get; init; }
    public string Message { get; init; } = "";   // сообщение об ошибке / нераспарсенный вывод
    public List<GpuDeviceInfo> Devices { get; init; } = new();
  }
}
