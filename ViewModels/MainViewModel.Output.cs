using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using LlmScanHelper.Models;

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
    {
      var g = _gguf;
      if (g == null) return "(сначала выбери модель)";

      string host = string.IsNullOrWhiteSpace(Host) ? AppDefaults.DefaultHost : Host.Trim();
      string devices = DevicesText.Trim();

      var sb = new StringBuilder();
      sb.Append("llama-server -m \"").Append(modelPath.Replace("\"", "\\\"")).Append("\"");

      if (!string.IsNullOrWhiteSpace(AliasText))
        sb.Append(" --alias \"").Append(AliasText.Trim()).Append("\"");

      // Родной chat-шаблон GGUF: без него сервер не отдаст модели tools
      // и не распарсит ответ в OpenAI-совместимые tool_calls (агентная работа).
      // Без шаблона в GGUF флаг бессмысленен — даже если галочка осталась в профиле.
      if (JinjaChecked && g.HasChatTemplate)
        sb.Append(" --jinja");

      // Мультимодальность: --mmproj (переключатель + выбранный файл)
      if (MmprojAvailable && MmprojChecked && SelectedMmproj != null)
        sb.Append(" --mmproj \"").Append(SelectedMmproj.FullPath.Replace("\"", "\\\"")).Append("\"");

      if (!string.IsNullOrWhiteSpace(devices))
        sb.Append(" --device ").Append(devices);

      if (ModeIndex == 0)
      {
        // AUTO: НЕ задаём точный -ngl и НЕ задаём --tensor-split.
        sb.Append(" --split-mode layer");
        sb.Append(" --fit on");

        var targets = CurrentFitTargetsMiB();
        if (targets.Count > 0)
          sb.Append(" --fit-target ").Append(string.Join(",", targets));
      }
      else
      {
        // MANUAL: --fit off, явный -ngl, --tensor-split — ПРОПОРЦИИ, не слои.
        string sm = SplitMode;
        sb.Append(" --fit off");
        sb.Append(" --split-mode ").Append(sm);
        sb.Append(" -ngl ").Append(ManualNgl);

        if (!sm.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
          int a = Split0, b = Split1;
          if (a > 0 || b > 0)
            sb.Append(" --tensor-split ").Append(a).Append(",").Append(b);
        }
      }

      sb.Append(" -c ").Append(Context);
      sb.Append(" --cache-type-k ").Append(KvK);
      sb.Append(" --cache-type-v ").Append(KvV);
      sb.Append(" -b ").Append(Batch);
      sb.Append(" -ub ").Append(UBatch);
      sb.Append(" -np ").Append(Slots);

      if (Threads > 0) sb.Append(" -t ").Append(Threads);
      if (ThreadsBatch > 0) sb.Append(" -tb ").Append(ThreadsBatch);

      sb.Append(" --host ").Append(host);
      sb.Append(" --port ").Append(Port);
      sb.Append(" -fa ").Append(Flash);
      sb.Append(" --timeout ").Append(Timeout);
      sb.Append(" --sse-ping-interval ").Append(SsePing);
      sb.Append(PromptCache ? " --cache-prompt" : " --no-cache-prompt");
      if (PromptCache && CacheReuse > 0)
        sb.Append(" --cache-reuse ").Append(CacheReuse);
      if (Perf)
        sb.Append(" --perf");

      // Sampling — параметры разработчика (дефолты сервера)
      if (SamplingEnabled)
      {
        sb.Append(" --temp ").Append(Num(Temp));
        sb.Append(" --top-k ").Append(TopK);
        sb.Append(" --top-p ").Append(Num(TopP));
        sb.Append(" --min-p ").Append(Num(MinP));
        sb.Append(" --repeat-penalty ").Append(Num(RepeatPenalty));
        sb.Append(" --repeat-last-n ").Append(RepeatLastN);
        sb.Append(" --presence-penalty ").Append(Num(PresencePenalty));
        sb.Append(" --frequency-penalty ").Append(Num(FrequencyPenalty));
        sb.Append(" --seed ").Append(Seed);
      }

      // MTP (draft-mtp): только если доступен и включён
      if (MtpAvailable && MtpChecked && g.HasMtp)
      {
        int dMax = DraftMax;
        int dMin = Math.Min(DraftMin, dMax);
        double dP = DraftP;

        sb.Append(" --spec-type draft-mtp");
        sb.Append(" --spec-draft-n-max ").Append(dMax);
        sb.Append(" --spec-draft-n-min ").Append(dMin);
        if (dP > 0)
          sb.Append(" --spec-draft-p-min ").Append(dP.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        sb.Append(" --spec-draft-type-k ").Append(DraftK);
        sb.Append(" --spec-draft-type-v ").Append(DraftV);
      }

      if (g.HasReasoning)
      {
        sb.Append(" --reasoning ").Append(ReasoningChecked ? "on" : "off");
        if (ReasoningChecked)
        {
          // ТЗ3: бюджет — только при значении > 0. 0 → не передаём (в runtime это unlimited).
          if (ReasonBudget > 0)
            sb.Append(" ").Append(ReasoningBudgetFlag).Append(" ").Append((long)ReasonBudget);

          // ТЗ3: сообщение — только при непустом значении (поле предзаполнено стандартным текстом).
          if (!string.IsNullOrWhiteSpace(ReasonBudgetMessage))
          {
            var msg = ReasonBudgetMessage.Trim();
            sb.Append(" ").Append(ReasonBudgetMessageFlag)
              .Append(" \"").Append(msg.Replace("\"", "\\\"")).Append("\"");
          }
        }
      }

      return sb.ToString();
    }

    private static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    // ==================== Предупреждения ====================

    private List<string> BuildWarnings()
    {
      var w = new List<string>();
      var g = _gguf;
      if (g == null) return w;

      int ctx = Context;
      bool quantKv = KvK != "f16" || KvV != "f16";
      bool mtp = MtpAvailable && MtpChecked;

      if (ModeIndex == 1)
        w.Add("MANUAL отключает --fit. --tensor-split — пропорции, а не точные слои. OOM в этом режиме — ответственность ручной раскладки.");

      if (ctx >= 65536)
        w.Add("Контекст 64k+ заметно увеличивает KV/RS/scratch. Для Q8 сначала сравни тот же агент на 32k: больше весов может остаться на GPU.");

      if (quantKv && Flash == "on")
        w.Add("FlashAttention принудительно ON + квантованный KV: если сборка без нужных CUDA FA kernels, возможен очень медленный fallback. При странной скорости сравни -fa auto и проверь лог.");

      if (mtp)
      {
        w.Add("MTP создаёт дополнительный speculative context/cache. На пограничной по VRAM модели сначала измерь baseline без MTP, потом MTP.");
        if (ModeIndex == 0 && ReserveV100GiB < 1.00)
          w.Add("MTP добавляет speculative context/cache. На V100 запас <1 GiB может быть тесным; если увидишь OOM, первым делом увеличь резерв V100.");
      }

      // Tool-calls / агентная работа
      if (JinjaChecked && g.ToolSupport == ToolSupportKind.No)
        w.Add("Chat-шаблон GGUF без обработки tools: --jinja включён, но tool-calls через этот шаблон работать не будут — проверь карточку модели на HF.");
      if (!JinjaChecked && g.ToolSupport == ToolSupportKind.Yes)
        w.Add("Шаблон модели поддерживает tools, но --jinja выключен: для агентной работы с функциями включи --jinja.");

      if (Slots > 1)
        w.Add($"Слотов {Slots}: для одного coding-agent обычно быстрее/предсказуемее -np 1; параллельность делит ресурсы и контекст между слотами.");

      if (CacheReuse > 0)
        w.Add("cache-reuse полезен в реальной агентной работе, но для чистого сравнительного benchmark ставь 0 или начинай каждый прогон на чистом server/cache.");

      if (MmprojAvailable && MmprojChecked && SelectedMmproj != null)
        w.Add($"Мультимодальность включена: проектор {Path.GetFileName(SelectedMmproj.FullPath)} (~{SelectedMmproj.FileSize / MiB / 1024.0:F1} GiB) загрузится дополнительно. Для чистого benchmark отключи.");

      if (_gpus.Count > 0 && ModeIndex == 0)
      {
        long freeAfterMargin = 0;
        var devs = SelectedDevices();
        var targets = CurrentFitTargetsMiB();
        for (int i = 0; i < devs.Count; i++)
        {
          var gi = _gpus.FirstOrDefault(x => x.Id.Equals(devs[i], StringComparison.OrdinalIgnoreCase));
          if (gi == null) continue;
          int margin = i < targets.Count ? targets[i] : 1024;
          freeAfterMargin += Math.Max(0, gi.FreeMiB - margin);
        }

        double fileMiB = g.FileSize / MiB;
        if (fileMiB > freeAfterMargin)
          w.Add($"GGUF ~{fileMiB / 1024.0:F1} GiB больше доступного GPU-бюджета после fit-target (~{freeAfterMargin / 1024.0:F1} GiB). Часть весов почти наверняка уйдёт на CPU — это главный кандидат на низкий tok/s.");
      }

      if (ModeIndex == 0)
      {
        var devs = SelectedDevices();
        bool desktopIncluded = devs.Any(id =>
        {
          var gi = _gpus.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
          return gi != null && gi.IsDesktopRtx();
        });

        if (desktopIncluded)
        {
          if (ReserveRtxGiB <= 2.00)
            w.Add("AGGRESSIVE: desktop RTX оставляет только 2 GiB целевого запаса. Не открывай новые GPU-приложения во время работы модели; --fit-target не является hard-cap.");
          else if (ReserveRtxGiB < 3.00)
            w.Add("BALANCED: запас desktop RTX меньше SAFE 3 GiB. Используй только при стабильном наборе уже открытых приложений.");
        }
        else
        {
          w.Add("Desktop RTX не включена в --device: это самый безопасный режим для новой/подозрительной модели и исключает llama-offload на системную видеокарту.");
        }

        if (_gpus.Count == 0)
          w.Add("GPU ещё не опрошены. Нажми «Обновить GPU» перед запуском, чтобы оценка свободной VRAM соответствовала текущему состоянию системы.");
      }

      string fn = SelectedModel?.DisplayName ?? "";
      if (fn.IndexOf("Q8", StringComparison.OrdinalIgnoreCase) >= 0)
        w.Add("Q8 — кандидат на CPU offload в твоей текущей паре 16+6 GB. Для агента обязательно сравни Q6/Q4 на той же задаче: меньший квант может оказаться не только быстрее, но и фактически полезнее.");

      if (UBatch > Batch)
        w.Add("ubatch не должен быть больше batch. Уменьши -ub или увеличь -b.");

      return w;
    }

    // ==================== Оценка распределения слоёв ====================

    private void UpdateLayerEstimate()
    {
      var g = _gguf;
      if (g == null || g.LayerSize.Length == 0)
      {
        LayerEstimateText = "Оценка: модель не выбрана.";
        return;
      }

      var devs = SelectedDevices();
      if (devs.Count == 0)
      {
        LayerEstimateText = "Оценка: устройства не заданы.";
        return;
      }

      var est = LayerEstimator.Estimate(
        g, _gpus, devs, CurrentFitTargetsMiB(),
        Context, KvK, KvV,
        useMtp: MtpAvailable && MtpChecked && g.HasMtp);

      var sb = new StringBuilder();
      sb.AppendLine("Оценка (грубо: веса блоков + KV; эмбеддинги на первой карте):");

      foreach (var d in est.Devices)
      {
        if (!d.Known)
        {
          sb.AppendLine($"  {d.DeviceId} {d.Name}: нет данных VRAM (нужен «Обновить GPU»)");
          continue;
        }
        sb.AppendLine($"  {d.DeviceId} {d.Name}: ~{d.Blocks} бл." +
                $" | веса ~{d.WeightsGiB:F2} GiB, KV ~{d.KvGiB:F2} GiB" +
                $" | бюджет ~{d.BudgetGiB:F2} GiB");
      }

      if (est.CpuBlocks > 0)
        sb.AppendLine($"  CPU: {est.CpuBlocks} бл. (~{est.CpuWeightsGiB:F2} GiB) — выталкивание весов, будет медленно");
      else
        sb.AppendLine("  CPU: всё помещается (по этой оценке)");

      if (est.MtpGiB > 0)
        sb.AppendLine($"  MTP: ~{est.MtpGiB:F2} GiB тензоров сверх раскладки (реальный расход больше)");

      sb.Append("Точную раскладку делает llama.cpp через --fit (KV/RS/scratch/спекулятивный контекст считаются рантаймом).");
      LayerEstimateText = sb.ToString();
    }

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
        Clipboard.SetText(LaunchCommand);
        ShowCopyStatus("Скопировано ✓");
      }
      catch
      {
        // Буфер может быть занят другим процессом
        ShowCopyStatus("Не удалось скопировать — буфер занят другой программой");
      }
    }

    private void ShowCopyStatus(string text)
    {
      CopyStatusText = text;
      if (_flashTimer == null)
      {
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        _flashTimer.Tick += (_, _) => { _flashTimer.Stop(); CopyStatusText = ""; };
      }
      _flashTimer.Stop();
      _flashTimer.Start();
    }
  }
}
