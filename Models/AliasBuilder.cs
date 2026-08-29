using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Генератор алиаса из имени файла.
  ///
  /// Правила (v4):
  ///  • регистр НЕ приводится — GPT, UD, Qwen3 и т.п. остаются как есть;
  ///  • разделители остаются как в имени файла (дефисы — дефисами, подчёркивания — подчёркиваниями);
  ///  • мусорные токены издателя (gguf/unsloth/lmstudio/mradermacher) выкидываются;
  ///  • всё, что намекает на квантование/точность (Q8_0, Q6_K, Q4_K_M, Q4_K_L,
  ///    Q4_K_XL, Q8_K_XL, MXFP4, BF16, F16, F32), убирается целиком, вместе с
  ///    разделителем перед ним — в алиасе остаётся только модель и её свойства.
  /// </summary>
  public static class AliasBuilder
  {
    // Квант-теги и теги точности: Q-семейство (Q4_0, Q4_K_M, Q8_K_XL, …),
    // MXFP4, BF16, F16, F32. Разделитель перед тегом съедается, чтобы не
    // оставалось висящих дефисов/подчёркиваний.
    private static readonly Regex QuantTokens = new Regex(
        @"[\-_.\s]?(?:(?:Q[0-9](?:_[A-Za-z0-9]+)*|MXFP[0-9]|BF16|F16|F32))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string MakeAlias(string fileName)
    {
      if (string.IsNullOrWhiteSpace(fileName)) return "";

      var s = fileName.Trim();
      if (s.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
        s = s[..^5];

      // Мусорные токены целиком (регистр не важен), соседние разделители не трогаем
      s = Regex.Replace(s,
        @"(?<=[\-_.\s]|^)(?:gguf|unsloth|lmstudio|mradermacher)(?=[\-_.\s]|$)",
        "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

      // Квант-теги/теги точности — убираем вместе с разделителем перед ними
      s = QuantTokens.Replace(s, "");

      // Схлопывание повторов разделителей и обрезка мусора по краям
      s = Regex.Replace(s, @"([\-_.\s]){2,}", "$1");
      s = s.Trim(' ', '-', '_', '.');

      return s;
    }
  }
}
