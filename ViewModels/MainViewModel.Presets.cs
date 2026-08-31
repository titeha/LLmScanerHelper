using LlmScanHelper.Models;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Частичный класс: пресеты параметров (готовые профили запуска).
  /// </summary>
  public sealed partial class MainViewModel
  {
    // ==================== Пресеты ====================

    private enum PresetDevices { Combined, V100Only, RtxOnly }

    private void ApplyBaseAuto(double rtxReserveGiB, PresetDevices devices)
    {
      _suppressSave = true;
      try
      {
        Context = Math.Min(32768, MaxContext);
        KvK = "q8_0";
        KvV = "q8_0";
        Flash = "auto";
        ModeIndex = 0;
        DevicesText = devices switch
        {
          PresetDevices.V100Only => FindV100Device(),
          PresetDevices.RtxOnly => FindDesktopRtxDevice(),
          _ => CombinedDevices()
        };
        ReserveV100GiB = AppDefaults.SafeReserveV100GiB;
        ReserveRtxGiB = Math.Max(rtxReserveGiB, AppDefaults.MinDesktopReserveGiB);
        Batch = 2048;
        UBatch = 512;
        Slots = 1;
        Threads = 0;
        ThreadsBatch = 0;
        CacheReuse = 256;
        SsePing = 15;
        Timeout = 7200;
        if (MtpAvailable)
          MtpChecked = false;
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

    private void ApplyPresetRtxOnly() => ApplyBaseAuto(AppDefaults.SafeReserveRtxGiB, PresetDevices.RtxOnly);

    private void ApplyPresetQ8()
    {
      ApplyBaseAuto(AppDefaults.SafeReserveRtxGiB, PresetDevices.Combined);
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
      ApplyBaseAuto(AppDefaults.SafeReserveRtxGiB, PresetDevices.Combined);
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
  }
}
