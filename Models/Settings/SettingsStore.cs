using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace LlmScanHelper.Models.Settings
{
  /// <summary>Глобальные (железо/серверные) параметры.</summary>
  public sealed class GlobalParams
  {
    public string Host { get; set; } = AppDefaults.DefaultHost;
    public int Port { get; set; } = AppDefaults.DefaultPort;
    public string Devices { get; set; } = AppDefaults.DefaultDevices;
    public double ReserveV100GiB { get; set; } = AppDefaults.SafeReserveV100GiB;
    public double ReserveRtxGiB { get; set; } = AppDefaults.SafeReserveRtxGiB;

    public int ModeIndex { get; set; } = 0;            // 0 = AUTO, 1 = MANUAL
    public string SplitMode { get; set; } = "layer";
    public int ManualNgl { get; set; } = 0;
    public int Split0 { get; set; } = 3;
    public int Split1 { get; set; } = 1;

    public int Batch { get; set; } = AppDefaults.DefaultBatch;
    public int UBatch { get; set; } = AppDefaults.DefaultUBatch;
    public int Slots { get; set; } = AppDefaults.DefaultSlots;
    public int Threads { get; set; } = 0;
    public int ThreadsBatch { get; set; } = 0;

    public bool PromptCache { get; set; } = true;
    public int CacheReuse { get; set; } = AppDefaults.DefaultCacheReuse;
    public int SsePing { get; set; } = AppDefaults.DefaultSsePing;
    public int Timeout { get; set; } = AppDefaults.DefaultTimeout;
    public bool Perf { get; set; } = true;

    // Sampling (параметры разработчика)
    public bool SamplingEnabled { get; set; } = AppDefaults.DefaultSamplingEnabled;
    public double Temp { get; set; } = AppDefaults.DefaultTemp;
    public int TopK { get; set; } = AppDefaults.DefaultTopK;
    public double TopP { get; set; } = AppDefaults.DefaultTopP;
    public double MinP { get; set; } = AppDefaults.DefaultMinP;
    public double RepeatPenalty { get; set; } = AppDefaults.DefaultRepeatPenalty;
    public int RepeatLastN { get; set; } = AppDefaults.DefaultRepeatLastN;
    public double PresencePenalty { get; set; } = AppDefaults.DefaultPresencePenalty;
    public double FrequencyPenalty { get; set; } = AppDefaults.DefaultFrequencyPenalty;
    public int Seed { get; set; } = AppDefaults.DefaultSeed;
  }

  /// <summary>Переопределения для конкретной модели (ключ — полный путь файла).</summary>
  public sealed class ModelSettings
  {
    public string Alias { get; set; } = "";
    public bool AliasEdited { get; set; }             // алиас правился вручную — не перегенерировать
    public int Context { get; set; } = 0;             // 0 = использовать дефолт при загрузке
    public string KvK { get; set; } = "q8_0";
    public string KvV { get; set; } = "q8_0";
    public string Flash { get; set; } = "auto";

    public bool MtpChecked { get; set; }
    public int DraftMax { get; set; } = 3;
    public int DraftMin { get; set; } = 0;
    public double DraftP { get; set; } = 0;
    public string DraftK { get; set; } = "q8_0";
    public string DraftV { get; set; } = "q8_0";

    public bool ReasoningChecked { get; set; } = true;
    public int ReasonBudget { get; set; } = 4096;
    public string ReasonBudgetMessage { get; set; } = "";   // пусто = дефолт из AppDefaults

    public bool UseJinja { get; set; }                // --jinja: родной chat-шаблон GGUF (tools/agents)
    public bool JinjaEdited { get; set; }             // юзер переключал вручную — авто-детект не переопределяет

    public string MmprojPath { get; set; } = "";      // выбранный проектор
    public bool MmprojEnabled { get; set; }
  }

  /// <summary>Корневой объект settings.json.</summary>
  public sealed class AppSettings
  {
    public string SettingsVersion { get; set; } = "4.0";
    // Корневые каталоги с моделями (ранее одна строка ModelsRoot).
    // Пустой по умолчанию; при старте без settings.json вьюмодель подставит дефолт.
    public List<string> Catalogs { get; set; } = new();
    public int SelectedCatalogIndex { get; set; } = 0;
    public string LastModelPath { get; set; } = "";
    public int SelectedTabIndex { get; set; } = 0;    // 0 = Панель, 1 = Памятка, 2 = Настройки

    // Мостик для миграции из старых файлов (до нескольких каталогов), где корень
    // моделей хранился как строка ModelsRoot. После миграции обнуляется.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ModelsRoot { get; set; }

    public GlobalParams Global { get; set; } = new();

    public Dictionary<string, ModelSettings> PerModel { get; set; } =
      new(StringComparer.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Хранение параметров: JSON рядом с exe (portable-режим).
  /// Атомарная запись через tmp-файл; битый файл уводится в .bad.
  /// </summary>
  public sealed class SettingsStore
  {
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
      WriteIndented = true,
      DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
      Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // читаемая кириллица в файле
    };

    public AppSettings Settings { get; private set; } = new();

    public string FilePath { get; }

    public SettingsStore()
    {
      // JSON рядом с exe — переносимый режим
      string dir = AppContext.BaseDirectory;
      FilePath = Path.Combine(dir, "settings.json");
    }

    public void Load()
    {
      try
      {
        if (!File.Exists(FilePath)) return;
        var json = File.ReadAllText(FilePath);
        var parsed = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
        if (parsed != null)
        {
          Settings = parsed;
          // словарь без компаратора при десериализации — восстановим
          Settings.PerModel = new Dictionary<string, ModelSettings>(
            Settings.PerModel, StringComparer.OrdinalIgnoreCase);

          // Миграция: старые файлы хранили ModelsRoot как строку. Новый список
          // каталогов пуст при десериализации старого файла, подтягиваем ModelsRoot
          // как единственный каталог, а мостиковое поле обнуляем.
          if (Settings.Catalogs.Count == 0)
          {
            Settings.Catalogs = new List<string>
            {
              string.IsNullOrEmpty(Settings.ModelsRoot) ? AppDefaults.ModelsRoot : Settings.ModelsRoot
            };
            Settings.SelectedCatalogIndex = 0;
            Settings.ModelsRoot = null;
          }
        }
      }
      catch (Exception)
      {
        try
        {
          string bad = FilePath + ".bad";
          if (File.Exists(bad)) File.Delete(bad);
          File.Move(FilePath, bad);
        }
        catch { /* не критично */ }
        Settings = new AppSettings();
      }
    }

    /// <summary>Атомарная запись на диск. Вызывается из любого потока.</summary>
    public void Save(AppSettings snapshot)
    {
      try
      {
        string dir = Path.GetDirectoryName(FilePath) ?? ".";
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

        string tmp = FilePath + ".tmp";
        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        File.WriteAllText(tmp, json);

        // Перезапись атомарно: tmp -> settings.json
        if (File.Exists(FilePath))
          File.Replace(tmp, FilePath, null);
        else
          File.Move(tmp, FilePath);
      }
      catch
      {
        // Ошибки записи не роняем приложение: попробуем в следующий раз.
      }
    }

    public ModelSettings GetOrCreateModel(string fullPath)
    {
      if (!Settings.PerModel.TryGetValue(fullPath, out var ms))
      {
        ms = new ModelSettings();
        Settings.PerModel[fullPath] = ms;
      }
      return ms;
    }
  }
}
