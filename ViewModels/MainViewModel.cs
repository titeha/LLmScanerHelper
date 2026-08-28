using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using LlmScanHelper.Models;
using LlmScanHelper.Models.Settings;
using LlmScanHelper.Texts;

namespace LlmScanHelper.ViewModels
{
    /// <summary>
    /// Главный MVVM-вьюмодель: состояние панели, сканирование, GPU, пресеты,
    /// сохранение параметров (JSON рядом с exe + по-модельные профили).
    /// Сборка команды/предупреждений — в MainViewModel.Output.cs.
    /// </summary>
    public sealed partial class MainViewModel : ObservableObject
    {
        private readonly SettingsStore _store = new();
        private GgufInfo? _gguf;
        private List<GpuDeviceInfo> _gpus = new();
        private List<MmprojEntry> _allMmproj = new();
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

            ScanCommand = new AsyncRelayCommand(ScanAsync);
            RefreshGpusCommand = new AsyncRelayCommand(RefreshGpusAsync);
            BuildCommandCommand = new RelayCommand(BuildOutputs);
            CopyCommandCommand = new RelayCommand(CopyToClipboard);

            PresetV100OnlyCommand = new RelayCommand(() => ApplyBaseAuto(BenchDefaults.SafeReserveRtxGiB, v100Only: true));
            PresetSafeCommand = new RelayCommand(() => ApplyBaseAuto(BenchDefaults.SafeReserveRtxGiB, v100Only: false));
            PresetBalancedCommand = new RelayCommand(() => ApplyBaseAuto(BenchDefaults.BalancedReserveRtxGiB, v100Only: false));
            PresetAggressiveCommand = new RelayCommand(() => ApplyBaseAuto(BenchDefaults.AggressiveReserveRtxGiB, v100Only: false));
            PresetQ8Command = new RelayCommand(ApplyPresetQ8);
            PresetV100Command = new RelayCommand(ApplyPresetV100);
        }

        /// <summary>Вызывается из MainWindow после загрузки окна.</summary>
        public async Task InitializeAsync()
        {
            await ScanAsync();
            // GPU НЕ опрашиваем автоматически (как в LINQPad-версии) — кнопка «Обновить GPU».
        }

        // ==================== Модели / каталог ====================

        private string _modelsRoot = BenchDefaults.ModelsRoot;
        public string ModelsRoot
        {
            get => _modelsRoot;
            set { if (SetProperty(ref _modelsRoot, value)) SaveSoon(); }
        }

        public ObservableCollection<ModelEntry> Models { get; } = new();

        private ModelEntry? _selectedModel;
        public ModelEntry? SelectedModel
        {
            get => _selectedModel;
            set
            {
                if (ReferenceEquals(_selectedModel, value)) return;
                FlushPendingSave();           // правки старой модели — в её профиль
                if (!SetProperty(ref _selectedModel, value)) return;
                _ = LoadModelAsync(value);
            }
        }

        private string _statusText = "Модель не загружена";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public string[] KvOptions { get; } = { "f16", "q8_0", "q4_0" };
        public string[] FlashOptions { get; } = { "auto", "on", "off" };
        public string[] ModeOptions { get; } = { "AUTO — llama.cpp --fit", "MANUAL — экспертный" };
        public string[] SplitModeOptions { get; } = { "layer", "row", "tensor", "none" };

        // ==================== Информация о модели ====================

        private string _infoArch = "-";
        public string InfoArch { get => _infoArch; private set => SetProperty(ref _infoArch, value); }

        private string _infoBlocks = "-";
        public string InfoBlocks { get => _infoBlocks; private set => SetProperty(ref _infoBlocks, value); }

        private string _infoMaxCtx = "-";
        public string InfoMaxCtx { get => _infoMaxCtx; private set => SetProperty(ref _infoMaxCtx, value); }

        private string _infoFileSize = "-";
        public string InfoFileSize { get => _infoFileSize; private set => SetProperty(ref _infoFileSize, value); }

        private string _infoMtpSize = "-";
        public string InfoMtpSize { get => _infoMtpSize; private set => SetProperty(ref _infoMtpSize, value); }

        private string _infoQuant = "-";
        public string InfoQuant { get => _infoQuant; private set => SetProperty(ref _infoQuant, value); }

        private string _infoTools = "-";
        public string InfoTools { get => _infoTools; private set => SetProperty(ref _infoTools, value); }

        // ==================== Контекст / KV / Attention ====================

        private int _maxContext = 131072;
        public int MaxContext
        {
            get => _maxContext;
            private set { if (SetProperty(ref _maxContext, value)) { OnPropertyChanged(nameof(ContextTick)); OnPropertyChanged(nameof(ContextLarge)); } }
        }

        public int ContextTick => Math.Max(4096, MaxContext / 30);
        public int ContextLarge => Math.Max(4096, MaxContext / 10);

        private int _context = BenchDefaults.DefaultContext;
        public int Context
        {
            get => _context;
            set { value = Math.Clamp(value, 1024, MaxContext); if (SetProperty(ref _context, value)) SaveSoon(); }
        }

        private string _kvK = "q8_0";
        public string KvK { get => _kvK; set { if (SetProperty(ref _kvK, value)) SaveSoon(); } }

        private string _kvV = "q8_0";
        public string KvV { get => _kvV; set { if (SetProperty(ref _kvV, value)) SaveSoon(); } }

        private string _flash = "auto";
        public string Flash { get => _flash; set { if (SetProperty(ref _flash, value)) SaveSoon(); } }

        // ==================== GPU layout ====================

        private int _modeIndex = 0;
        public int ModeIndex
        {
            get => _modeIndex;
            set
            {
                if (SetProperty(ref _modeIndex, value))
                {
                    OnPropertyChanged(nameof(AutoModeActive));
                    OnPropertyChanged(nameof(ManualModeActive));
                    UpdateFitTargets();
                    SaveSoon();
                }
            }
        }

        /// <summary>AUTO-режим (--fit on) — резервы активны, MANUAL-поля выключены.</summary>
        public bool AutoModeActive => ModeIndex == 0;

        /// <summary>MANUAL-режим — активны -ngl/--tensor-split/split-mode.</summary>
        public bool ManualModeActive => ModeIndex != 0;

        private string _devicesText = BenchDefaults.DefaultDevices;
        public string DevicesText
        {
            get => _devicesText;
            set { if (SetProperty(ref _devicesText, value)) { UpdateFitTargets(); SaveSoon(); } }
        }

        private string _splitMode = "layer";
        public string SplitMode { get => _splitMode; set { if (SetProperty(ref _splitMode, value)) SaveSoon(); } }

        private double _reserveV100GiB = BenchDefaults.SafeReserveV100GiB;
        public double ReserveV100GiB
        {
            get => _reserveV100GiB;
            set { if (SetProperty(ref _reserveV100GiB, value)) { UpdateFitTargets(); SaveSoon(); } }
        }

        private double _reserveRtxGiB = BenchDefaults.SafeReserveRtxGiB;
        public double ReserveRtxGiB
        {
            get => _reserveRtxGiB;
            set
            {
                value = Math.Max(value, BenchDefaults.MinDesktopReserveGiB); // ниже 2 GiB UI не предлагает
                if (SetProperty(ref _reserveRtxGiB, value)) { UpdateFitTargets(); SaveSoon(); }
            }
        }

        private string _fitTargetsText = "fit-target: -";
        public string FitTargetsText { get => _fitTargetsText; private set => SetProperty(ref _fitTargetsText, value); }

        private int _manualNglMax = 512;
        public int ManualNglMax
        {
            get => _manualNglMax;
            private set { if (SetProperty(ref _manualNglMax, value)) OnPropertyChanged(nameof(ManualNgl)); }
        }

        private int _manualNgl = 0;
        public int ManualNgl
        {
            get => _manualNgl;
            set { value = Math.Clamp(value, 0, ManualNglMax); if (SetProperty(ref _manualNgl, value)) SaveSoon(); }
        }

        private int _split0 = 3;
        public int Split0 { get => _split0; set { if (SetProperty(ref _split0, value)) SaveSoon(); } }

        private int _split1 = 1;
        public int Split1 { get => _split1; set { if (SetProperty(ref _split1, value)) SaveSoon(); } }

        // ==================== GPU ====================

        private string _gpuSummaryText = "GPU: не опрошены";
        public string GpuSummaryText { get => _gpuSummaryText; private set => SetProperty(ref _gpuSummaryText, value); }

        // ==================== Производительность / агент ====================

        private int _batch = BenchDefaults.DefaultBatch;
        public int Batch { get => _batch; set { if (SetProperty(ref _batch, value)) SaveSoon(); } }

        private int _ubatch = BenchDefaults.DefaultUBatch;
        public int UBatch { get => _ubatch; set { if (SetProperty(ref _ubatch, value)) SaveSoon(); } }

        private int _slots = BenchDefaults.DefaultSlots;
        public int Slots { get => _slots; set { if (SetProperty(ref _slots, value)) SaveSoon(); } }

        private int _threads = 0;
        public int Threads { get => _threads; set { if (SetProperty(ref _threads, value)) SaveSoon(); } }

        private int _threadsBatch = 0;
        public int ThreadsBatch { get => _threadsBatch; set { if (SetProperty(ref _threadsBatch, value)) SaveSoon(); } }

        private bool _promptCache = true;
        public bool PromptCache { get => _promptCache; set { if (SetProperty(ref _promptCache, value)) SaveSoon(); } }

        private int _cacheReuse = BenchDefaults.DefaultCacheReuse;
        public int CacheReuse { get => _cacheReuse; set { if (SetProperty(ref _cacheReuse, value)) SaveSoon(); } }

        private int _ssePing = BenchDefaults.DefaultSsePing;
        public int SsePing { get => _ssePing; set { if (SetProperty(ref _ssePing, value)) SaveSoon(); } }

        private int _timeout = BenchDefaults.DefaultTimeout;
        public int Timeout { get => _timeout; set { if (SetProperty(ref _timeout, value)) SaveSoon(); } }

        private bool _perf = true;
        public bool Perf { get => _perf; set { if (SetProperty(ref _perf, value)) SaveSoon(); } }

        // ==================== MTP / reasoning / server ====================

        private string _host = BenchDefaults.DefaultHost;
        public string Host { get => _host; set { if (SetProperty(ref _host, value)) SaveSoon(); } }

        private int _port = BenchDefaults.DefaultPort;
        public int Port { get => _port; set { if (SetProperty(ref _port, value)) SaveSoon(); } }

        private bool _mtpAvailable;
        public bool MtpAvailable
        {
            get => _mtpAvailable;
            private set
            {
                if (SetProperty(ref _mtpAvailable, value))
                {
                    OnPropertyChanged(nameof(MtpLockReason));
                    OnPropertyChanged(nameof(MtpToolTip));
                    OnPropertyChanged(nameof(IsMtpControlsEnabled));
                }
            }
        }

        private bool _mtpChecked;
        public bool MtpChecked
        {
            get => _mtpChecked;
            set
            {
                if (SetProperty(ref _mtpChecked, value))
                {
                    OnPropertyChanged(nameof(IsMtpControlsEnabled));
                    SyncDraftRange();
                    SaveSoon();
                }
            }
        }

        /// <summary>Поля draft-группы активны только при включённом и доступном MTP.</summary>
        public bool IsMtpControlsEnabled => MtpAvailable && MtpChecked;

        /// <summary>Подсказка, почему MTP недоступен (пустая — доступен).</summary>
        public string MtpLockReason =>
            !MtpAvailable
                ? (_gguf == null ? "Модель не загружена"
                    : !_gguf.HasMtp ? "В модели нет MTP/nextn-тензоров"
                    : "Издатель заявляет: MTP недоступен для Q8-квантов — переключатель отключён")
                : "";

        /// <summary>Полный tooltip для MTP: документация + причина блокировки, если есть.</summary>
        public string MtpToolTip =>
            MtpAvailable ? Texts.ToolTips.Mtp : Texts.ToolTips.Mtp + "\n\nСейчас недоступно: " + MtpLockReason;

        private int _draftMax = 3;
        public int DraftMax
        {
            get => _draftMax;
            set
            {
                value = Math.Clamp(value, 1, 16);
                if (SetProperty(ref _draftMax, value))
                {
                    if (DraftMin > _draftMax) DraftMin = _draftMax; // min никогда не > max
                    OnPropertyChanged(nameof(DraftMin));
                    SaveSoon();
                }
            }
        }

        private int _draftMin = 0;
        public int DraftMin
        {
            get => _draftMin;
            set { value = Math.Clamp(value, 0, DraftMax); if (SetProperty(ref _draftMin, value)) SaveSoon(); }
        }

        private double _draftP = 0;
        public double DraftP { get => _draftP; set { if (SetProperty(ref _draftP, value)) SaveSoon(); } }

        private string _draftK = "q8_0";
        public string DraftK { get => _draftK; set { if (SetProperty(ref _draftK, value)) SaveSoon(); } }

        private string _draftV = "q8_0";
        public string DraftV { get => _draftV; set { if (SetProperty(ref _draftV, value)) SaveSoon(); } }

        private bool _reasoningAvailable;
        public bool ReasoningAvailable { get => _reasoningAvailable; private set => SetProperty(ref _reasoningAvailable, value); }

        private bool _reasoningChecked;
        public bool ReasoningChecked { get => _reasoningChecked; set { if (SetProperty(ref _reasoningChecked, value)) SaveSoon(); } }

        private int _reasonBudget = 4096;
        public int ReasonBudget
        {
            get => _reasonBudget;
            set { value = Math.Clamp(value, 0, 1_000_000); if (SetProperty(ref _reasonBudget, value)) SaveSoon(); }
        }

        // --jinja: родной chat-шаблон GGUF (нужен для tool-calls/функций в OpenAI API).
        // По умолчанию включается, если сканер нашёл в шаблоне обработку tools;
        // ручное переключение фиксируется (JinjaEdited) и авто-детектом больше не трогается.
        private bool _suppressJinjaEdit;                  // программная установка флага
        private bool _jinjaChecked;
        public bool JinjaChecked
        {
            get => _jinjaChecked;
            set
            {
                if (SetProperty(ref _jinjaChecked, value))
                {
                    if (!_suppressJinjaEdit && _currentPath != null)
                    {
                        var ms = _store.GetOrCreateModel(_currentPath);
                        ms.UseJinja = value;
                        ms.JinjaEdited = true;
                    }
                    SaveSoon();
                }
            }
        }

        // ==================== Мультимодальность ====================

        public ObservableCollection<MmprojEntry> MmprojFiles { get; } = new();

        private bool _mmprojAvailable;
        public bool MmprojAvailable { get => _mmprojAvailable; private set => SetProperty(ref _mmprojAvailable, value); }

        private MmprojEntry? _selectedMmproj;
        public MmprojEntry? SelectedMmproj
        {
            get => _selectedMmproj;
            set { if (SetProperty(ref _selectedMmproj, value)) { UpdateMmprojInfo(); SaveSoon(); } }
        }

        private bool _mmprojChecked;
        public bool MmprojChecked
        {
            get => _mmprojChecked;
            set { if (SetProperty(ref _mmprojChecked, value)) SaveSoon(); }
        }

        private string _mmprojInfoText = "";
        public string MmprojInfoText { get => _mmprojInfoText; private set => SetProperty(ref _mmprojInfoText, value); }

        // ==================== Sampling (параметры разработчика) ====================

        private bool _samplingEnabled = BenchDefaults.DefaultSamplingEnabled;
        public bool SamplingEnabled { get => _samplingEnabled; set { if (SetProperty(ref _samplingEnabled, value)) SaveSoon(); } }

        private double _temp = BenchDefaults.DefaultTemp;
        public double Temp { get => _temp; set { if (SetProperty(ref _temp, value)) SaveSoon(); } }

        private int _topK = BenchDefaults.DefaultTopK;
        public int TopK { get => _topK; set { if (SetProperty(ref _topK, value)) SaveSoon(); } }

        private double _topP = BenchDefaults.DefaultTopP;
        public double TopP { get => _topP; set { if (SetProperty(ref _topP, value)) SaveSoon(); } }

        private double _minP = BenchDefaults.DefaultMinP;
        public double MinP { get => _minP; set { if (SetProperty(ref _minP, value)) SaveSoon(); } }

        private double _repeatPenalty = BenchDefaults.DefaultRepeatPenalty;
        public double RepeatPenalty { get => _repeatPenalty; set { if (SetProperty(ref _repeatPenalty, value)) SaveSoon(); } }

        private int _repeatLastN = BenchDefaults.DefaultRepeatLastN;
        public int RepeatLastN { get => _repeatLastN; set { if (SetProperty(ref _repeatLastN, value)) SaveSoon(); } }

        private double _presencePenalty = BenchDefaults.DefaultPresencePenalty;
        public double PresencePenalty { get => _presencePenalty; set { if (SetProperty(ref _presencePenalty, value)) SaveSoon(); } }

        private double _frequencyPenalty = BenchDefaults.DefaultFrequencyPenalty;
        public double FrequencyPenalty { get => _frequencyPenalty; set { if (SetProperty(ref _frequencyPenalty, value)) SaveSoon(); } }

        private int _seed = BenchDefaults.DefaultSeed;
        public int Seed { get => _seed; set { if (SetProperty(ref _seed, value)) SaveSoon(); } }

        // ==================== Выходы ====================

        private string _launchCommand = "(выбери модель и нажми «Собрать команду»)";
        public string LaunchCommand { get => _launchCommand; private set => SetProperty(ref _launchCommand, value); }

        public ObservableCollection<string> Warnings { get; } = new();

        private string _copyStatusText = "";
        public string CopyStatusText { get => _copyStatusText; private set => SetProperty(ref _copyStatusText, value); }

        private string _layerEstimateText = "Оценка: модель не выбрана.";
        public string LayerEstimateText { get => _layerEstimateText; private set => SetProperty(ref _layerEstimateText, value); }

        private int _selectedTabIndex;
        public int SelectedTabIndex { get => _selectedTabIndex; set { if (SetProperty(ref _selectedTabIndex, value)) SaveSoon(); } }

        // ==================== Команды ====================

        public System.Windows.Input.ICommand ScanCommand { get; }
        public System.Windows.Input.ICommand RefreshGpusCommand { get; }
        public System.Windows.Input.ICommand BuildCommandCommand { get; }
        public System.Windows.Input.ICommand CopyCommandCommand { get; }
        public System.Windows.Input.ICommand PresetV100OnlyCommand { get; }
        public System.Windows.Input.ICommand PresetSafeCommand { get; }
        public System.Windows.Input.ICommand PresetBalancedCommand { get; }
        public System.Windows.Input.ICommand PresetAggressiveCommand { get; }
        public System.Windows.Input.ICommand PresetQ8Command { get; }
        public System.Windows.Input.ICommand PresetV100Command { get; }

        // ==================== Сканирование ====================

        private async Task ScanAsync()
        {
            StatusText = "Сканирование моделей...";
            var res = await Task.Run(() => GgufScannerService.Scan(ModelsRoot));

            if (res.Error != null)
            {
                StatusText = "Не прочитать папку моделей: " + res.Error;
                return;
            }

            _allMmproj = res.AllMmproj;

            Models.Clear();
            foreach (var m in res.Models) Models.Add(m);

            if (Models.Count == 0)
            {
                StatusText = "GGUF не найдены в " + ModelsRoot;
                return;
            }

            var target = Models.FirstOrDefault(x =>
                x.FullPath.Equals(_store.Settings.LastModelPath, StringComparison.OrdinalIgnoreCase)) ?? Models[0];

            SelectedModel = target; // триггерит LoadModelAsync
        }

        // ==================== Загрузка модели ====================

        private async Task LoadModelAsync(ModelEntry? m)
        {
            int seq = ++_loadSeq;
            _gguf = null;

            if (m == null)
            {
                _currentPath = null;
                InfoArch = InfoBlocks = InfoMaxCtx = InfoFileSize = InfoMtpSize = InfoQuant = InfoTools = "-";
                MtpAvailable = false; MtpChecked = false;
                ReasoningAvailable = false; ReasoningChecked = false;
                JinjaChecked = false;
                MmprojAvailable = false; MmprojChecked = false; MmprojFiles.Clear();
                StatusText = "Модель не выбрана";
                return;
            }

            GgufInfo g;
            try
            {
                g = await Task.Run(() => GgufInfo.Read(m.FullPath));
            }
            catch (Exception ex)
            {
                if (seq != _loadSeq) return;
                StatusText = "Ошибка: " + ex.Message;
                return;
            }

            if (seq != _loadSeq) return; // уже выбрана другая модель

            _gguf = g;
            _currentPath = m.FullPath;

            bool q8 = g.IsQ8Quant(m.FileName);
            var ms = _store.GetOrCreateModel(m.FullPath);

            _suppressSave = true;
            try
            {
                MaxContext = (int)Math.Min(int.MaxValue, Math.Max(32768, g.ContextLength));

                Context = ms.Context > 0
                    ? Math.Clamp(ms.Context, 1024, MaxContext)
                    : Math.Min(BenchDefaults.DefaultContext, MaxContext);

                if (KvOptions.Contains(ms.KvK)) KvK = ms.KvK; else KvK = "q8_0";
                if (KvOptions.Contains(ms.KvV)) KvV = ms.KvV; else KvV = "q8_0";
                if (FlashOptions.Contains(ms.Flash)) Flash = ms.Flash; else Flash = "auto";

                ManualNglMax = Math.Max(1, g.BlockCount + 8);
                if (ManualNgl > ManualNglMax) ManualNgl = ManualNglMax; // хранимое значение клампим, но не сбрасываем

                // MTP: у Q8-квантов недоступен по заявлению издателя
                MtpAvailable = g.HasMtp && !q8;
                MtpChecked = MtpAvailable && ms.MtpChecked;

                DraftMax = Math.Clamp(ms.DraftMax, 1, 16);
                DraftMin = Math.Clamp(ms.DraftMin, 0, DraftMax);
                DraftP = Math.Clamp(ms.DraftP, 0, 1);
                if (KvOptions.Contains(ms.DraftK)) DraftK = ms.DraftK; else DraftK = "q8_0";
                if (KvOptions.Contains(ms.DraftV)) DraftV = ms.DraftV; else DraftV = "q8_0";

                ReasoningAvailable = g.HasReasoning;
                ReasoningChecked = g.HasReasoning && ms.ReasoningChecked;
                ReasonBudget = Math.Clamp(ms.ReasonBudget, 0, 1_000_000);

                // --jinja: авто по вердикту сканера; ручной выбор юзера имеет приоритет
                _suppressJinjaEdit = true;
                JinjaChecked = ms.JinjaEdited ? ms.UseJinja : g.ToolSupport == ToolSupportKind.Yes;
                _suppressJinjaEdit = false;

                InfoTools = g.ToolSupport switch
                {
                    ToolSupportKind.Yes => "да — " + g.ToolEvidence,
                    ToolSupportKind.No => "нет — " + g.ToolEvidence,
                    _ => "неизвестно — " + g.ToolEvidence
                };

                BuildMmprojList(m, ms);

                // Алиас: не приводим к нижнему регистру; правленный вручную не перегенерируем
                _suppressAliasEdit = true;
                AliasText = (ms.AliasEdited && !string.IsNullOrWhiteSpace(ms.Alias))
                    ? ms.Alias
                    : AliasBuilder.MakeAlias(m.FileName);
                _suppressAliasEdit = false;

                InfoArch = g.Arch;
                InfoBlocks = g.BlockCount.ToString();
                InfoMaxCtx = g.ContextLength.ToString();
                InfoFileSize = $"{g.FileSize / GiB:F2} GiB";
                InfoMtpSize = g.HasMtp ? $"~{g.MtpSize / MiB:F0} MiB" : "нет";
                InfoQuant = q8 ? "Q8_0 — MTP отключён" : "-";

                StatusText = $"Загружено: {m.FileName}" +
                             $" | MTP: {(g.HasMtp ? (MtpAvailable ? "да" : "есть, но недоступен (Q8)") : "нет")}" +
                             $" | reasoning: {(g.HasReasoning ? "да" : "нет")}" +
                             $" | tools: {(g.ToolSupport == ToolSupportKind.Yes ? "да" : g.ToolSupport == ToolSupportKind.No ? "нет" : "?")}" +
                             (MmprojAvailable ? " | mmproj: да" : "");
            }
            finally
            {
                _suppressSave = false;
            }

            UpdateFitTargets();
            UpdateLayerEstimate();
            SaveSoon();
        }

        private void BuildMmprojList(ModelEntry m, ModelSettings ms)
        {
            MmprojFiles.Clear();

            if (m.LocalMmproj.Count > 0)
            {
                foreach (var p in m.LocalMmproj)
                {
                    long size = 0;
                    try { size = new FileInfo(p).Length; } catch { }
                    MmprojFiles.Add(new MmprojEntry { FullPath = p, DisplayName = Path.GetFileName(p), FileSize = size, IsLocal = true });
                }
            }
            else
            {
                // Рядом с моделью нет — показываем всё найденное в дереве моделей
                foreach (var p in _allMmproj)
                {
                    MmprojFiles.Add(new MmprojEntry
                    {
                        FullPath = p.FullPath,
                        DisplayName = p.DisplayName + "  (в дереве моделей)",
                        FileSize = p.FileSize,
                        IsLocal = false
                    });
                }
            }

            MmprojAvailable = MmprojFiles.Count > 0;

            var chosen = MmprojFiles.FirstOrDefault(f => f.FullPath.Equals(ms.MmprojPath, StringComparison.OrdinalIgnoreCase))
                         ?? MmprojFiles.FirstOrDefault();
            SelectedMmproj = chosen;
            MmprojChecked = MmprojAvailable && ms.MmprojEnabled && chosen != null;

            UpdateMmprojInfo();
        }

        private void UpdateMmprojInfo()
        {
            if (SelectedMmproj == null)
            {
                MmprojInfoText = "mmproj-файлы не найдены";
                return;
            }
            double mib = SelectedMmproj.FileSize / (1024.0 * 1024.0);
            MmprojInfoText = $"Проектор: {Path.GetFileName(SelectedMmproj.FullPath)} (~{mib:F0} MiB)";
        }

        // ==================== GPU ====================

        private async Task RefreshGpusAsync()
        {
            GpuSummaryText = "GPU: опрос llama-server --list-devices...";
            var r = await GpuService.QueryAsync();

            if (!r.Ok)
            {
                _gpus = new List<GpuDeviceInfo>();
                GpuSummaryText = "GPU: llama-server --list-devices не удалось распарсить.\n" + r.Message;
                UpdateLayerEstimate();
                return;
            }

            _gpus = r.Devices;
            GpuSummaryText = "GPU: " + string.Join(" | ", _gpus.Select(g => g.ToString()));

            // Подставляем реальные CUDA-id (резервы/профиль не трогаем)
            DevicesText = string.Join(",", _gpus.Select(g => g.Id));

            UpdateFitTargets();
            UpdateLayerEstimate();
        }

        private string FindV100Device()
        {
            var d = _gpus.FirstOrDefault(x => x.IsV100());
            return d?.Id ?? "CUDA0";
        }

        private string FindDesktopRtxDevice()
        {
            var d = _gpus.FirstOrDefault(x => x.IsDesktopRtx());
            if (d != null && !d.Id.Equals(FindV100Device(), StringComparison.OrdinalIgnoreCase)) return d.Id;
            return "CUDA1";
        }

        private string CombinedDevices() => FindV100Device() + "," + FindDesktopRtxDevice();

        private List<string> SelectedDevices() => (DevicesText ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => x.Length > 0)
            .ToList();

        private double ReserveForDeviceGiB(string deviceId, int position)
        {
            var info = _gpus.FirstOrDefault(x => x.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
            if (info != null)
            {
                if (info.IsV100()) return ReserveV100GiB;
                if (info.IsDesktopRtx()) return Math.Max(ReserveRtxGiB, BenchDefaults.MinDesktopReserveGiB);
            }
            // Fallback: первая карта — compute V100, вторая — desktop RTX
            return position == 0 ? ReserveV100GiB : Math.Max(ReserveRtxGiB, BenchDefaults.MinDesktopReserveGiB);
        }

        private static int GiBToMiB(double gib) => (int)Math.Round(gib * 1024.0, MidpointRounding.AwayFromZero);

        private List<int> CurrentFitTargetsMiB()
        {
            var devs = SelectedDevices();
            var result = new List<int>();
            for (int i = 0; i < devs.Count; i++)
                result.Add(GiBToMiB(ReserveForDeviceGiB(devs[i], i)));
            return result;
        }

        private void UpdateFitTargets()
        {
            var devs = SelectedDevices();
            if (devs.Count == 0)
            {
                FitTargetsText = "fit-target: устройства не заданы";
                return;
            }
            var targets = CurrentFitTargetsMiB();
            FitTargetsText = "--fit-target " + string.Join(",", targets) + " MiB";
        }

        private void SyncDraftRange()
        {
            // DraftMin max привязан к DraftMax в UI; здесь только страховка значения
            if (DraftMin > DraftMax) DraftMin = DraftMax;
        }

        // ==================== Пресеты ====================

        private void ApplyBaseAuto(double rtxReserveGiB, bool v100Only)
        {
            _suppressSave = true;
            try
            {
                Context = Math.Min(32768, MaxContext);
                KvK = "q8_0";
                KvV = "q8_0";
                Flash = "auto";
                ModeIndex = 0;
                DevicesText = v100Only ? FindV100Device() : CombinedDevices();
                ReserveV100GiB = BenchDefaults.SafeReserveV100GiB;
                ReserveRtxGiB = Math.Max(rtxReserveGiB, BenchDefaults.MinDesktopReserveGiB);
                Batch = 2048;
                UBatch = 512;
                Slots = 1;
                Threads = 0;
                ThreadsBatch = 0;
                CacheReuse = 256;
                SsePing = 15;
                Timeout = 7200;
                if (MtpAvailable) MtpChecked = false;
                if (ReasoningAvailable)
                {
                    ReasoningChecked = true;
                    ReasonBudget = Math.Min(4096, 1_000_000);
                }
            }
            finally
            {
                _suppressSave = false;
            }

            UpdateFitTargets();
            SaveSoon();
        }

        private void ApplyPresetQ8()
        {
            ApplyBaseAuto(BenchDefaults.SafeReserveRtxGiB, v100Only: false);
            _suppressSave = true;
            try
            {
                Batch = 1024;
                UBatch = 256;
                CacheReuse = 256;
                SsePing = 10;
                Timeout = 10800;
            }
            finally { _suppressSave = false; }
            SaveSoon();
        }

        private void ApplyPresetV100()
        {
            ApplyBaseAuto(BenchDefaults.SafeReserveRtxGiB, v100Only: false);
            _suppressSave = true;
            try
            {
                Flash = "on";
                UBatch = 64; // ЭКСПЕРИМЕНТ: сравнить 64/128/256/512/1024
                CacheReuse = 0;
            }
            finally { _suppressSave = false; }
            SaveSoon();
        }

        // ==================== Алиас ====================

        private string _aliasText = BenchDefaults.DefaultAlias;
        public string AliasText
        {
            get => _aliasText;
            set
            {
                if (!SetProperty(ref _aliasText, value)) return;
                if (_suppressAliasEdit) return;
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

        // ==================== Сохранение параметров ====================

        private DispatcherTimer SaveTimer
        {
            get
            {
                if (_saveTimer == null)
                {
                    _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                    _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
                }
                return _saveTimer;
            }
        }

        private void SaveSoon()
        {
            if (_suppressSave) return;
            SaveTimer.Stop();
            SaveTimer.Start();
        }

        /// <summary>Немедленно сбросить отложенные правки в хранилище (сохранить сейчас).</summary>
        public void FlushPendingSave()
        {
            _saveTimer?.Stop();
            if (_suppressSave) return;
            if (_currentPath != null || Models.Count > 0) SaveNow();
        }

        /// <summary>Синхронное сохранение: глобальные параметры + профиль текущей модели.</summary>
        public void SaveNow()
        {
            var s = _store.Settings;
            s.ModelsRoot = ModelsRoot;
            s.LastModelPath = _currentPath ?? "";
            s.SelectedTabIndex = SelectedTabIndex;
            s.Global = SnapshotGlobal();

            if (_currentPath != null)
            {
                var ms = _store.GetOrCreateModel(_currentPath);
                FillModelSettings(ms);
            }

            _store.Save(s);
        }

        private void ApplyGlobalFromStore()
        {
            _suppressSave = true;
            try
            {
                var gp = _store.Settings.Global;
                Host = gp.Host;
                Port = Math.Clamp(gp.Port, 1024, 65535);
                DevicesText = gp.Devices;
                ReserveV100GiB = gp.ReserveV100GiB;
                ReserveRtxGiB = gp.ReserveRtxGiB;
                ModeIndex = Math.Clamp(gp.ModeIndex, 0, 1);
                if (SplitModeOptions.Contains(gp.SplitMode)) SplitMode = gp.SplitMode;
                ManualNgl = Math.Max(0, gp.ManualNgl);
                Split0 = Math.Max(0, gp.Split0);
                Split1 = Math.Max(0, gp.Split1);
                Batch = Math.Max(32, gp.Batch);
                UBatch = Math.Max(8, gp.UBatch);
                Slots = Math.Max(1, gp.Slots);
                Threads = Math.Max(0, gp.Threads);
                ThreadsBatch = Math.Max(0, gp.ThreadsBatch);
                PromptCache = gp.PromptCache;
                CacheReuse = Math.Max(0, gp.CacheReuse);
                SsePing = Math.Clamp(gp.SsePing, 1, 300);
                Timeout = Math.Clamp(gp.Timeout, 60, 86400);
                Perf = gp.Perf;

                SamplingEnabled = gp.SamplingEnabled;
                Temp = Math.Clamp(gp.Temp, 0, 2);
                TopK = Math.Clamp(gp.TopK, 0, 200);
                TopP = Math.Clamp(gp.TopP, 0, 1);
                MinP = Math.Clamp(gp.MinP, 0, 1);
                RepeatPenalty = Math.Clamp(gp.RepeatPenalty, 0, 2);
                RepeatLastN = Math.Clamp(gp.RepeatLastN, 0, 8192);
                PresencePenalty = Math.Clamp(gp.PresencePenalty, -2, 2);
                FrequencyPenalty = Math.Clamp(gp.FrequencyPenalty, -2, 2);
                Seed = gp.Seed;

                ModelsRoot = string.IsNullOrWhiteSpace(_store.Settings.ModelsRoot)
                    ? BenchDefaults.ModelsRoot
                    : _store.Settings.ModelsRoot;
                SelectedTabIndex = Math.Clamp(_store.Settings.SelectedTabIndex, 0, 1);
            }
            finally { _suppressSave = false; }
        }

        private GlobalParams SnapshotGlobal() => new()
        {
            Host = Host,
            Port = Port,
            Devices = DevicesText,
            ReserveV100GiB = ReserveV100GiB,
            ReserveRtxGiB = ReserveRtxGiB,
            ModeIndex = ModeIndex,
            SplitMode = SplitMode,
            ManualNgl = ManualNgl,
            Split0 = Split0,
            Split1 = Split1,
            Batch = Batch,
            UBatch = UBatch,
            Slots = Slots,
            Threads = Threads,
            ThreadsBatch = ThreadsBatch,
            PromptCache = PromptCache,
            CacheReuse = CacheReuse,
            SsePing = SsePing,
            Timeout = Timeout,
            Perf = Perf,
            SamplingEnabled = SamplingEnabled,
            Temp = Temp,
            TopK = TopK,
            TopP = TopP,
            MinP = MinP,
            RepeatPenalty = RepeatPenalty,
            RepeatLastN = RepeatLastN,
            PresencePenalty = PresencePenalty,
            FrequencyPenalty = FrequencyPenalty,
            Seed = Seed
        };

        private void FillModelSettings(ModelSettings ms)
        {
            ms.Alias = AliasText;
            // ms.AliasEdited управляется сеттером AliasText: правка вручную помечается там,
            // программная (при загрузке модели) — не помечает.
            ms.Context = Context;
            ms.KvK = KvK;
            ms.KvV = KvV;
            ms.Flash = Flash;
            ms.MtpChecked = MtpAvailable && MtpChecked;
            ms.DraftMax = DraftMax;
            ms.DraftMin = DraftMin;
            ms.DraftP = DraftP;
            ms.DraftK = DraftK;
            ms.DraftV = DraftV;
            ms.ReasoningChecked = ReasoningChecked;
            ms.ReasonBudget = ReasonBudget;
            ms.UseJinja = JinjaChecked;   // ms.JinjaEdited — только из сеттера (ручная правка)
            ms.MmprojPath = SelectedMmproj?.FullPath ?? "";
            ms.MmprojEnabled = MmprojAvailable && MmprojChecked;
        }

        private const double MiB = 1024.0 * 1024.0;
        private const double GiB = 1024.0 * 1024.0 * 1024.0;
    }
}
