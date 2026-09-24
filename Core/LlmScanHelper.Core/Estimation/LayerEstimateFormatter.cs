using System.Text;

namespace LlmScanHelper.Models.Estimation;

/// <summary>
/// Форматирование грубой оценки распределения слоёв (текст для UI).
/// Перенесено из MainViewModel.UpdateLayerEstimate (UI.Common) — только текст.
/// </summary>
public static class LayerEstimateFormatter
{
  public static string Format(LayerEstimator.EstimateResult est)
  {
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
    return sb.ToString();
  }
}
