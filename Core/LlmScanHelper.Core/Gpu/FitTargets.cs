namespace LlmScanHelper.Models.Gpu;

/// <summary>
/// Математика --fit-target: разбор устройств, перевод резервов GiB→MiB.
/// Перенесено из MainViewModel (UI.Common) — чистая доменная логика.
/// </summary>
public static class FitTargets
{
  // Тело MainViewModel.SelectedDevices()
  public static List<string> ParseDevices(string devicesText) => (devicesText ?? "")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Where(x => x.Length > 0)
    .ToList();

  // Тело MainViewModel.GiBToMiB()
  public static int GiBToMiB(double gib) => (int)Math.Round(gib * 1024.0, MidpointRounding.AwayFromZero);

  // Тело MainViewModel.ReserveForDeviceGiB() — без обращения к полям VM.
  public static double ReserveForDeviceGiB(
    IReadOnlyList<GpuDeviceInfo> gpus, string deviceId, int position,
    double reserveV100GiB, double reserveRtxGiB)
  {
    var info = gpus.FirstOrDefault(x => x.Id.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
    if (info != null)
    {
      if (info.IsV100())
        return reserveV100GiB;
      if (info.IsDesktopRtx())
        return Math.Max(reserveRtxGiB, AppDefaults.MinDesktopReserveGiB);
    }
    // Fallback: первая карта — compute V100, вторая — desktop RTX
    return position == 0 ? reserveV100GiB : Math.Max(reserveRtxGiB, AppDefaults.MinDesktopReserveGiB);
  }

  // Тело MainViewModel.CurrentFitTargetsMiB()
  public static List<int> CurrentFitTargetsMiB(
    IReadOnlyList<GpuDeviceInfo> gpus, string devicesText, double reserveV100GiB, double reserveRtxGiB)
  {
    var devs = ParseDevices(devicesText);
    var result = new List<int>();
    for (int i = 0; i < devs.Count; i++)
      result.Add(GiBToMiB(ReserveForDeviceGiB(gpus, devs[i], i, reserveV100GiB, reserveRtxGiB)));
    return result;
  }
}
