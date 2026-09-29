using System.IO;
using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>Источник значения квантования.</summary>
  public enum QuantizationSource
  {
    NoData = 0,
    Metadata = 1,
    Guessed = 2
  }

  /// <summary>Доля одного ggml-типа по байтам (для распределения).</summary>
  public readonly record struct TypeShare(uint TypeId, string TypeName, long Bytes, double Share);

  /// <summary>Результат определения квантования модели (S5).</summary>
  public sealed class QuantizationInfo
  {
    public string Label = "—";
    public QuantizationSource Source = QuantizationSource.NoData;
    public double Bpw;                        // Σ байт × 8 / Σ элементов по всем тензорам
    public IReadOnlyList<TypeShare> TypeShares = Array.Empty<TypeShare>(); // топ-4 по байтам
    public long BodyBytes;                    // байты ≥2D body-тензоров (без эмбеддингов/выхода)
    public string BodyTopType = "";           // доминирующий ≥2D body-тип (для сверки/тултипа)
    public double BodyTopShare;               // его доля в BodyBytes
    public string NameToken = "";             // токен квантования из имени файла
    public bool NameMatches;                  // нормализованный токен совпал с Label
    public IReadOnlyList<string> Notes = Array.Empty<string>();
  }

  /// <summary>
  /// Определяет квантование модели по агрегату (S4). Значение — из <c>general.file_type</c>;
  /// догадка по байтам body-тензоров — только при отсутствии ключа. Метаданные догадкой не перебиваются.
  /// </summary>
  public static class QuantizationAnalyzer
  {
    // Границы допуска сверки bpw с эталоном метки (§3 задачи): ±0.15.
    private const double BpwTolerance = 0.15;

    private static readonly Regex ShardSuffixRx = new(@"-\d{5}-of-\d{5}$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Разделители вендорских имён. Знак '_' НЕ разделитель — он внутри токенов (Q6_K, IQ4_XS).
    private static readonly char[] TokenSeparators = { '-', '.', ' ', '\t' };

    // Квантоподобный токен: префикс типа + цифра + хвост, с вендорским суффиксом _XL/_L.
    private static readonly Regex QuantTokenRx = new(
      @"^(?:IQ|TQ|Q|MXFP|NVFP|BF|F)\d[A-Za-z0-9_]*(?:_XL|_L)?$",
      RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Набор меток из S1 (единственный источник — LlamaFtype).
    private static readonly HashSet<string> LabelSet = BuildLabelSet();

    // Эталонные bpw метки: базовые значения — gguf-spec-context §4 (теоретический bpw типа);
    // верхняя граница расширена для вариантов _S/_M/_L по замерам §7 (часть весов — в более точном типе).
    private static readonly Dictionary<string, (double Low, double High)> BpwRef = new(StringComparer.Ordinal)
    {
      ["F32"] = (32.0, 32.0),
      ["F16"] = (16.0, 16.0),
      ["BF16"] = (16.0, 16.0),
      ["Q4_0"] = (4.5, 4.5),
      ["Q4_1"] = (5.0, 5.0),
      ["Q5_0"] = (5.5, 5.5),
      ["Q5_1"] = (6.0, 6.0),
      ["Q8_0"] = (8.5, 8.5),
      ["Q2_K"] = (2.625, 2.625),
      ["Q3_K_S"] = (3.4375, 3.6),
      ["Q3_K_M"] = (3.4375, 3.8),
      ["Q3_K_L"] = (3.4375, 4.0),
      ["Q4_K_S"] = (4.5, 4.7),
      ["Q4_K_M"] = (4.5, 5.0),   // §7: 4.88–4.94
      ["Q5_K_S"] = (5.5, 5.7),
      ["Q5_K_M"] = (5.5, 5.9),
      ["Q6_K"] = (6.5625, 6.5625),
      ["IQ1_S"] = (1.5625, 1.5625),
      ["IQ4_NL"] = (4.5, 4.5),
      ["IQ4_XS"] = (4.25, 4.25),
      ["IQ1_M"] = (1.75, 1.75),
      ["MXFP4_MOE"] = (4.25, 4.25),
      ["NVFP4"] = (4.5, 4.5),
      ["Q1_0"] = (1.125, 1.125),
      ["Q2_0"] = (2.25, 2.25),
    };

    private const uint GgmlTypeMxfp4 = 39;

    /// <summary>
    /// Анализ квантования агрегата. <paramref name="fileName"/> — имя файла или stem (S3 `ModelEntry.FileName`).
    /// </summary>
    public static QuantizationInfo Analyze(GgufInfo g, string fileName)
    {
      var info = new QuantizationInfo();
      var notes = new List<string>();

      long totalBytes = 0;
      long totalElements = 0;
      var byType = new Dictionary<uint, long>();
      var bodyByType = new Dictionary<uint, long>();
      long bodyBytes = 0;

      foreach (var t in g.Tensors)
      {
        totalBytes += t.Bytes;
        totalElements += t.Dims[0] * t.Dims[1] * t.Dims[2] * t.Dims[3];
        byType[t.TypeId] = byType.GetValueOrDefault(t.TypeId) + t.Bytes;
        if (IsBodyTensor(t))
        {
          bodyBytes += t.Bytes;
          bodyByType[t.TypeId] = bodyByType.GetValueOrDefault(t.TypeId) + t.Bytes;
        }
      }

      info.BodyBytes = bodyBytes;
      info.Bpw = totalElements > 0 ? totalBytes * 8.0 / totalElements : 0.0;

      info.TypeShares = byType
        .Where(kv => kv.Value > 0)
        .OrderByDescending(kv => kv.Value)
        .Take(4)
        .Select(kv => new TypeShare(kv.Key, TypeName(kv.Key), kv.Value,
          totalBytes > 0 ? (double)kv.Value / totalBytes : 0))
        .ToList();

      var bodyTop = bodyByType.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();
      uint? bodyTopId = bodyTop.Value > 0 ? bodyTop.Key : null;
      var allTop = byType.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).FirstOrDefault();

      if (bodyTopId != null)
      {
        info.BodyTopType = TypeName(bodyTopId.Value);
        info.BodyTopShare = bodyBytes > 0 ? (double)bodyByType[bodyTopId.Value] / bodyBytes : 0;
      }

      // ---- 1. Приоритет значения: general.file_type → метаданные; иначе доминанта body-тензоров ----
      string label;
      if (g.FileType >= 0)
      {
        info.Source = QuantizationSource.Metadata;
        notes.Add("источник значения: general.file_type");
        if (LlamaFtype.TryGetName((uint)g.FileType, out var fromMeta))
          label = fromMeta;
        else
        {
          label = $"file_type {g.FileType}";
          notes.Add($"неизвестная метка general.file_type = {g.FileType}");
        }
      }
      else if (bodyTopId != null)
      {
        info.Source = QuantizationSource.Guessed;
        notes.Add("источник значения: оценка по байтам body-тензоров (general.file_type отсутствует)");
        label = LabelForType(bodyTopId.Value);
      }
      else
      {
        info.Source = QuantizationSource.NoData;
        label = "—";
        notes.Add("нет данных о квантовании");
      }
      info.Label = label;

      // ---- 2. Сверка bpw с эталоном метки ----
      if (info.Source != QuantizationSource.NoData && BpwRef.TryGetValue(label, out var reference))
      {
        if (info.Bpw < reference.Low - BpwTolerance)
          notes.Add($"метка не подтверждается весами: факты — {Facts(bodyTopId, bodyByType, bodyBytes, allTop, totalBytes)}");
        else if (info.Bpw > reference.High + BpwTolerance)
          notes.Add("часть весов в более точном типе (XL/повышенная точность)");
        else
          notes.Add("bpw соответствует метке");
      }

      // ---- 3. Распределение: метка метаданных расходится с фактическими body-весами ----
      if (info.Source == QuantizationSource.Metadata && bodyTopId != null &&
          LabelForType(bodyTopId.Value) != label)
      {
        if (bodyTopId.Value == GgmlTypeMxfp4)
        {
          long mxfp4 = byType.GetValueOrDefault(GgmlTypeMxfp4);
          notes.Add($"метрика file_type описывает не-экспертные веса, эксперты: mxfp4 {Gib(mxfp4):F2} GiB");
        }
        else
        {
          long b = bodyByType[bodyTopId.Value];
          double share = bodyBytes > 0 ? (double)b / bodyBytes : 0;
          notes.Add($"веса доминирует {TypeName(bodyTopId.Value)} ({share * 100:F1}%, {Gib(b):F2} GiB)");
        }
      }

      // ---- 4. Токен квантования из имени файла ----
      info.NameToken = ExtractNameToken(fileName);
      if (info.NameToken.Length > 0)
      {
        info.NameMatches = info.Source != QuantizationSource.NoData &&
                   string.Equals(NormalizeToken(info.NameToken), label, StringComparison.Ordinal);
        if (!info.NameMatches)
          notes.Add($"в имени: {info.NameToken}, в GGUF: {label}");
      }

      // ---- 5. Из S2: нераспознанные типы / неотнесённые к слоям байты ----
      if (g.UnknownTypeTensors > 0)
        notes.Add($"нераспознанные типы: {g.UnknownTypeTensors} тензоров");
      if (g.UnknownBytes > 0)
        notes.Add($"не отнесено к слоям/эмбеддингам: {g.UnknownTensors} тензоров, {Gib(g.UnknownBytes):F2} GiB");

      info.Notes = notes;
      return info;
    }

    /// <summary>≥2D (ne[1] > 1) и не эмбеддинг/выход: token_embd*, per_layer_token_embd*, output.weight, cls*.</summary>
    private static bool IsBodyTensor(GgufTensorInfo t)
    {
      if (t.Dims.Length < 2 || t.Dims[1] <= 1) return false;
      string n = t.Name;
      if (n.StartsWith("token_embd", StringComparison.OrdinalIgnoreCase)) return false;
      if (n.StartsWith("per_layer_token_embd", StringComparison.OrdinalIgnoreCase)) return false;
      if (n.StartsWith("cls", StringComparison.OrdinalIgnoreCase)) return false;
      if (n.Equals("output.weight", StringComparison.OrdinalIgnoreCase)) return false;
      return true;
    }

    private static string Facts(uint? bodyTopId, Dictionary<uint, long> bodyByType, long bodyBytes,
      KeyValuePair<uint, long> allTop, long totalBytes)
    {
      if (bodyTopId != null)
      {
        long b = bodyByType[bodyTopId.Value];
        double share = bodyBytes > 0 ? (double)b / bodyBytes : 0;
        return $"{share * 100:F1}% {TypeName(bodyTopId.Value)} ({Gib(b):F2} GiB)";
      }
      if (allTop.Value > 0)
      {
        double share = totalBytes > 0 ? (double)allTop.Value / totalBytes : 0;
        return $"{share * 100:F1}% {TypeName(allTop.Key)} ({Gib(allTop.Value):F2} GiB)";
      }
      return "нет тензоров";
    }

    private static string LabelForType(uint ggmlTypeId)
    {
      if (LlamaFtype.TryGetFtypeForGgmlType(ggmlTypeId, out var ftype) &&
          LlamaFtype.TryGetName(ftype, out var label))
        return label;
      return TypeName(ggmlTypeId);
    }

    private static string TypeName(uint id) => GgmlTypes.TryGet(id, out var ti) ? ti.Name : $"тип {id}";

    private static double Gib(long bytes) => bytes / 1024.0 / 1024.0 / 1024.0;

    private static HashSet<string> BuildLabelSet()
    {
      var set = new HashSet<string>(StringComparer.Ordinal);
      for (uint id = 0; id <= 41; id++)
        if (LlamaFtype.TryGetName(id, out var name)) set.Add(name);
      return set;
    }

    /// <summary>Извлекает квантоподобный токен из имени (по разделителям - . и пробел).</summary>
    private static string ExtractNameToken(string fileName)
    {
      if (string.IsNullOrEmpty(fileName)) return "";
      // НЕ Path.GetFileNameWithoutExtension: в именах вида Qwen3.8-... точка не расширение.
      string stem = Path.GetFileName(fileName);
      if (stem.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) stem = stem[..^5];
      stem = ShardSuffixRx.Replace(stem, "");

      string? fallback = null;
      foreach (var raw in stem.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
      {
        if (!QuantTokenRx.IsMatch(raw)) continue;
        if (LabelSet.Contains(NormalizeToken(raw))) return raw;
        fallback ??= raw;
      }
      return fallback ?? "";
    }

    /// <summary>
    /// Нормализация токена: точная метка; иначе срез вендорского _XL/_L, если остаток — метка;
    /// алиас MXFP4 → MXFP4_MOE. Годится и для сравнения «не совпало» (срез суффикса).
    /// </summary>
    private static string NormalizeToken(string token)
    {
      string up = token.ToUpperInvariant();
      if (LabelSet.Contains(up)) return up;
      if (up.EndsWith("_XL", StringComparison.Ordinal) && LabelSet.Contains(up[..^3])) return up[..^3];
      if (up.EndsWith("_L", StringComparison.Ordinal) && LabelSet.Contains(up[..^2])) return up[..^2];
      if (up == "MXFP4") return "MXFP4_MOE";
      if (up.EndsWith("_XL", StringComparison.Ordinal)) return up[..^3];
      if (up.EndsWith("_L", StringComparison.Ordinal)) return up[..^2];
      return up;
    }
  }
}
