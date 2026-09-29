namespace LlmScanHelper.Models
{
  /// <summary>
  /// Перевод `general.file_type` (id из `enum llama_ftype`) в человекочитаемую метку и обратно —
  /// «доминирующий ggml-тип → ftype». Источник значений — include/llama.h (enum llama_ftype) и
  /// src/llama-model-loader.cpp (§3, switch type_max). Метки для UI — без префикса «Mostly»
  /// (это внутренности ложа llama.cpp) и без суфикса bpw.
  /// </summary>
  public static class LlamaFtype
  {
    // llama.cpp помечает догадку битом (не отдаём его наружу): ftype & ~1024.
    private const uint GUESSED_MASK = 1024;

    // id enum llama_ftype → метка (строго по §3 контекста, без «Mostly»).
    private static readonly (int Id, string Name)[] Names =
    {
      (0,  "F32"),
      (1,  "F16"),
      (2,  "Q4_0"),
      (3,  "Q4_1"),
      (7,  "Q8_0"),
      (8,  "Q5_0"),
      (9,  "Q5_1"),
      (10, "Q2_K"),
      (11, "Q3_K_S"),
      (12, "Q3_K_M"),
      (13, "Q3_K_L"),
      (14, "Q4_K_S"),
      (15, "Q4_K_M"),
      (16, "Q5_K_S"),
      (17, "Q5_K_M"),
      (18, "Q6_K"),
      (24, "IQ1_S"),
      (25, "IQ4_NL"),
      (30, "IQ4_XS"),
      (31, "IQ1_M"),
      (32, "BF16"),
      (38, "MXFP4_MOE"),
      (39, "NVFP4"),
      (40, "Q1_0"),
      (41, "Q2_0"),
    };

    /// <summary>
    /// Метка для `general.file_type`. Бит (guessed) [1024] игнорируется.
    /// Неизвестный id → false.
    /// </summary>
    public static bool TryGetName(uint ftype, out string name)
    {
      name = "";
      uint key = ftype & ~GUESSED_MASK;
      foreach (var (id, label) in Names)
        if (id == (int)key)
        {
          name = label;
          return true;
        }
      return false;
    }

    // Копия switch(type_max) из src/llama-model-loader.cpp:755-790:
    // ggml_type id → доминирующий llama_ftype (Q3_K→Q3_K_M, Q4_K→Q4_K_M, Q5_K→Q5_K_M и т.д.).
    // Неизвестный ggml-тип → false (в llama.cpp тут default → предупреждение, но не метка).
    private static readonly (int GgmlId, uint Ftype)[] GgmlToFtype =
    {
      (0,  0),   // GGML_TYPE_F32     → ALL_F32
      (1,  1),   // GGML_TYPE_F16     → MOSTLY_F16
      (2,  2),   // GGML_TYPE_Q4_0    → MOSTLY_Q4_0
      (3,  3),   // GGML_TYPE_Q4_1    → MOSTLY_Q4_1
      (6,  8),   // GGML_TYPE_Q5_0    → MOSTLY_Q5_0
      (7,  9),   // GGML_TYPE_Q5_1    → MOSTLY_Q5_1
      (8,  7),   // GGML_TYPE_Q8_0    → MOSTLY_Q8_0
      (10, 10),  // GGML_TYPE_Q2_K    → MOSTLY_Q2_K
      (11, 12),  // GGML_TYPE_Q3_K    → MOSTLY_Q3_K_M
      (12, 15),  // GGML_TYPE_Q4_K    → MOSTLY_Q4_K_M
      (13, 17),  // GGML_TYPE_Q5_K    → MOSTLY_Q5_K_M
      (14, 18),  // GGML_TYPE_Q6_K    → MOSTLY_Q6_K
      (16, 19),  // GGML_TYPE_IQ2_XXS   → MOSTLY_IQ2_XXS
      (17, 20),  // GGML_TYPE_IQ2_XS    → MOSTLY_IQ2_XS
      (18, 23),  // GGML_TYPE_IQ3_XXS   → MOSTLY_IQ3_XXS
      (19, 24),  // GGML_TYPE_IQ1_S     → MOSTLY_IQ1_S
      (20, 25),  // GGML_TYPE_IQ4_NL    → MOSTLY_IQ4_NL
      (21, 26),  // GGML_TYPE_IQ3_S     → MOSTLY_IQ3_S
      (22, 28),  // GGML_TYPE_IQ2_S     → MOSTLY_IQ2_S
      (23, 30),  // GGML_TYPE_IQ4_XS    → MOSTLY_IQ4_XS
      (29, 31),  // GGML_TYPE_IQ1_M     → MOSTLY_IQ1_M
      (30, 32),  // GGML_TYPE_BF16      → MOSTLY_BF16
      (34, 36),  // GGML_TYPE_TQ1_0     → MOSTLY_TQ1_0
      (35, 37),  // GGML_TYPE_TQ2_0     → MOSTLY_TQ2_0
      (40, 39),  // GGML_TYPE_NVFP4     → MOSTLY_NVFP4
      (41, 40),  // GGML_TYPE_Q1_0      → MOSTLY_Q1_0
      (42, 41),  // GGML_TYPE_Q2_0      → MOSTLY_Q2_0
    };

    /// <summary>
    /// Отображение «доминирующий ggml-тип → ftype» (копия switch в llama-model-loader.cpp).
    /// Нужна S5, чтобы она не изобретала свою таблицу. Неизвестный тип → false.
    /// </summary>
    public static bool TryGetFtypeForGgmlType(uint ggmlTypeId, out uint ftype)
    {
      ftype = 0;
      foreach (var (ggmlId, ft) in GgmlToFtype)
        if (ggmlId == (int)ggmlTypeId)
        {
          ftype = ft;
          return true;
        }
      return false;
    }
  }
}
