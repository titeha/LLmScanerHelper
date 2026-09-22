using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Генератор алиаса из имени файла.
  ///
  /// Правила (v4):
  ///  • квант-теги и теги точности (Q8_0, Q6_K, Q4_K_M, Q4_K_L, Q4_K_XL,
  ///    Q8_K_XL, MXFP4, BF16, F16, F32) убираются целиком — в алиасе остаётся
  ///    только имя модели, её параметры и прочие свойства;
  ///  • регистр приводится к «заглавной с буквы»: аббревиатуры остаются ВЕРХНИМ
  ///    регистром (GPT, OSS, UD, VL, MTP, GLM, CD), бренды и уже корректные слова
  ///    (Qwen, Olmo, Devstral, Granite, Ornith, Instruct, A3B) остаются как есть,
  ///    остальные слова получают заглавную букву (granite → Granite, 20b → 20B);
  ///  • разделители остаются как в имени файла (дефисы — дефисами,
  ///    подчёркивания — подчёркиваниями);
  ///  • мусорные токены издателя (gguf/unsloth/lmstudio/mradermacher) выкидываются.
  /// </summary>
  public static class AliasBuilder
  {
    // Аббревиатуры, которые в алиасе должны оставаться ВЕРХНИМ регистром.
    private static readonly HashSet<string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
    {
      "GPT", "OSS", "UD", "VL", "MTP", "GLM", "CD",
    };

    // Мусорные токены издателя.
    private static readonly Regex PublisherGarbage = new Regex(
        @"(?<=[\-_.\s]|^)(?:gguf|unsloth|lmstudio|mradermacher)(?=[\-_.\s]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Квант-теги и теги точности: Q-семейство (Q4_0, Q4_K_M, Q8_K_XL, …),
    // MXFP4, BF16, F16, F32. Разделитель перед тегом съедается, чтобы не
    // оставалось висящих дефисов/подчёркиваний.
    private static readonly Regex QuantTokens = new Regex(
        @"[\-_.\s]?(?:(?:Q[0-9](?:_[A-Za-z0-9]+)*|MXFP[0-9]|BF16|F16|F32))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PureNumber = new Regex(
        @"^\d+(?:\.\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex VersionCode = new Regex(
        @"^[a-z]+\d+$", RegexOptions.CultureInvariant); // i1, a2 — служебные суффиксы
    private static readonly Regex LowerWord = new Regex(
        @"^[a-z]+$", RegexOptions.CultureInvariant);
    private static readonly Regex LowerAlpha = new Regex(
        @"[a-z]", RegexOptions.CultureInvariant);
    private static readonly Regex Word = new Regex(
        @"[A-Za-z0-9]+", RegexOptions.CultureInvariant);

    public static string MakeAlias(string fileName)
    {
      if (string.IsNullOrWhiteSpace(fileName)) return "";

      var s = fileName.Trim();
      if (s.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        s = s[..^5];

      // Мусорные токены целиком (регистр не важен), соседние разделители не трогаем
      s = PublisherGarbage.Replace(s, "");

      // Квант-теги/теги точности — убираем вместе с разделителем перед ними
      s = QuantTokens.Replace(s, "");

      // Приведение регистра к отдельным токенам (разделители остаются на месте)
      s = Word.Replace(s, m => CaseToken(m.Value));

      // Схлопывание повторов разделителей и обрезка мусора по краям
      s = Regex.Replace(s, @"([\-_.\s]){2,}", "$1");
      s = s.Trim(' ', '-', '_', '.');

      return s;
    }

    private static string CaseToken(string t)
    {
      if (PureNumber.IsMatch(t)) return t;                       // 4.1, 1.5, 24
      if (Abbreviations.Contains(t)) return t.ToUpperInvariant(); // gpt → GPT, oss → OSS
      if (char.IsUpper(t[0])) return t;                           // Qwen3, Olmo, A3B, Instruct
      if (VersionCode.IsMatch(t)) return t;                       // i1, a2 — суффиксы как есть
      if (char.IsDigit(t[0])) return LowerAlpha.Replace(t, m => m.Value.ToUpperInvariant()); // 20b → 20B
      if (LowerWord.IsMatch(t)) return char.ToUpperInvariant(t[0]) + t.Substring(1).ToLowerInvariant(); // granite → Granite
      return t;                                                   // прочее — как есть
    }
  }
}
