namespace LlmScanHelper.Models
{
  /// <summary>
  /// ГРУБАЯ оценка распределения блоков (слоёв) между GPU.
  ///
  /// Памятка из шапки: формула "веса + обычный KV на каждый слой" уже НЕ
  /// точна (RS-cache у recurrent-слоёв, speculative context от MTP,
  /// compute/scratch от -b/-ub и FA). Точную раскладку в AUTO-режиме делает
  /// сам llama.cpp через --fit. Оценка ниже — ориентир "куда что поедет",
  /// а не гарантия.
  /// </summary>
  public static class LayerEstimator
  {
    public sealed class DeviceEstimate
    {
      public string DeviceId = "";
      public string Name = "";
      public bool Known;          // нашли GPU в списке опрошенных
      public double BudgetGiB;    // свободно минус fit-target
      public double WeightsGiB;   // назначено весов блоков
      public double KvGiB;        // назначено KV-кэша
      public int Blocks;          // сколько блоков легло на устройство
    }

    public sealed class EstimateResult
    {
      public List<DeviceEstimate> Devices { get; } = [];
      public int CpuBlocks;
      public double CpuWeightsGiB;
      public double MtpGiB;
    }

    private const double _miB = 1024.0 * 1024.0;
    private const double _giB = 1024.0 * 1024.0 * 1024.0;

    private static double BytesPerElem(string type) => type switch
    {
      "f16" => 2.0,
      "q8_0" => 34.0 / 32.0,   // блок: 32 элемента + 2 байта scale
      "q4_0" => 18.0 / 32.0,   // блок: 32 элемента + 2 байта scale
      _ => 2.0
    };

    public static EstimateResult Estimate(
      GgufInfo g,
      IReadOnlyList<GpuDeviceInfo> gpus,
      IReadOnlyList<string> devices,
      IReadOnlyList<int> fitTargetsMiB,
      long ctx,
      string kvK, string kvV,
      bool useMtp)
    {
      var res = new EstimateResult();

      // KV-кэш на один блок: (K + V), по kvHeads * headDim на каждый токен контекста
      double kvPerLayer = (double)ctx * Math.Max(1, g.KvHeads) * Math.Max(1, g.HeadDim) *
                (BytesPerElem(kvK) + BytesPerElem(kvV));

      int n = Math.Max(1, devices.Count);

      // Бюджеты: свободная VRAM минус fit-target (в том же порядке, что --device)
      var budgets = new double[n];
      var known = new bool[n];
      var freeMiB = new int[n];
      for (int i = 0; i < n; i++)
      {
        string id = i < devices.Count ? devices[i] : $"CUDA{i}";
        var gi = gpus.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        known[i] = gi != null;
        freeMiB[i] = gi?.FreeMiB ?? 0;
        int target = i < fitTargetsMiB.Count ? fitTargetsMiB[i] : 0;
        budgets[i] = known[i] ? Math.Max(0, freeMiB[i] - target) * _miB : 0;
      }

      // Эмбеддинги условно кладём на первую карту (грубое допущение)
      if (known[0])
        budgets[0] = Math.Max(0, budgets[0] - g.EmbdSize);

      var weights = new double[n];
      var kv = new double[n];
      var blocks = new int[n];

      int cursor = 0;
      int cpuBlocks = 0;
      double cpuWeights = 0;

      for (int b = 0; b < g.BlockCount && b < g.LayerSize.Length; b++)
      {
        // Если текущая карта не опрошена (нет данных о VRAM) — сразу считаем блок на CPU
        double w = g.LayerSize[b];
        double cost = w + kvPerLayer;

        while (cursor < n - 1 && (!known[cursor] || cost > budgets[cursor]))
          cursor++;

        if (known[cursor] && cost <= budgets[cursor])
        {
          budgets[cursor] -= cost;
          weights[cursor] += w;
          kv[cursor] += kvPerLayer;
          blocks[cursor]++;
        }
        else
        {
          cpuBlocks++;
          cpuWeights += w;
        }
      }

      for (int i = 0; i < n; i++)
      {
        string id = i < devices.Count ? devices[i] : $"CUDA{i}";
        res.Devices.Add(new DeviceEstimate
        {
          DeviceId = id,
          Name = gpus.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))?.Name ?? "(не опрошен)",
          Known = known[i],
          BudgetGiB = known[i] ? (freeMiB[i] - (i < fitTargetsMiB.Count ? fitTargetsMiB[i] : 0)) / 1024.0 : 0,
          WeightsGiB = weights[i] / _giB,
          KvGiB = kv[i] / _giB,
          Blocks = blocks[i]
        });
      }

      res.CpuBlocks = cpuBlocks;
      res.CpuWeightsGiB = cpuWeights / _giB;
      res.MtpGiB = useMtp ? g.MtpSize / _giB : 0;

      return res;
    }
  }
}
