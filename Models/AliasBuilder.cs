using System.Text.RegularExpressions;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Генератор алиаса из имени файла.
  ///
  /// Правила (v4):
  ///  • регистр НЕ приводится к нижнему — GPT, UD, Qwen3 и т.п. остаются как есть;
  ///  • разделители остаются как в имени файла (дефисы — дефисами, подчёркивания — подчёркиваниями);
  ///  • мусорные токены издателя (gguf/unsloth/lmstudio/mradermacher) выкидываются;
  ///  • квант-теги сокращаются как в LINQPad-версии: Q4_K_M → Q4, Q6_K → Q6, Q8_0 → Q8.
  /// </summary>
  public static class AliasBuilder
  {
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

      // Квант-теги (как в оригинальном MakeAlias, но с сохранением регистра)
      s = Regex.Replace(s, @"Q4[._\-]?K[._\-]?M", "Q4", RegexOptions.IgnoreCase);
      s = Regex.Replace(s, @"Q5[._\-]?K[._\-]?M", "Q5", RegexOptions.IgnoreCase);
      s = Regex.Replace(s, @"Q6[._\-]?K(?![A-Za-z0-9])", "Q6", RegexOptions.IgnoreCase);
      s = Regex.Replace(s, @"Q8[._\-]?0(?![A-Za-z0-9])", "Q8", RegexOptions.IgnoreCase);

      // Схлопывание повторов разделителей и обрезка мусора по краям
      s = Regex.Replace(s, @"([\-_.\s]){2,}", "$1");
      s = s.Trim(' ', '-', '_', '.');

      return s;
    }
  }
}
