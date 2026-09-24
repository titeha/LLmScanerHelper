using System.IO;
using System.Text;
using LlmScanHelper.Models;
using LlmScanHelper.UI.Services;
using LlmScanHelper.Models.Command;
using LlmScanHelper.Models.Gpu;
using LlmScanHelper.Models.Estimation;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Сборка строки запуска llama-server, предупреждения, оценка слоёв, буфер обмена.
  /// Все флаги llama-server добавляются здесь (карта достройки — в шапке файла).
  /// </summary>
  public sealed partial class MainViewModel
  {
    // Имена флагов бюджета reasoning гуляют между билдами — правятся в ОДНОМ месте.
    private const string ReasoningBudgetFlag = "--reasoning-budget";
    private const string ReasonBudgetMessageFlag = "--reasoning-budget-message";

    // ==================== «Собрать команду» ====================

    private void BuildOutputs()
    {
      if (_gguf == null || _currentPath == null)
      {
        LaunchCommand = "(сначала выбери модель)";
        Warnings.Clear();
        Warnings.Add("Выбери модель в списке — без GGUF-метаданных команда не собирается.");
        UpdateLayerEstimate();
        return;
      }

      LaunchCommand = BuildCommand(_currentPath);

      Warnings.Clear();
      foreach (var w in BuildWarnings()) Warnings.Add(w);

      UpdateLayerEstimate();
      CopyStatusText = "";
    }

    // internal — для регрессионных тестов сборки команды (LLMScanHelper.Tests).
        internal string BuildCommand(string modelPath)
      => LlamaServerCommandBuilder.Build(ToParams(), _gguf, modelPath);

    // ==================== Предупреждения ====================

        private List<string> BuildWarnings()
      => LlamaServerCommandBuilder.BuildWarnings(ToParams(), _gguf);

    // ==================== Оценка распределения слоёв ====================

        private void UpdateLayerEstimate()
    {
      var g = _gguf;
      if (g == null || g.LayerSize.Length == 0)
      {
        LayerEstimateText = "Оценка: модель не выбрана.";
        return;
      }

      var devs = FitTargets.ParseDevices(DevicesText);
      if (devs.Count == 0)
      {
        LayerEstimateText = "Оценка: устройства не заданы.";
        return;
      }

      var est = LayerEstimator.Estimate(
        g, _gpus, devs, FitTargets.CurrentFitTargetsMiB(_gpus, DevicesText, ReserveV100GiB, ReserveRtxGiB),
        Context, KvK, KvV,
        useMtp: MtpAvailable && MtpChecked && g.HasMtp);

      LayerEstimateText = LayerEstimateFormatter.Format(est);
    }

    // Собирает record для LlamaServerCommandBuilder из свойств VM (один к одному).
    private LlamaServerParams ToParams() => new()
    {
      Host = Host, Port = Port, DevicesText = DevicesText, AliasText = AliasText,
      JinjaChecked = JinjaChecked, MmprojAvailable = MmprojAvailable, MmprojChecked = MmprojChecked,
      SelectedMmproj = SelectedMmproj, ModeIndex = ModeIndex, SplitMode = SplitMode,
      ManualNgl = ManualNgl, Split0 = Split0, Split1 = Split1,
      Context = Context, KvK = KvK, KvV = KvV, Batch = Batch, UBatch = UBatch, Slots = Slots,
      Threads = Threads, ThreadsBatch = ThreadsBatch, Flash = Flash, Timeout = Timeout,
      SsePing = SsePing, PromptCache = PromptCache, CacheReuse = CacheReuse, Perf = Perf,
      SamplingEnabled = SamplingEnabled, Temp = Temp, TopK = TopK, TopP = TopP, MinP = MinP,
      RepeatPenalty = RepeatPenalty, RepeatLastN = RepeatLastN, PresencePenalty = PresencePenalty,
      FrequencyPenalty = FrequencyPenalty, Seed = Seed,
      MtpAvailable = MtpAvailable, MtpChecked = MtpChecked, DraftMax = DraftMax, DraftMin = DraftMin,
      DraftP = DraftP, DraftK = DraftK, DraftV = DraftV,
      ReasoningMode = ReasoningMode, ReasonBudget = ReasonBudget,
      ReasonBudgetMessage = ReasonBudgetMessage,
      Gpus = _gpus, ReserveV100GiB = ReserveV100GiB, ReserveRtxGiB = ReserveRtxGiB,
      SelectedModelDisplayName = SelectedModel?.DisplayName ?? "",
    };

    // ==================== Буфер обмена ====================

    private void CopyToClipboard()
    {
      if (string.IsNullOrWhiteSpace(LaunchCommand) || LaunchCommand.StartsWith("("))
      {
        ShowCopyStatus("Сначала «Собрать команду»");
        return;
      }

      try
      {
        _clipboard.SetText(LaunchCommand);
        ShowCopyStatus("Скопировано ✓");
      }
      catch
      {
        // Буфер может быть занят другим процессом
        ShowCopyStatus("Не удалось скопировать — буфер занят другой программой");
      }
    }

    private void CopyAliasToClipboard()
    {
      if (string.IsNullOrWhiteSpace(AliasText))
      {
        ShowCopyStatus("Пустой алиас — нечего копировать");
        return;
      }

      try
      {
        _clipboard.SetText(AliasText);
        ShowCopyStatus("Алиас скопирован ✓");
      }
      catch
      {
        ShowCopyStatus("Не удалось скопировать — буфер занят другой программой");
      }
    }

    private readonly Debouncer _flashDebouncer;

    private void ShowCopyStatus(string text)
    {
      CopyStatusText = text;
      _flashDebouncer.Start();
    }
  }
}
