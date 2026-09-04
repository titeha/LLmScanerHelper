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

        InfoArch = g.Arch;
        InfoBlocks = g.BlockCount.ToString();
        InfoMaxCtx = g.ContextLength.ToString();
        InfoFileSize = $"{g.FileSize / GiB:F2} GiB";
        InfoMtp = FormatInfoMtp(g);
        InfoMultimodal = MmprojAvailable ? "да" : "нет";
        InfoReasoning = g.HasReasoning ? "да" : "нет";

        StatusText = $"Загружено: {m.FileName}" +
               $" | MTP: {(g.HasMtp ? "да" : "нет")}" +
               $" | reasoning: {(g.HasReasoning ? "да" : "нет")}" +
               $" | tools: {(g.ToolSupport == ToolSupportKind.Yes ? "да" : g.ToolSupport == ToolSupportKind.No ? "нет" : "?")}" +
               (MmprojAvailable ? " | mmproj: да" : "");
      }
      finally
      {
        _suppressSave = false;
      }
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
