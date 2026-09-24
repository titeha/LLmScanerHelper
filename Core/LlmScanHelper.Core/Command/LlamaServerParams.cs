namespace LlmScanHelper.Models.Command;

/// <summary>
/// Все параметры для сборки строки llama-server. Чистый перенос данных:
/// имена свойств совпадают со свойствами MainViewModel, значения по умолчанию —
/// с дефолтами VM (единственный источник дефолтов — AppDefaults).
/// </summary>
public sealed record LlamaServerParams
{
  // Сервер
  public string Host { get; init; } = AppDefaults.DefaultHost;
  public int Port { get; init; } = AppDefaults.DefaultPort;

  // Устройства / режим раскладки
  public string DevicesText { get; init; } = AppDefaults.DefaultDevices;
  public int ModeIndex { get; init; } = 0;                 // 0 = AUTO (--fit on), 1 = MANUAL
  public string SplitMode { get; init; } = "layer";
  public int ManualNgl { get; init; } = 0;
  public int Split0 { get; init; } = 3;
  public int Split1 { get; init; } = 1;
  public double ReserveV100GiB { get; init; } = AppDefaults.SafeReserveV100GiB;
  public double ReserveRtxGiB { get; init; } = AppDefaults.SafeReserveRtxGiB;

  // Модель / алиас / mmproj
  public string AliasText { get; init; } = AppDefaults.DefaultAlias;
  public MmprojEntry? SelectedMmproj { get; init; }
  public string SelectedModelDisplayName { get; init; } = "";   // SelectedModel?.DisplayName ?? ""

  // Контекст / KV / батчи
  public int Context { get; init; } = AppDefaults.DefaultContext;
  public string KvK { get; init; } = "q8_0";
  public string KvV { get; init; } = "q8_0";
  public int Batch { get; init; } = AppDefaults.DefaultBatch;
  public int UBatch { get; init; } = AppDefaults.DefaultUBatch;
  public int Slots { get; init; } = AppDefaults.DefaultSlots;

  // Потоки / производительность
  public int Threads { get; init; } = 0;
  public int ThreadsBatch { get; init; } = 0;
  public string Flash { get; init; } = "auto";
  public bool Perf { get; init; } = true;

  // Поведение сервера
  public int Timeout { get; init; } = AppDefaults.DefaultTimeout;
  public int SsePing { get; init; } = AppDefaults.DefaultSsePing;
  public bool PromptCache { get; init; } = true;
  public int CacheReuse { get; init; } = AppDefaults.DefaultCacheReuse;

  // Галочки (jinja / mmproj)
  public bool JinjaChecked { get; init; }
  public bool MmprojAvailable { get; init; }
  public bool MmprojChecked { get; init; }

  // Sampling — параметры разработчика (дефолты сервера)
  public bool SamplingEnabled { get; init; } = AppDefaults.DefaultSamplingEnabled;
  public double Temp { get; init; } = AppDefaults.DefaultTemp;
  public int TopK { get; init; } = AppDefaults.DefaultTopK;
  public double TopP { get; init; } = AppDefaults.DefaultTopP;
  public double MinP { get; init; } = AppDefaults.DefaultMinP;
  public double RepeatPenalty { get; init; } = AppDefaults.DefaultRepeatPenalty;
  public int RepeatLastN { get; init; } = AppDefaults.DefaultRepeatLastN;
  public double PresencePenalty { get; init; } = AppDefaults.DefaultPresencePenalty;
  public double FrequencyPenalty { get; init; } = AppDefaults.DefaultFrequencyPenalty;
  public int Seed { get; init; } = AppDefaults.DefaultSeed;

  // MTP (draft-mtp)
  public bool MtpAvailable { get; init; }
  public bool MtpChecked { get; init; }
  public int DraftMax { get; init; } = 3;
  public int DraftMin { get; init; } = 0;
  public double DraftP { get; init; } = 0;
  public string DraftK { get; init; } = "q8_0";
  public string DraftV { get; init; } = "q8_0";

  // Reasoning
  public string ReasoningMode { get; init; } = AppDefaults.DefaultReasoningMode;
  public int ReasonBudget { get; init; } = 4096;
  public string ReasonBudgetMessage { get; init; } = AppDefaults.DefaultReasonBudgetMessage;

  // GPU (для fit-targets и предупреждений)
  public IReadOnlyList<GpuDeviceInfo> Gpus { get; init; } = Array.Empty<GpuDeviceInfo>();
}
