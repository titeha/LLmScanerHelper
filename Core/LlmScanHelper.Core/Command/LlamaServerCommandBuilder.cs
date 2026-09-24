using System.Text;
using LlmScanHelper.Models.Gpu;

namespace LlmScanHelper.Models.Command;

/// <summary>
/// Сборка строки запуска llama-server и списка предупреждений.
/// Перенесено из MainViewModel (UI.Common) 1:1 — поведение не меняется.
/// </summary>
public static class LlamaServerCommandBuilder
{
  // Имена флагов бюджета reasoning гуляют между билдами — правятся в ОДНОМ месте.
  private const string ReasoningBudgetFlag = "--reasoning-budget";
  private const string ReasonBudgetMessageFlag = "--reasoning-budget-message";

  private const double MiB = 1024.0 * 1024.0;

  private static string Num(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

  public static string Build(LlamaServerParams p, GgufInfo? g, string modelPath)
  {
    if (g == null) return "(сначала выбери модель)";

    string host = string.IsNullOrWhiteSpace(p.Host) ? AppDefaults.DefaultHost : p.Host.Trim();
    string devices = p.DevicesText.Trim();

    var sb = new StringBuilder();
    sb.Append("llama-server -m \"").Append(modelPath.Replace("\"", "\\\"")).Append("\"");

    if (!string.IsNullOrWhiteSpace(p.AliasText))
      sb.Append(" --alias \"").Append(p.AliasText.Trim()).Append("\"");

    // Родной chat-шаблон GGUF: без него сервер не отдаст модели tools
    // и не распарсит ответ в OpenAI-совместимые tool_calls (агентная работа).
    // Без шаблона в GGUF флаг бессмысленен — даже если галочка осталась в профиле.
    if (p.JinjaChecked && g.HasChatTemplate)
      sb.Append(" --jinja");

    // Мультимодальность: --mmproj (переключатель + выбранный файл)
    if (p.MmprojAvailable && p.MmprojChecked && p.SelectedMmproj != null)
      sb.Append(" --mmproj \"").Append(p.SelectedMmproj.FullPath.Replace("\"", "\\\"")).Append("\"");

    if (!string.IsNullOrWhiteSpace(devices))
      sb.Append(" --device ").Append(devices);

    if (p.ModeIndex == 0)
    {
      // AUTO: НЕ задаём точный -ngl и НЕ задаём --tensor-split.
      sb.Append(" --split-mode layer");
      sb.Append(" --fit on");

      var targets = FitTargets.CurrentFitTargetsMiB(p.Gpus, p.DevicesText, p.ReserveV100GiB, p.ReserveRtxGiB);
      if (targets.Count > 0)
        sb.Append(" --fit-target ").Append(string.Join(",", targets));
    }
    else
    {
      // MANUAL: --fit off, явный -ngl, --tensor-split — ПРОПОРЦИИ, не слои.
      string sm = p.SplitMode;
      sb.Append(" --fit off");
      sb.Append(" --split-mode ").Append(sm);
      sb.Append(" -ngl ").Append(p.ManualNgl);

      if (!sm.Equals("none", StringComparison.OrdinalIgnoreCase))
      {
        int a = p.Split0, b = p.Split1;
        if (a > 0 || b > 0)
          sb.Append(" --tensor-split ").Append(a).Append(",").Append(b);
      }
    }

    sb.Append(" -c ").Append(p.Context);
    sb.Append(" --cache-type-k ").Append(p.KvK);
    sb.Append(" --cache-type-v ").Append(p.KvV);
    sb.Append(" -b ").Append(p.Batch);
    sb.Append(" -ub ").Append(p.UBatch);
    sb.Append(" -np ").Append(p.Slots);

    if (p.Threads > 0) sb.Append(" -t ").Append(p.Threads);
    if (p.ThreadsBatch > 0) sb.Append(" -tb ").Append(p.ThreadsBatch);

    sb.Append(" --host ").Append(host);
    sb.Append(" --port ").Append(p.Port);
    sb.Append(" -fa ").Append(p.Flash);
    sb.Append(" --timeout ").Append(p.Timeout);
    sb.Append(" --sse-ping-interval ").Append(p.SsePing);
    sb.Append(p.PromptCache ? " --cache-prompt" : " --no-cache-prompt");
    if (p.PromptCache && p.CacheReuse > 0)
      sb.Append(" --cache-reuse ").Append(p.CacheReuse);
    if (p.Perf)
      sb.Append(" --perf");

    // Sampling — параметры разработчика (дефолты сервера)
    if (p.SamplingEnabled)
    {
      sb.Append(" --temp ").Append(Num(p.Temp));
      sb.Append(" --top-k ").Append(p.TopK);
      sb.Append(" --top-p ").Append(Num(p.TopP));
      sb.Append(" --min-p ").Append(Num(p.MinP));
      sb.Append(" --repeat-penalty ").Append(Num(p.RepeatPenalty));
      sb.Append(" --repeat-last-n ").Append(p.RepeatLastN);
      sb.Append(" --presence-penalty ").Append(Num(p.PresencePenalty));
      sb.Append(" --frequency-penalty ").Append(Num(p.FrequencyPenalty));
      sb.Append(" --seed ").Append(p.Seed);
    }

    // MTP (draft-mtp): только если доступен и включён
    if (p.MtpAvailable && p.MtpChecked && g.HasMtp)
    {
      int dMax = p.DraftMax;
      int dMin = Math.Min(p.DraftMin, dMax);
      double dP = p.DraftP;

      sb.Append(" --spec-type draft-mtp");
      sb.Append(" --spec-draft-n-max ").Append(dMax);
      sb.Append(" --spec-draft-n-min ").Append(dMin);
      if (dP > 0)
        sb.Append(" --spec-draft-p-min ").Append(dP.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
      sb.Append(" --spec-draft-type-k ").Append(p.DraftK);
      sb.Append(" --spec-draft-type-v ").Append(p.DraftV);
    }

    if (g.HasReasoning)
    {
      // ТЗ2: on → --reasoning on, off → --reasoning off, auto → флаг не передаём
      // (дефолт runtime — auto, решает по чат-шаблону).
      if (p.ReasoningMode == "on") sb.Append(" --reasoning on");
      else if (p.ReasoningMode == "off") sb.Append(" --reasoning off");

      // Бюджет и сообщение — при on и auto (не off).
      if (p.ReasoningMode != "off")
      {
        // ТЗ3: бюджет — только при значении > 0. 0 → не передаём (в runtime это unlimited).
        // Но при режиме on и 0 бюджета используем минимальный бюджет (1024).
        int budget = p.ReasonBudget;
        if (p.ReasoningMode == "on" && budget == 0)
          budget = AppDefaults.DefaultReasonBudgetMinimum;

        if (budget > 0)
        {
          sb.Append(" ").Append(ReasoningBudgetFlag).Append(" ").Append((long)budget);
          // Сообщение — обязательно при budget > 0. Если поле пустое, ставим дефолт.
          var msg = p.ReasonBudgetMessage?.Trim();
          if (string.IsNullOrWhiteSpace(msg))
            msg = AppDefaults.DefaultReasonBudgetMessage;
          sb.Append(" ").Append(ReasonBudgetMessageFlag)
            .Append(" \"").Append(msg.Replace("\"", "\\\"")).Append("\"");
        }
        else
        {
          // budget = 0 (только auto) → не передаём ни budget, ни message.
        }
      }
    }

    return sb.ToString();
  }

  public static List<string> BuildWarnings(LlamaServerParams p, GgufInfo? g)
  {
    var w = new List<string>();
    if (g == null) return w;

    int ctx = p.Context;
    bool quantKv = p.KvK != "f16" || p.KvV != "f16";
    bool mtp = p.MtpAvailable && p.MtpChecked;

    if (p.ModeIndex == 1)
      w.Add("MANUAL отключает --fit. --tensor-split — пропорции, а не точные слои. OOM в этом режиме — ответственность ручной раскладки.");

    if (ctx >= 65536)
      w.Add("Контекст 64k+ заметно увеличивает KV/RS/scratch. Для Q8 сначала сравни тот же агент на 32k: больше весов может остаться на GPU.");

    if (quantKv && p.Flash == "on")
      w.Add("FlashAttention принудительно ON + квантованный KV: если сборка без нужных CUDA FA kernels, возможен очень медленный fallback. При странной скорости сравни -fa auto и проверь лог.");

    if (mtp)
    {
      w.Add("MTP создаёт дополнительный speculative context/cache. На пограничной по VRAM модели сначала измерь baseline без MTP, потом MTP.");
      if (p.ModeIndex == 0 && p.ReserveV100GiB < 1.00)
        w.Add("MTP добавляет speculative context/cache. На V100 запас <1 GiB может быть тесным; если увидишь OOM, первым делом увеличь резерв V100.");
    }

    // Tool-calls / агентная работа
    if (p.JinjaChecked && g.ToolSupport == ToolSupportKind.No)
      w.Add("Chat-шаблон GGUF без обработки tools: --jinja включён, но tool-calls через этот шаблон работать не будут — проверь карточку модели на HF.");
    if (!p.JinjaChecked && g.ToolSupport == ToolSupportKind.Yes)
      w.Add("Шаблон модели поддерживает tools, но --jinja выключен: для агентной работы с функциями включи --jinja.");

    if (p.Slots > 1)
      w.Add($"Слотов {p.Slots}: для одного coding-agent обычно быстрее/предсказуемее -np 1; параллельность делит ресурсы и контекст между слотами.");

    if (p.CacheReuse > 0)
      w.Add("cache-reuse полезен в реальной агентной работе, но для чистого сравнительного benchmark ставь 0 или начинай каждый прогон на чистом server/cache.");

    if (p.MmprojAvailable && p.MmprojChecked && p.SelectedMmproj != null)
      w.Add($"Мультимодальность включена: проектор {Path.GetFileName(p.SelectedMmproj.FullPath)} (~{p.SelectedMmproj.FileSize / MiB / 1024.0:F1} GiB) загрузится дополнительно. Для чистого benchmark отключи.");

    if (p.Gpus.Count > 0 && p.ModeIndex == 0)
    {
      long freeAfterMargin = 0;
      var devs = FitTargets.ParseDevices(p.DevicesText);
      var targets = FitTargets.CurrentFitTargetsMiB(p.Gpus, p.DevicesText, p.ReserveV100GiB, p.ReserveRtxGiB);
      for (int i = 0; i < devs.Count; i++)
      {
        var gi = p.Gpus.FirstOrDefault(x => x.Id.Equals(devs[i], StringComparison.OrdinalIgnoreCase));
        if (gi == null) continue;
        int margin = i < targets.Count ? targets[i] : 1024;
        freeAfterMargin += Math.Max(0, gi.FreeMiB - margin);
      }

      double fileMiB = g.FileSize / MiB;
      if (fileMiB > freeAfterMargin)
        w.Add($"GGUF ~{fileMiB / 1024.0:F1} GiB больше доступного GPU-бюджета после fit-target (~{freeAfterMargin / 1024.0:F1} GiB). Часть весов почти наверняка уйдёт на CPU — это главный кандидат на низкий tok/s.");
    }

    if (p.ModeIndex == 0)
    {
      var devs = FitTargets.ParseDevices(p.DevicesText);
      bool desktopIncluded = devs.Any(id =>
      {
        var gi = p.Gpus.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        return gi != null && gi.IsDesktopRtx();
      });

      if (desktopIncluded)
      {
        if (p.ReserveRtxGiB <= 2.00)
          w.Add("AGGRESSIVE: desktop RTX оставляет только 2 GiB целевого запаса. Не открывай новые GPU-приложения во время работы модели; --fit-target не является hard-cap.");
        else if (p.ReserveRtxGiB < 3.00)
          w.Add("BALANCED: запас desktop RTX меньше SAFE 3 GiB. Используй только при стабильном наборе уже открытых приложений.");
      }
      else
      {
        w.Add("Desktop RTX не включена в --device: это самый безопасный режим для новой/подозрительной модели и исключает llama-offload на системную видеокарту.");
      }

      if (p.Gpus.Count == 0)
        w.Add("GPU ещё не опрошены. Нажми «Обновить GPU» перед запуском, чтобы оценка свободной VRAM соответствовала текущему состоянию системы.");
    }

    string fn = p.SelectedModelDisplayName;
    if (fn.IndexOf("Q8", StringComparison.OrdinalIgnoreCase) >= 0)
      w.Add("Q8 — кандидат на CPU offload в твоей текущей паре 16+12 GB (V100 + RTX 3060). Для агента обязательно сравни Q6/Q4 на той же задаче: меньший квант может оказаться не только быстрее, но и фактически полезнее.");

    if (p.UBatch > p.Batch)
      w.Add("ubatch не должен быть больше batch. Уменьши -ub или увеличь -b.");

    return w;
  }
}
