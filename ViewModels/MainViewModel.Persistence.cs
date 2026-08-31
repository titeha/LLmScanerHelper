using System.Windows.Threading;

using LlmScanHelper.Models.Settings;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Частичный класс: сохранение и загрузка параметров (глобальные + по-модельные профили).
  /// </summary>
  public sealed partial class MainViewModel
  {
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
      if (_suppressSave)
        return;
      SaveTimer.Stop();
      SaveTimer.Start();
    }

    /// <summary>Немедленно сбросить отложенные правки в хранилище (сохранить сейчас).</summary>
    public void FlushPendingSave()
    {
      _saveTimer?.Stop();
      if (_suppressSave)
        return;
      if (_currentPath != null || Models.Count > 0)
        SaveNow();
    }

    /// <summary>Синхронное сохранение: глобальные параметры + профиль текущей модели.</summary>
    public void SaveNow()
    {
      var s = _store.Settings;
      s.Catalogs = new List<string>(Catalogs);
      s.SelectedCatalogIndex = Math.Max(0, Catalogs.IndexOf(SelectedCatalog));
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
        if (SplitModeOptions.Contains(gp.SplitMode))
          SplitMode = gp.SplitMode;
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

        SelectedTabIndex = Math.Clamp(_store.Settings.SelectedTabIndex, 0, 2);
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
      ms.ReasonBudgetMessage = ReasonBudgetMessage;
      ms.UseJinja = JinjaChecked;   // ms.JinjaEdited — только из сеттера (ручная правка)
      ms.MmprojPath = SelectedMmproj?.FullPath ?? "";
      ms.MmprojEnabled = MmprojAvailable && MmprojChecked;
    }
  }
}
