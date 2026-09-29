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

  /// <summary>
  /// Тензор файла GGUF в том виде, в котором он лежит в tensor-info.
  /// Размер (<see cref="Bytes"/>) считается по формуле ggml через <see cref="GgmlTypes.Bytes"/>,
  /// а не по дельте offset'ов (дельта включает padding и хвост файла).
  /// Для типа вне таблицы S1 <see cref="Bytes"/> = 0 (размер неизвестен).
  /// </summary>
  internal readonly record struct GgufTensorInfo(string Name, long[] Dims, uint TypeId, long Offset, long Bytes);

  /// <summary>Состояние набора шардов после агрегации (S4).</summary>
  public enum SplitStatus
  {
    NotApplicable = 0, // одиночный файл
    Complete = 1,      // все шарды найдены, расхождений нет
    Incomplete = 2,    // не хватает шардов из split.count
    Mismatch = 3       // split.no / дубликаты тензоров / контрольные числа
  }

  // ========================== Парсер GGUF ==========================
  // Размер тензора — по таблице S1, а не по дельте offset'ов; границы data section
  // проверяются строгим инвариантом padding'а. Список тензоров доступен через Tensors.
  public sealed class GgufInfo
  {
    public string Arch = "llama";
    public int BlockCount;
    public long ContextLength, KvHeads, HeadDim, EmbdSize, MtpSize;
    public bool HasReasoning;

    // Детали MTP: тип реализации и сколько доп. токенов модель предсказывает за шаг
    // (число MTP-слоёв). "" / "нет", если MTP не найден; токены 0 = не удалось выяснить.
    public string MtpKind = "";           // nextn | mtp | extra (blk.N сверх block_count)
    public int MtpTokens;

    // Tool-calls (агентная работа): chat-шаблон + спец-токены словаря
    public bool HasChatTemplate;
    public ToolSupportKind ToolSupport;
    public string ToolEvidence = "";
    public long[] LayerSize = Array.Empty<long>();
    public long FileSize;

    // S2: честный учёт байт и контроль целостности.
    public long UnknownBytes;         // известный тип, но имя вне Layer/Embd/MTP
    public int UnknownTensors;        // число таких тензоров
    public int UnknownTypeTensors;    // тензоры с типом вне таблицы S1 (размер неизвестен)
    public string IntegrityNote = ""; // пусто = инвариант padding'а соблюдён

    // Тензоры файла (для S4/S5) и границы data section.
    internal IReadOnlyList<GgufTensorInfo> Tensors = Array.Empty<GgufTensorInfo>();
    internal long HeaderEnd;
    internal long DataStart;

    // S4: агрегация шардов.
    public SplitStatus SplitStatus = SplitStatus.NotApplicable;
    public IReadOnlyList<string> MissingShardPaths = Array.Empty<string>();
    public long PayloadBytes;          // Σ nbytes (по агрегату)
    public bool HasSplitKeys;          // в метаданных был split.count
    public int SplitNo = -1;           // значение split.no файла (0-based)
    public int SplitCount = -1;        // значение split.count
    public int SplitTensorsCount = -1; // значение split.tensors.count
    public int FileType = -1;          // general.file_type (llama_ftype), -1 если ключа нет

    public bool HasMtp => MtpSize > 0;

    /// <summary>Тонкая обёртка: одиночная модель или агрегат шардов (S4).</summary>
    public static GgufInfo Read(string path) => GgufModelReader.Read(path);

    /// <summary>
    /// Разбор одного GGUF-файла (S2). Не агрегирует шарды — для этого <see cref="GgufModelReader"/>.
    /// </summary>
    internal static GgufInfo ParseSingle(string path)
    {
      var g = new GgufInfo();
      long fileSize = new FileInfo(path).Length;
      g.FileSize = fileSize;

      using (var fs = File.OpenRead(path))
      using (var r = new BinaryReader(fs))
      {
        if (r.ReadUInt32() != 0x46554747) throw new Exception("это не GGUF");

        uint version = r.ReadUInt32();
        if (version == 1) throw new Exception("GGUFv1 не поддерживается llama.cpp");
        if (version == 2) throw new Exception("GGUF v2 не поддерживается");
        if (version == 0 || version > 3)
          throw new Exception($"неподдерживаемая версия GGUF: {version} (максимальная поддерживаемая: 3)");

        ulong tensorCount = r.ReadUInt64();
        ulong kvCount = r.ReadUInt64();

        if (tensorCount > AppDefaults.MaxTensorCount)
          throw new Exception($"Слишком много тензоров: {tensorCount}");
        if (kvCount > AppDefaults.MaxKvCount)
          throw new Exception($"Слишком много KV-пар: {kvCount}");

        var meta = new Dictionary<string, object>();
        for (ulong i = 0; i < kvCount; i++)
          meta[RStr(r)] = RVal(r, r.ReadUInt32());

        if (meta.TryGetValue("general.architecture", out var a) && a is string s)
          g.Arch = s;

        g.BlockCount = (int)Num(meta, g.Arch + ".block_count");
        g.ContextLength = (long)Num(meta, g.Arch + ".context_length", 32768);

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

        // ---- split.*: контрольные числа набора шардов (S4, §2) ----
        double splitCount = Num(meta, "split.count", -1);
        if (splitCount >= 0)
        {
          g.HasSplitKeys = true;
          g.SplitCount = (int)splitCount;
        }
        g.SplitNo = (int)Num(meta, "split.no", -1);
        g.SplitTensorsCount = (int)Num(meta, "split.tensors.count", -1);
        g.FileType = (int)Num(meta, "general.file_type", -1);

        // ---- general.alignment: строго uint32, не 0 и степень двойки (gguf.cpp:621-635) ----
        long alignment = 32;
        if (meta.TryGetValue("general.alignment", out var alv))
        {
          if (alv is not uint alu)
            throw new Exception("general.alignment должен быть uint32");
          if (alu == 0 || (alu & (alu - 1)) != 0)
            throw new Exception($"general.alignment = {alu} не является степенью двойки");
          alignment = alu;
        }

        // ---- Tensor-info: name / n_dims / ne[n_dims] / type / offset ----
        var tensors = new List<GgufTensorInfo>((int)Math.Min(tensorCount, 1UL << 20));
        var tensorNames = new HashSet<string>(StringComparer.Ordinal);
        long totalNbytes = 0;

        for (ulong i = 0; i < tensorCount; i++)
        {
          string name = RStr(r);
          if (!tensorNames.Add(name))
            throw new Exception($"дубликат имени тензора: {name}");

          uint nd = r.ReadUInt32();
          if (nd > 4)
            throw new Exception($"тензор {name}: n_dims = {nd} > GGML_MAX_DIMS (4)");

          // dims дополняются единицами (llama.cpp: ne[1..3] = 1, если измерений меньше).
          var dims = new long[4] { 1, 1, 1, 1 };
          for (uint d = 0; d < nd; d++)
          {
            ulong v = r.ReadUInt64();
            if (v > long.MaxValue)
              throw new Exception($"тензор {name}: размерность {v} не помещается в long");
            dims[d] = (long)v;
          }
          CheckElementProduct(name, dims);

          uint typeId = r.ReadUInt32();
          ulong offsetRaw = r.ReadUInt64();
          if (offsetRaw > long.MaxValue)
            throw new Exception($"Смещение тензора слишком велико для long: {offsetRaw}");
          long offset = (long)offsetRaw;

          bool typeKnown = GgmlTypes.TryGet(typeId, out var typeInfo);
          long bytes = 0;
          if (typeKnown)
          {
            if (typeInfo.BlckSize == 0 || dims[0] % typeInfo.BlckSize != 0)
              throw new Exception(
                $"тензор {name}: ne[0] = {dims[0]} не делится на blck_size ({typeInfo.BlckSize}) типа {typeInfo.Name}");
            bytes = GgmlTypes.Bytes(dims, typeId);
          }

          tensors.Add(new GgufTensorInfo(name, dims, typeId, offset, bytes));
          totalNbytes += bytes;
        }

        long headerEnd = fs.Position;
        // data section выравнивается только при n_tensors > 0 (gguf.cpp:773).
        long dataStart = tensorCount > 0 ? Align(headerEnd, alignment) : headerEnd;
        g.HeaderEnd = headerEnd;
        g.DataStart = dataStart;
        g.Tensors = tensors;

        long dataSize = fileSize - dataStart;
        if (dataSize < 0)
          throw new Exception($"файл оборван: конец заголовка {headerEnd} больше размера файла {fileSize}");

        // ---- offset + nbytes должны лежать в data section ----
        foreach (var t in tensors)
          if (t.Offset > dataSize || t.Bytes > dataSize - t.Offset)
            throw new Exception(
              $"тензор {t.Name}: offset {t.Offset} + {t.Bytes} выходит за данные файла ({dataSize})");

        // ---- строгий инвариант padding'а: Σ nbytes ≤ dataSize ≤ Σ nbytes + n·(align−1) ----
        long paddingUpper = (long)tensorCount * (alignment - 1);
        if (dataSize < totalNbytes || dataSize > totalNbytes + paddingUpper)
          g.IntegrityNote =
            $"данные {dataSize} Б вне границы [{totalNbytes}, {totalNbytes + paddingUpper}] (Σ nbytes, padding ≤ {paddingUpper})";

        // ---- классификация байт по списку тензоров (одиночный файл или агрегат) ----
        ClassifyTensors(g, tensors);
      }

      return g;
    }

    /// <summary>
    /// Раскладывает тензоры по слоям / эмбеддингам / MTP и считает нераспознанные байты.
    /// Используется и для одиночного файла (S2), и для агрегата шардов (S4).
    /// </summary>
    internal static void ClassifyTensors(GgufInfo g, IReadOnlyList<GgufTensorInfo> tensors)
    {
      g.EmbdSize = 0;
      g.MtpSize = 0;
      g.UnknownBytes = 0;
      g.UnknownTensors = 0;
      g.UnknownTypeTensors = 0;
      g.MtpKind = "";
      g.MtpTokens = 0;
      g.LayerSize = new long[Math.Max(0, g.BlockCount)];

      // индексы слоёв по типам MTP — для «тип» и «сколько токенов предсказывает»
      var nextnIdx = new HashSet<int>();
      var mtpIdx = new HashSet<int>();
      var extraIdx = new HashSet<int>();
      long payload = 0;

      foreach (var t in tensors)
      {
        payload += t.Bytes;
        if (!GgmlTypes.TryGet(t.TypeId, out _))
        {
          // Тип вне таблицы S1: размер неизвестен, байты не учитываем (отдельный счётчик).
          g.UnknownTypeTensors++;
          continue;
        }

        long size = t.Bytes;
        string name = t.Name;
        bool isNextn = name.IndexOf("nextn", StringComparison.OrdinalIgnoreCase) >= 0;
        bool explicitMtp = isNextn ||
                   name.IndexOf(".mtp.", StringComparison.OrdinalIgnoreCase) >= 0;

        if (explicitMtp)
        {
          g.MtpSize += size;
          var bm = Regex.Match(name, @"^blk\.(\d+)\.");
          if (bm.Success && int.TryParse(bm.Groups[1].Value, out int bi))
            (isNextn ? nextnIdx : mtpIdx).Add(bi);
        }
        else if (name.StartsWith("token_embd", StringComparison.OrdinalIgnoreCase))
          g.EmbdSize += size;
        else
        {
          var m = Regex.Match(name, @"^blk\.(\d+)\.");
          if (m.Success && int.TryParse(m.Groups[1].Value, out int li))
          {
            if (li >= 0 && li < g.BlockCount)
              g.LayerSize[li] += size;
            else
            {
              g.MtpSize += size; // дополнительный blk.N за block_count
              extraIdx.Add(li);
            }
          }
          else
          {
            // Напр. per_layer_token_embd.* (qwen4exp), output.weight, cls.* —
            // не слой, не эмбеддинг, не MTP: учитываем явно, не молча.
            g.UnknownBytes += size;
            g.UnknownTensors++;
          }
        }
      }

      g.PayloadBytes = payload;

      // Тип MTP и число предсказываемых токенов: приоритет nextn > mtp > доп. блоки.
      // Один слой = один доп. токен за шаг; без индексов слой не распознать (токены = 0).
      if (nextnIdx.Count > 0) { g.MtpKind = "nextn"; g.MtpTokens = nextnIdx.Count; }
      else if (mtpIdx.Count > 0) { g.MtpKind = "mtp"; g.MtpTokens = mtpIdx.Count; }
      else if (extraIdx.Count > 0) { g.MtpKind = "extra"; g.MtpTokens = extraIdx.Count; }
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
        foreach (var t in tka)
          if (t is string ts
                      && ts.Contains("tool", StringComparison.OrdinalIgnoreCase)
                      && Regex.IsMatch(ts, @"tool_call|tool_use|^<\|?[^>]*tool|^\[\s*TOOL", RegexOptions.IgnoreCase)
                      && !toolTokens.Contains(ts)
                      && toolTokens.Count < 3)
            toolTokens.Add(ts);

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

    /// <summary>Произведение размерностей не должно переполнять long (gguf.cpp:700-710).</summary>
    private static void CheckElementProduct(string name, long[] dims)
    {
      if (dims[0] == 0 || dims[1] == 0 || dims[2] == 0 || dims[3] == 0) return;
      try
      {
        long p = checked(dims[0] * dims[1]);
        p = checked(p * dims[2]);
        p = checked(p * dims[3]);
      }
      catch (OverflowException)
      {
        throw new Exception($"тензор {name}: произведение размерностей переполняет long");
      }
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
      if (len > AppDefaults.MaxStringLen)
        throw new Exception($"Строка слишком длинная: {len} байт (макс. {AppDefaults.MaxStringLen})");
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
          if (n > AppDefaults.MaxArrayLen)
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
