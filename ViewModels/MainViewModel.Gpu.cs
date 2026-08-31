using LlmScanHelper.Models;

namespace LlmScanHelper.ViewModels
{
  /// <summary>
  /// Частичный класс: GPU layout (режим, устройства, резервы, разкладка) и опрос устройств.
  /// </summary>
  public sealed partial class MainViewModel
  {
    // ==================== GPU layout ====================

    private int _modeIndex = 0;
    public int ModeIndex
    {
      get => _modeIndex;
      set
      {
        if (Set(ref _modeIndex, value))
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

    private string _devicesText = AppDefaults.DefaultDevices;
    public string DevicesText
    {
      get => _devicesText;
      set { if (Set(ref _devicesText, value)) { UpdateFitTargets(); SaveSoon(); } }
    }

    private string _splitMode = "layer";
    public string SplitMode { get => _splitMode; set { if (Set(ref _splitMode, value)) SaveSoon(); } }

    private double _reserveV100GiB = AppDefaults.SafeReserveV100GiB;
    public double ReserveV100GiB
    {
      get => _reserveV100GiB;
      set { if (Set(ref _reserveV100GiB, value)) { UpdateFitTargets(); SaveSoon(); } }
    }

    private double _reserveRtxGiB = AppDefaults.SafeReserveRtxGiB;
    public double ReserveRtxGiB
    {
      get => _reserveRtxGiB;
      set
      {
        value = Math.Max(value, AppDefaults.MinDesktopReserveGiB); // ниже 2 GiB UI не предлагает
        if (Set(ref _reserveRtxGiB, value))
        { UpdateFitTargets(); SaveSoon(); }
      }
    }

    private string _fitTargetsText = "fit-target: -";
    public string FitTargetsText { get => _fitTargetsText; private set => Set(ref _fitTargetsText, value); }

    private int _manualNglMax = 512;
    public int ManualNglMax
    {
      get => _manualNglMax;
      private set { if (Set(ref _manualNglMax, value)) OnPropertyChanged(nameof(ManualNgl)); }
    }

    private int _manualNgl = 0;
    public int ManualNgl
    {
      get => _manualNgl;
      set { value = Math.Clamp(value, 0, ManualNglMax); if (Set(ref _manualNgl, value)) SaveSoon(); }
    }

    private int _split0 = 3;
    public int Split0 { get => _split0; set { if (Set(ref _split0, value)) SaveSoon(); } }

    private int _split1 = 1;
    public int Split1 { get => _split1; set { if (Set(ref _split1, value)) SaveSoon(); } }

    // ==================== GPU ====================

    private string _gpuSummaryText = "GPU: не опрошены";
    public string GpuSummaryText { get => _gpuSummaryText; private set => Set(ref _gpuSummaryText, value); }

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
      if (d?.Id.Equals(FindV100Device(), StringComparison.OrdinalIgnoreCase) == false)
        return d.Id;
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
        if (info.IsV100())
          return ReserveV100GiB;
        if (info.IsDesktopRtx())
          return Math.Max(ReserveRtxGiB, AppDefaults.MinDesktopReserveGiB);
      }
      // Fallback: первая карта — compute V100, вторая — desktop RTX
      return position == 0 ? ReserveV100GiB : Math.Max(ReserveRtxGiB, AppDefaults.MinDesktopReserveGiB);
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
  }
}
