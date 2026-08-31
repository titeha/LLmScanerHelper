using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

using LlmScanHelper.Models;
using LlmScanHelper.Models.Settings;
using MvvmUtilites;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Главный MVVM-вьюмодель: состояние панели, сканирование,
  /// сохранение параметров (JSON рядом с exe + по-модельные профили).
  /// Разбивка по файлам:
  ///   MainViewModel.Model.cs        — инфо о модели, загрузка, мультимодальность (mmproj);
  ///   MainViewModel.MtpReasoning.cs — сервер (хост/порт), MTP, reasoning, jinja;
  ///   MainViewModel.Gpu.cs          — GPU layout и опрос устройств;
  ///   MainViewModel.Presets.cs      — пресеты;
  ///   MainViewModel.Catalogs.cs     — корневые каталоги моделей;
  ///   MainViewModel.Persistence.cs  — сохранение/загрузка настроек;
  ///   MainViewModel.Output.cs       — сборка команды, предупреждения, оценка слоёв.
  /// </summary>
  public sealed partial class MainViewModel : ObservableObject
  {
    private readonly SettingsStore _store = new();
    private GgufInfo? _gguf;
    private List<GpuDeviceInfo> _gpus = new();
    private string? _currentPath;          // путь текущей модели (ключ по-модельного профиля)
    private bool _suppressSave;            // массовое применение — без сохранения
    private bool _suppressAliasEdit;       // программная установка алиаса
    private int _loadSeq;                  // защита от гонок при быстрой смене моделей
    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _flashTimer;

    public MainViewModel()
    {
      _store.Load();
      ApplyGlobalFromStore();
      ApplyCatalogsFromStore();

      ScanCommand = new AsyncRelayCommand(ScanAsync);
      RefreshGpusCommand = new AsyncRelayCommand(RefreshGpusAsync);
      BuildCommandCommand = new RelayCommand(BuildOutputs);
      CopyCommandCommand = new RelayCommand(CopyToClipboard);

      PresetV100OnlyCommand = new RelayCommand(() => ApplyBaseAuto(AppDefaults.SafeReserveRtxGiB, PresetDevices.V100Only));
      PresetRtxOnlyCommand = new RelayCommand(ApplyPresetRtxOnly);
      PresetSafeCommand = new RelayCommand(() => ApplyBaseAuto(AppDefaults.SafeReserveRtxGiB, PresetDevices.Combined));
      PresetBalancedCommand = new RelayCommand(() => ApplyBaseAuto(AppDefaults.BalancedReserveRtxGiB, PresetDevices.Combined));
      PresetAggressiveCommand = new RelayCommand(() => ApplyBaseAuto(AppDefaults.AggressiveReserveRtxGiB, PresetDevices.Combined));
      PresetQ8Command = new RelayCommand(ApplyPresetQ8);
      PresetV100Command = new RelayCommand(ApplyPresetV100);

      AddCatalogCommand = new RelayCommand(AddCatalog);
      RemoveCatalogCommand = new RelayCommand(RemoveCatalog);
      EditCatalogCommand = new RelayCommand(EditCatalog);
    }

    /// <summary>Вызывается из MainWindow после загрузки окна.</summary>
    public async Task InitializeAsync()
    {
      await ScanAsync();
      // GPU НЕ опрашиваем автоматически (как в LINQPad-версии) — кнопка «Обновить GPU».
    }

    // ==================== Модели ====================

    public ObservableCollection<ModelEntry> Models { get; } = new();

    private ModelEntry? _selectedModel;
    public ModelEntry? SelectedModel
    {
      get => _selectedModel;
      set
      {
        if (ReferenceEquals(_selectedModel, value))
          return;
        FlushPendingSave();           // правки старой модели — в её профиль
        if (!Set(ref _selectedModel, value))
          return;
        _ = LoadModelAsync(value);
      }
    }

    private string _statusText = "Модель не загружена";
    public string StatusText
    {
      get => _statusText;
      set => Set(ref _statusText, value);
    }

    public string[] KvOptions { get; } = { "f16", "q8_0", "q4_0" };
    public string[] FlashOptions { get; } = { "auto", "on", "off" };
    public string[] ModeOptions { get; } = { "AUTO — llama.cpp --fit", "MANUAL — экспертный" };
    public string[] SplitModeOptions { get; } = { "layer", "row", "tensor", "none" };

    // ==================== Контекст / KV / Attention ====================

    private int _maxContext = 131072;
    public int MaxContext
    {
      get => _maxContext;
      private set { if (Set(ref _maxContext, value)) { OnPropertyChanged(nameof(ContextTick)); OnPropertyChanged(nameof(ContextLarge)); } }
    }

    public int ContextTick => Math.Max(4096, MaxContext / 30);
    public int ContextLarge => Math.Max(4096, MaxContext / 10);

    private int _context = AppDefaults.DefaultContext;
    public int Context
    {
      get => _context;
      set { value = Math.Clamp(value, 1024, MaxContext); if (Set(ref _context, value)) SaveSoon(); }
    }

    private string _kvK = "q8_0";
    public string KvK { get => _kvK; set { if (Set(ref _kvK, value)) SaveSoon(); } }

    private string _kvV = "q8_0";
    public string KvV { get => _kvV; set { if (Set(ref _kvV, value)) SaveSoon(); } }

    private string _flash = "auto";
    public string Flash { get => _flash; set { if (Set(ref _flash, value)) SaveSoon(); } }

    // ==================== Производительность / агент ====================

    private int _batch = AppDefaults.DefaultBatch;
    public int Batch { get => _batch; set { if (Set(ref _batch, value)) SaveSoon(); } }

    private int _ubatch = AppDefaults.DefaultUBatch;
    public int UBatch { get => _ubatch; set { if (Set(ref _ubatch, value)) SaveSoon(); } }

    private int _slots = AppDefaults.DefaultSlots;
    public int Slots { get => _slots; set { if (Set(ref _slots, value)) SaveSoon(); } }

    private int _threads = 0;
    public int Threads { get => _threads; set { if (Set(ref _threads, value)) SaveSoon(); } }

    private int _threadsBatch = 0;
    public int ThreadsBatch { get => _threadsBatch; set { if (Set(ref _threadsBatch, value)) SaveSoon(); } }

    private bool _promptCache = true;
    public bool PromptCache { get => _promptCache; set { if (Set(ref _promptCache, value)) SaveSoon(); } }

    private int _cacheReuse = AppDefaults.DefaultCacheReuse;
    public int CacheReuse { get => _cacheReuse; set { if (Set(ref _cacheReuse, value)) SaveSoon(); } }

    private int _ssePing = AppDefaults.DefaultSsePing;
    public int SsePing { get => _ssePing; set { if (Set(ref _ssePing, value)) SaveSoon(); } }

    private int _timeout = AppDefaults.DefaultTimeout;
    public int Timeout { get => _timeout; set { if (Set(ref _timeout, value)) SaveSoon(); } }

    private bool _perf = true;
    public bool Perf { get => _perf; set { if (Set(ref _perf, value)) SaveSoon(); } }

    // ==================== Sampling (параметры разработчика) ====================

    private bool _samplingEnabled = AppDefaults.DefaultSamplingEnabled;
    public bool SamplingEnabled { get => _samplingEnabled; set { if (Set(ref _samplingEnabled, value)) SaveSoon(); } }

    private double _temp = AppDefaults.DefaultTemp;
    public double Temp { get => _temp; set { if (Set(ref _temp, value)) SaveSoon(); } }

    private int _topK = AppDefaults.DefaultTopK;
    public int TopK { get => _topK; set { if (Set(ref _topK, value)) SaveSoon(); } }

    private double _topP = AppDefaults.DefaultTopP;
    public double TopP { get => _topP; set { if (Set(ref _topP, value)) SaveSoon(); } }

    private double _minP = AppDefaults.DefaultMinP;
    public double MinP { get => _minP; set { if (Set(ref _minP, value)) SaveSoon(); } }

    private double _repeatPenalty = AppDefaults.DefaultRepeatPenalty;
    public double RepeatPenalty { get => _repeatPenalty; set { if (Set(ref _repeatPenalty, value)) SaveSoon(); } }

    private int _repeatLastN = AppDefaults.DefaultRepeatLastN;
    public int RepeatLastN { get => _repeatLastN; set { if (Set(ref _repeatLastN, value)) SaveSoon(); } }

    private double _presencePenalty = AppDefaults.DefaultPresencePenalty;
    public double PresencePenalty { get => _presencePenalty; set { if (Set(ref _presencePenalty, value)) SaveSoon(); } }

    private double _frequencyPenalty = AppDefaults.DefaultFrequencyPenalty;
    public double FrequencyPenalty { get => _frequencyPenalty; set { if (Set(ref _frequencyPenalty, value)) SaveSoon(); } }

    private int _seed = AppDefaults.DefaultSeed;
    public int Seed { get => _seed; set { if (Set(ref _seed, value)) SaveSoon(); } }

    // ==================== Выходы ====================

    private string _launchCommand = "(выбери модель и нажми «Собрать команду»)";
    public string LaunchCommand { get => _launchCommand; private set => Set(ref _launchCommand, value); }

    public ObservableCollection<string> Warnings { get; } = new();

    private string _copyStatusText = "";
    public string CopyStatusText { get => _copyStatusText; private set => Set(ref _copyStatusText, value); }

    private string _layerEstimateText = "Оценка: модель не выбрана.";
    public string LayerEstimateText { get => _layerEstimateText; private set => Set(ref _layerEstimateText, value); }

    private int _selectedTabIndex;
    public int SelectedTabIndex { get => _selectedTabIndex; set { if (Set(ref _selectedTabIndex, value)) SaveSoon(); } }

    // ==================== Алиас ====================

    private string _aliasText = AppDefaults.DefaultAlias;
    public string AliasText
    {
      get => _aliasText;
      set
      {
        if (!Set(ref _aliasText, value))
          return;
        if (_suppressAliasEdit)
          return;
        // Ручная правка: фиксируем в профиле модели (регистр сохраняется как ввели)
        if (_currentPath != null)
        {
          var ms = _store.GetOrCreateModel(_currentPath);
          ms.Alias = value;
          ms.AliasEdited = true;
        }
        SaveSoon();
      }
    }

    // ==================== Команды ====================

    public ICommand ScanCommand { get; }
    public ICommand RefreshGpusCommand { get; }
    public ICommand BuildCommandCommand { get; }
    public ICommand CopyCommandCommand { get; }
    public ICommand PresetV100OnlyCommand { get; }
    public ICommand PresetRtxOnlyCommand { get; }
    public ICommand PresetSafeCommand { get; }
    public ICommand PresetBalancedCommand { get; }
    public ICommand PresetAggressiveCommand { get; }
    public ICommand PresetQ8Command { get; }
    public ICommand PresetV100Command { get; }
    public ICommand AddCatalogCommand { get; }
    public ICommand RemoveCatalogCommand { get; }
    public ICommand EditCatalogCommand { get; }

    // ==================== Сканирование ====================

    private async Task ScanAsync()
    {
      StatusText = "Сканирование моделей...";
      var res = await Task.Run(() => GgufScannerService.Scan(SelectedCatalog));

      if (res.Error != null)
      {
        StatusText = "Не прочитать папку моделей: " + res.Error;
        return;
      }

      Models.Clear();
      foreach (var m in res.Models)
        Models.Add(m);

      if (Models.Count == 0)
      {
        StatusText = "GGUF не найдены в " + SelectedCatalog;
        return;
      }

      SelectedModel = Models.FirstOrDefault(x =>
        x.FullPath.Equals(_store.Settings.LastModelPath, StringComparison.OrdinalIgnoreCase)) ?? Models[0]; // триггерит LoadModelAsync
    }

    private const double MiB = 1024.0 * 1024.0;
    private const double GiB = 1024.0 * 1024.0 * 1024.0;
  }
}
