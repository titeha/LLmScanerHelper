using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>Вердикт по tool-calls (агентная работа) на основе GGUF-метаданных.</summary>
  public enum ToolSupportKind
  {
    Unknown = 0,   // шаблона нет в GGUF — llama-server подберёт встроенный
    No = 1,        // chat-шаблон есть, но работы с tools в нём нет
    Yes = 2        // chat-шаблон содержит обработку tools/tool_calls
  }

  // ========================== Парсер GGUF ==========================
  // Тензорные offsets в GGUF относительны к началу data-section, поэтому
  // для последнего тензора учитываем выровненный dataStart. Это исправляет
  // старое завышение размера последнего тензора (особенно неприятно, когда
  // последним оказывался MTP/nextn блок).
  public sealed class GgufInfo
  {
    public string Arch = "llama";
    public int BlockCount;
    public long ContextLength, KvHeads, HeadDim, EmbdSize, MtpSize;
    public long FileType;                 // general.file_type (если есть в meta)
    public bool HasReasoning;

    // Tool-calls (агентная работа): chat-шаблон + спец-токены словаря
    public bool HasChatTemplate;
    public ToolSupportKind ToolSupport;
    public string ToolEvidence = "";
    public long[] LayerSize = Array.Empty<long>();
    public long FileSize;

    public bool HasMtp => MtpSize > 0;

    // LLAMA_FTYPE_MOSTLY_Q8_0 == 5 (см. llama.h LLAMA_FTYPE_*).
    // Плюс проверка имени файла: "...-Q8_0.gguf" / "...Q8.gguf".
    public bool IsQ8Quant(string fileName) => FileType == 5 || DetectQ8FromName(fileName);

    public static bool DetectQ8FromName(string fileName)
    {
      // Совпадения: Q8_0, Q8-0, Q8.0, Q8 ; НЕ совпадает IQ8_XXS (там перед Q8 буква I).
      return Regex.IsMatch(fileName ?? string.Empty, @"(^|[^A-Za-z0-9])Q8([._\-]?0)?([^A-Za-z0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static GgufInfo Read(string path)
    {
      var g = new GgufInfo();
      long fileSize = new FileInfo(path).Length;
      g.FileSize = fileSize;

      using (var fs = File.OpenRead(path))
      using (var r = new BinaryReader(fs))
      {
        if (r.ReadUInt32() != 0x46554747) throw new Exception("это не GGUF");
        r.ReadUInt32(); // version
        ulong tensorCount = r.ReadUInt64();
        ulong kvCount = r.ReadUInt64();

        if (tensorCount > BenchDefaults.MaxTensorCount)
          throw new Exception($"Слишком много тензоров: {tensorCount}");
        if (kvCount > BenchDefaults.MaxKvCount)
          throw new Exception($"Слишком много KV-пар: {kvCount}");

        var meta = new Dictionary<string, object>();
        for (ulong i = 0; i < kvCount; i++)
          meta[RStr(r)] = RVal(r, r.ReadUInt32());

        if (meta.TryGetValue("general.architecture", out var a) && a is string s)
          g.Arch = s;

        g.BlockCount = (int)Num(meta, g.Arch + ".block_count");
        g.ContextLength = (long)Num(meta, g.Arch + ".context_length", 32768);
        g.FileType = (long)Num(meta, "general.file_type", 0);

        long heads = (long)Num(meta, g.Arch + ".head_count", 1);
        g.KvHeads = (long)Num(meta, g.Arch + ".head_count_kv", heads);
        long emb = (long)Num(meta, g.Arch + ".embedding_length", 4096);
        g.HeadDim = (long)Num(meta, g.Arch + ".attention.key_length", heads > 0 ? emb / heads : 128);

        if (meta.TryGetValue("tokenizer.chat_template", out var ct) && ct is string cts)
          g.HasReasoning = cts.Contains("enable_thinking", StringComparison.OrdinalIgnoreCase) ||
                   cts.Contains("reasoning", StringComparison.OrdinalIgnoreCase);

        if (!g.HasReasoning && meta.TryGetValue("general.tags", out var tg) && tg is object[] tga)
          g.HasReasoning = tga.Any(t => t is string ts &&
            ts.IndexOf("reasoning", StringComparison.OrdinalIgnoreCase) >= 0);

        // ---- Tool-calls: шаблоны tokenizer.chat_template* + спец-токены словаря ----
        // Родной шаблон GGUF — главный прокси «умеет ли модель функции»: если в нём
        // есть tools/tool_calls/role==tool, llama-server --jinja сможет и отдавать
        // инструменты модели, и парсить её ответы в OpenAI-совместимые tool_calls.
        DetectToolSupport(g, meta);

        var names = new List<string>();
        var offs = new List<long>();

        for (ulong i = 0; i < tensorCount; i++)
        {
          names.Add(RStr(r));
          uint nd = r.ReadUInt32();
          for (int d = 0; d < nd; d++) r.ReadUInt64();
          r.ReadUInt32(); // type
          ulong offset = r.ReadUInt64();
          if (offset > long.MaxValue)
            throw new Exception($"Смещение тензора слишком велико для long: {offset}");
          offs.Add((long)offset);
        }

        long alignment = (long)Num(meta, "general.alignment", 32);
        if (alignment <= 0) alignment = 32;
        long dataStart = Align(fs.Position, alignment);

        var order = Enumerable.Range(0, names.Count).OrderBy(i => offs[i]).ToList();
        g.LayerSize = new long[Math.Max(0, g.BlockCount)];

        for (int j = 0; j < order.Count; j++)
        {
          int i = order[j];
          long absStart = dataStart + offs[i];
          if (absStart < 0 || absStart > fileSize)
            throw new Exception($"Смещение тензора {names[i]} выходит за файл: {absStart}/{fileSize}");

          long absEnd = (j + 1 < order.Count)
            ? dataStart + offs[order[j + 1]]
            : fileSize;

          long size = absEnd - absStart;
          if (size < 0)
            throw new Exception($"Отрицательный размер тензора для {names[i]}");

          string name = names[i];
          bool explicitMtp = name.IndexOf(".mtp.", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     name.IndexOf("nextn", StringComparison.OrdinalIgnoreCase) >= 0;

          if (explicitMtp)
          {
            g.MtpSize += size;
          }
          else if (name.StartsWith("token_embd", StringComparison.OrdinalIgnoreCase))
          {
            g.EmbdSize += size;
          }
          else
          {
            var m = Regex.Match(name, @"^blk\.(\d+)\.");
            if (m.Success && int.TryParse(m.Groups[1].Value, out int li))
            {
              if (li >= 0 && li < g.BlockCount)
                g.LayerSize[li] += size;
              else
                g.MtpSize += size; // дополнительный blk.N за block_count
            }
          }
        }
      }

      return g;
    }

    /// <summary>
    /// Эвристика tool-calls: (1) все ключи tokenizer.chat_template* сканируются на
    /// jinja-обработку инструментов; (2) словарь спец-токенов — на <tool_call>-подобные.
    /// Это прокси, не гарантия: реальное поведение зависит от сервера и обучения модели.
    /// </summary>
    private static void DetectToolSupport(GgufInfo g, Dictionary<string, object> meta)
    {
      var tpl = new StringBuilder();
      foreach (var kv in meta)
        if (kv.Key.StartsWith("tokenizer.chat_template", StringComparison.OrdinalIgnoreCase) &&
          kv.Value is string tv && tv.Length > 0)
        {
          g.HasChatTemplate = true;                 // т.е. tokenizer.chat_template(.tool_use/...)
          if (tpl.Length > 0) tpl.Append('\n');
          tpl.Append(tv);
        }

      string tplText = tpl.ToString();
      var markers = new List<string>();
      void Mark(string m) { if (!markers.Contains(m)) markers.Add(m); }

      if (tplText.Length > 0)
      {
        if (Regex.IsMatch(tplText, @"\btools\b")) Mark("tools");
        if (Regex.IsMatch(tplText, @"\btool_calls\b")) Mark("tool_calls");
        if (Regex.IsMatch(tplText, @"\btool_call_id\b")) Mark("tool_call_id");
        if (Regex.IsMatch(tplText, @"\btool_call\b")) Mark("tool_call");
        if (Regex.IsMatch(tplText, @"\btool_choice\b")) Mark("tool_choice");
        if (Regex.IsMatch(tplText, @"\bfunction_call\b|\bfunctions\b")) Mark("functions");
        if (Regex.IsMatch(tplText, @"role\s*==\s*['""]tool")) Mark("role==tool");
        if (Regex.IsMatch(tplText, @"\[TOOL_CALLS\]|<tool_call>|<\|tool|</tool>", RegexOptions.IgnoreCase))
          Mark("tool-теги");
      }

      // Спец-токены словаря (вторичный сигнал): <tool_call>, [TOOL_CALLS],
      // <|tool▁calls▁begin|>, </tool> и т.п. Обычные слова вида «tools» не считаем.
      var toolTokens = new List<string>();
      if (meta.TryGetValue("tokenizer.ggml.tokens", out var tk) && tk is object[] tka)
      {
        foreach (var t in tka)
        {
          if (t is string ts &&
            ts.IndexOf("tool", StringComparison.OrdinalIgnoreCase) >= 0 &&
            Regex.IsMatch(ts, @"tool_call|tool_use|^<\|?[^>]*tool|^\[\s*TOOL",
              RegexOptions.IgnoreCase) &&
            !toolTokens.Contains(ts))
          {
            if (toolTokens.Count < 3) toolTokens.Add(ts);
          }
        }
      }

      if (g.HasChatTemplate && markers.Count > 0)
      {
        g.ToolSupport = ToolSupportKind.Yes;
        g.ToolEvidence = "chat-шаблон: " + string.Join(", ", markers);
        if (toolTokens.Count > 0) g.ToolEvidence += "; токены: " + string.Join(" ", toolTokens);
      }
      else if (g.HasChatTemplate)
      {
        g.ToolSupport = ToolSupportKind.No;
        g.ToolEvidence = "в chat-шаблоне нет работы с tools";
        if (toolTokens.Count > 0) g.ToolEvidence += ", но в словаре есть " + string.Join(" ", toolTokens);
      }
      else if (toolTokens.Count > 0)
      {
        g.ToolSupport = ToolSupportKind.Unknown;
        g.ToolEvidence = "шаблона в GGUF нет; в словаре есть " + string.Join(" ", toolTokens) +
                 " — llama-server подберёт встроенный шаблон";
      }
      else
      {
        g.ToolSupport = ToolSupportKind.Unknown;
        g.ToolEvidence = "шаблона в GGUF нет — llama-server подберёт встроенный по семейству";
      }
    }

    private static long Align(long x, long a)
    {
      long rem = x % a;
      return rem == 0 ? x : x + (a - rem);
    }

    private static double Num(Dictionary<string, object> m, string key, double def = 0)
    {
      if (!m.TryGetValue(key, out var v)) return def;
      try
      {
        if (v is object[] arr) return arr.Length > 0 ? Convert.ToDouble(arr[0], CultureInfo.InvariantCulture) : def;
        return Convert.ToDouble(v, CultureInfo.InvariantCulture);
      }
      catch { return def; }
    }

    private static string RStr(BinaryReader r)
    {
      ulong len = r.ReadUInt64();
      if (len > BenchDefaults.MaxStringLen)
        throw new Exception($"Строка слишком длинная: {len} байт (макс. {BenchDefaults.MaxStringLen})");
      if (len > int.MaxValue)
        throw new Exception($"Длина строки превышает int.MaxValue: {len}");
      return Encoding.UTF8.GetString(r.ReadBytes((int)len));
    }

    private static object RVal(BinaryReader r, uint t)
    {
      switch (t)
      {
        case 0: return r.ReadByte();
        case 1: return r.ReadSByte();
        case 2: return r.ReadUInt16();
        case 3: return r.ReadInt16();
        case 4: return r.ReadUInt32();
        case 5: return r.ReadInt32();
        case 6: return r.ReadSingle();
        case 7: return r.ReadByte() != 0;
        case 8: return RStr(r);
        case 9:
          uint et = r.ReadUInt32();
          ulong n = r.ReadUInt64();
          if (n > BenchDefaults.MaxArrayLen)
            throw new Exception($"Массив слишком большой: {n}");
          var arr = new object[(int)n];
          for (ulong i = 0; i < n; i++) arr[(int)i] = RVal(r, et);
          return arr;
        case 10: return r.ReadUInt64();
        case 11: return r.ReadInt64();
        case 12: return r.ReadDouble();
        default: throw new Exception($"неизвестный тип значения GGUF: {t}");
      }
    }
  }
}
