using System.Collections.ObjectModel;
using System.IO;

using LlmScanHelper.Models;
using LlmScanHelper.Models.Settings;
using LlmScanHelper.Texts;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Частичный класс: информация о модели, загрузка модели, мультимодальность (mmproj).
  /// </summary>
  public sealed partial class MainViewModel
  {
    // ==================== Информация о модели ====================

    private string _infoArch = "-";
    public string InfoArch { get => _infoArch; private set => Set(ref _infoArch, value); }

    private string _infoBlocks = "-";
    public string InfoBlocks { get => _infoBlocks; private set => Set(ref _infoBlocks, value); }

    private string _infoMaxCtx = "-";
    public string InfoMaxCtx { get => _infoMaxCtx; private set => Set(ref _infoMaxCtx, value); }

    private string _infoFileSize = "-";
    public string InfoFileSize { get => _infoFileSize; private set => Set(ref _infoFileSize, value); }

    private string _infoMtp = "-";
    public string InfoMtp { get => _infoMtp; private set => Set(ref _infoMtp, value); }

    private string _infoTools = "-";
    public string InfoTools { get => _infoTools; private set => Set(ref _infoTools, value); }

    private string _infoToolsFull = "-";
    public string InfoToolsFull { get => _infoToolsFull; private set => Set(ref _infoToolsFull, value); }

    private string _infoToolsTooltip = "";
    public string InfoToolsTooltip { get => _infoToolsTooltip; private set => Set(ref _infoToolsTooltip, value); }

    private string _infoMultimodal = "-";
    public string InfoMultimodal { get => _infoMultimodal; private set => Set(ref _infoMultimodal, value); }

    private string _infoReasoning = "-";
    public string InfoReasoning { get => _infoReasoning; private set => Set(ref _infoReasoning, value); }
    // Новые свойства для имени файла и квантования
    private string _infoFileName = "-";
    public string InfoFileName { get => _infoFileName; private set => Set(ref _infoFileName, value); }

    private string _infoQuantization = "-";
    public string InfoQuantization { get => _infoQuantization; private set => Set(ref _infoQuantization, value); }

    // Тултип строки «Квантование»: источник значения + bpw + распределение типов + заметки (S6).
    private string _infoQuantizationTooltip = "";
    public string InfoQuantizationTooltip { get => _infoQuantizationTooltip; private set => Set(ref _infoQuantizationTooltip, value); }

    // Строка «Файлы шарда»: «1 файл» / «N файлов (shards)» (S6).
    private string _infoShards = "-";
    public string InfoShards { get => _infoShards; private set => Set(ref _infoShards, value); }

    // Предупреждение строки «Файлы шарда»: ⚠ + список отсутствующих шардов (или пусто) (S6).
    private string _infoShardWarning = "";
    public string InfoShardWarning { get => _infoShardWarning; private set => Set(ref _infoShardWarning, value); }

    // Строка «Не учтено»: «не учтено N GiB» из UnknownBytes, только если > 0 (S6, Q5).
    private string _infoUnaccounted = "";
    public string InfoUnaccounted { get => _infoUnaccounted; private set => Set(ref _infoUnaccounted, value); }

    // ==================== Загрузка модели ====================

    private async Task LoadModelAsync(ModelEntry? m)
    {
      int seq = ++_loadSeq;
      _gguf = null;

      if (m == null)
      {
        _currentPath = null;
        InfoArch = InfoBlocks = InfoMaxCtx = InfoFileSize = InfoMtp = InfoTools =
          InfoToolsFull = InfoMultimodal = InfoReasoning = "-";
        InfoQuantization = InfoShards = InfoShardWarning = InfoUnaccounted = "-";
        InfoToolsTooltip = "";
        MtpAvailable = false;
        MtpChecked = false;
        ReasoningAvailable = false;
        ReasoningMode = AppDefaults.DefaultReasoningMode;
        _suppressReasonMsgEdit = true;
        ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;
        _suppressReasonMsgEdit = false;
        JinjaChecked = false;
        JinjaAvailable = false;
        MmprojAvailable = false;
        MmprojChecked = false;
        MmprojFiles.Clear();
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
        if (seq != _loadSeq)
          return;
        StatusText = "Ошибка: " + ex.Message;
        return;
      }

      if (seq != _loadSeq)
        return; // уже выбрана другая модель

      _gguf = g;
      _currentPath = m.FullPath;

      ApplyModelProfile(m, g);

      UpdateFitTargets();
      UpdateLayerEstimate();
      SaveSoon();
    }

    /// <summary>Применить сохранённый профиль модели (или дефолты) ко всем параметрам.</summary>
    private void ApplyModelProfile(ModelEntry m, GgufInfo g)
    {
      var ms = _store.GetOrCreateModel(m.FullPath);

      _suppressSave = true;
      try
      {
        MaxContext = (int)Math.Min(int.MaxValue, Math.Max(32768, g.ContextLength));

        Context = ms.Context > 0
          ? Math.Clamp(ms.Context, 1024, MaxContext)
          : Math.Min(AppDefaults.DefaultContext, MaxContext);

        if (KvOptions.Contains(ms.KvK))
          KvK = ms.KvK;
        else
          KvK = "q8_0";
        if (KvOptions.Contains(ms.KvV))
          KvV = ms.KvV;
        else
          KvV = "q8_0";
        if (FlashOptions.Contains(ms.Flash))
          Flash = ms.Flash;
        else
          Flash = "auto";

        ManualNglMax = Math.Max(1, g.BlockCount + 8);
        if (ManualNgl > ManualNglMax)
          ManualNgl = ManualNglMax; // хранимое значение клампим, но не сбрасываем

        MtpAvailable = g.HasMtp;
        MtpChecked = MtpAvailable && ms.MtpChecked;

        DraftMax = Math.Clamp(ms.DraftMax, 1, 16);
        DraftMin = Math.Clamp(ms.DraftMin, 0, DraftMax);
        DraftP = Math.Clamp(ms.DraftP, 0, 1);
        if (KvOptions.Contains(ms.DraftK))
          DraftK = ms.DraftK;
        else
          DraftK = "q8_0";
        if (KvOptions.Contains(ms.DraftV))
          DraftV = ms.DraftV;
        else
          DraftV = "q8_0";

        ReasoningAvailable = g.HasReasoning;
        ReasoningMode = ReasoningModeOptions.Contains(ms.ReasoningMode)
          ? ms.ReasoningMode
          : AppDefaults.DefaultReasoningMode;
        ReasonBudget = Math.Clamp(ms.ReasonBudget, 0, 1_000_000);
        _suppressReasonMsgEdit = true;
        ReasonBudgetMessage = string.IsNullOrWhiteSpace(ms.ReasonBudgetMessage)
          ? AppDefaults.DefaultReasonBudgetMessage
          : ms.ReasonBudgetMessage;
        _suppressReasonMsgEdit = false;

        // --jinja: авто по вердикту сканера; ручной выбор юзера имеет приоритет
        _suppressJinjaEdit = true;
        JinjaChecked = ms.JinjaEdited ? ms.UseJinja : g.ToolSupport == ToolSupportKind.Yes;
        _suppressJinjaEdit = false;

        // Строка --jinja видна, только если в GGUF вообще есть chat-шаблон
        JinjaAvailable = g.HasChatTemplate;

        // Подробный вердикт по инструментам уводим во всплывашку, в строке — только да/нет/?
        InfoTools = g.ToolSupport switch
        {
          ToolSupportKind.Yes => "да",
          ToolSupportKind.No => "нет",
          _ => "?"
        };
        InfoToolsFull = g.ToolSupport switch
        {
          ToolSupportKind.Yes => "да — " + g.ToolEvidence,
          ToolSupportKind.No => "нет — " + g.ToolEvidence,
          _ => "неизвестно — " + g.ToolEvidence
        };
        InfoToolsTooltip = InfoToolsFull + "\n\n" + ToolTips.ToolsDetect;

        BuildMmprojList(m, ms);

        // Алиас: не приводим к нижнему регистру; правленный вручную не перегенерируем
        _suppressAliasEdit = true;
        AliasText = (ms.AliasEdited && !string.IsNullOrWhiteSpace(ms.Alias))
          ? ms.Alias
          : AliasBuilder.MakeAlias(m.FileName);
        _suppressAliasEdit = false;

        ApplyInfoSection(g, m.FileName);
      }
      finally
      {
        _suppressSave = false;
      }
    }

    // ==================== Инфо-строки модели (S6) ====================

    /// <summary>
    /// Заполняет инфо-строки правой панели: архитектура, блоки, контекст, размер, MTP,
    /// мультимодальность, рассуждения, имя файла, квантование модели и статус шардов.
    /// Выделяет отдельным методом, чтобы тесты могли подставить GGUF через _gguf/_currentPath
    /// и проверить вывод без парсинга реального файла.
    /// </summary>
    private void ApplyInfoSection(GgufInfo g, string fileName)
    {
      InfoArch = g.Arch;
      InfoBlocks = g.BlockCount.ToString();
      InfoMaxCtx = g.ContextLength.ToString();
      InfoFileSize = $"{g.FileSize / GiB:F2} GiB";
      InfoMtp = FormatInfoMtp(g);
      InfoMultimodal = MmprojAvailable ? "да" : "нет";
      InfoReasoning = g.HasReasoning ? "да" : "нет";

      InfoFileName = fileName;
      ApplyQuantizationInfo(g, fileName);
      ApplyShardInfo(g);
      ApplyUnaccountedInfo(g);
    }

    /// <summary>
    /// Хук для регрессионных тестов (S6): подставляет GGUF через _gguf/_currentPath и переcomputes
    /// инфо-строки, чтобы проверить вывод VM без парсинга реального GGUF-файла.
    /// </summary>
    internal void RefreshModelInfoForTest(GgufInfo g, string fileName)
    {
      _gguf = g;
      _currentPath = "Models/" + fileName;
      ApplyInfoSection(g, fileName);
    }

    /// <summary>Квантование модели из Core (S5): метка в строку, детали — в тултип (S6).</summary>
    private void ApplyQuantizationInfo(GgufInfo g, string fileName)
    {
      var q = QuantizationAnalyzer.Analyze(g, fileName);

      InfoQuantization = q.Label;

      var notes = new System.Text.StringBuilder(ToolTips.QuantizationRow);
      notes.Append("\n\n");
      notes.Append(SourceLabel(q.Source));
      if (q.Source != QuantizationSource.NoData)
        notes.Append(": bpw ").Append(q.Bpw.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
      notes.Append('\n');

      // Распределение типов с долями (топ-4 по байтам).
      if (q.TypeShares.Count > 0)
      {
        notes.Append("распределение типов: ");
        notes.Append(string.Join(", ", q.TypeShares.Select(s =>
          $"{s.TypeName} {s.Share * 100:F1}%")));
        notes.Append('\n');
      }

      // Все заметки из Core.
      foreach (var n in q.Notes)
        notes.Append(n).Append('\n');

      InfoQuantizationTooltip = notes.ToString();
    }

    private static string SourceLabel(QuantizationSource source) => source switch
    {
      QuantizationSource.Metadata => "из GGUF (general.file_type)",
      QuantizationSource.Guessed => "оценка по весам (body-тензоры)",
      _ => "нет данных",
    };

    /// <summary>Строка «Файлы шарда»: одиночная модель / набор / предупреждение о нехватке (S6).
    /// Статус набора — из GGUF (SplitStatus/SplitCount/MissingShardPaths), UI только форматирует.
    /// </summary>
    private void ApplyShardInfo(GgufInfo g)
    {
      InfoShards = g.SplitStatus == SplitStatus.NotApplicable ? "1 файл" : PluralFiles(g.SplitCount, "файлов");

      var missing = g.MissingShardPaths;
      InfoShardWarning = missing.Count > 0
        ? "⚠ не хватает шардов: " + string.Join(", ", missing)
        : "";
    }

    private static string PluralFiles(int n, string genitive) => n switch
    {
      1 => $"{n} файл",
      >= 2 and <= 4 => $"{n} {genitive} (shards)",
      _ => $"{n} {genitive} (shards)",
    };

    /// <summary>«Учтено N GiB» из UnknownBytes — только если байты реально есть (S6, Q5).</summary>
    private void ApplyUnaccountedInfo(GgufInfo g)
    {
      InfoUnaccounted = g.UnknownBytes > 0 ? $"не учтено {g.UnknownBytes / GiB:F2} GiB" : "";
    }

    // Строка MTP в инфо: да/нет + тип и число доп. токенов, если удалось распознать.
    private static string FormatInfoMtp(GgufInfo g)
    {
      if (!g.HasMtp)
        return "нет";
      string size = $"~{g.MtpSize / MiB:F0} MiB";
      if (g.MtpKind.Length == 0)
        return $"да, {size}";
      string kind = g.MtpKind == "extra" ? "доп. блоки" : g.MtpKind;   // nextn | mtp
      return g.MtpTokens > 0
        ? $"да — {kind}, +{g.MtpTokens} {TokensWord(g.MtpTokens)} за шаг, {size}"
        : $"да — {kind}, {size}";
    }

    private static string TokensWord(int n)
    {
      int d10 = n % 10, d100 = n % 100;
      if (d10 == 1 && d100 != 11) return "токен";
      if (d10 is >= 2 and <= 4 && (d100 < 10 || d100 >= 20)) return "токена";
      return "токенов";
    }

    // ==================== Мультимодальность ====================

    public ObservableCollection<MmprojEntry> MmprojFiles { get; } = new();

    private bool _mmprojAvailable;
    public bool MmprojAvailable { get => _mmprojAvailable; private set => Set(ref _mmprojAvailable, value); }

    private MmprojEntry? _selectedMmproj;
    public MmprojEntry? SelectedMmproj
    {
      get => _selectedMmproj;
      set { if (Set(ref _selectedMmproj, value)) { UpdateMmprojInfo(); SaveSoon(); } }
    }

    private bool _mmprojChecked;
    public bool MmprojChecked
    {
      get => _mmprojChecked;
      set { if (Set(ref _mmprojChecked, value)) SaveSoon(); }
    }

    private string _mmprojInfoText = "";
    public string MmprojInfoText { get => _mmprojInfoText; private set => Set(ref _mmprojInfoText, value); }

    private void BuildMmprojList(ModelEntry m, ModelSettings ms)
    {
      MmprojFiles.Clear();

      // mmproj-файлы ищем только в той же папке, где лежит модель
      foreach (var p in m.LocalMmproj)
      {
        long size = 0;
        try
        { size = new FileInfo(p).Length; }
        catch { }
        MmprojFiles.Add(new MmprojEntry { FullPath = p, DisplayName = Path.GetFileName(p), FileSize = size });
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
  }
}
