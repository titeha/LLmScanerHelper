using System.Runtime.CompilerServices;

namespace LlmScanHelper.Models
{
  /// <summary>
  /// Свойства одного ggml-типа данных: id в GGUF, человекочитаемое имя, размер блока
  /// (blck_size) и размер одного блока в байтах (type_size). Источник значений —
  /// ggml/src/ggml.c / ggml-common.h (block_* структуры + static_assert), а не gguf-py.
  /// </summary>
  public readonly record struct GgmlTypeInfo(int Id, string Name, int BlckSize, int TypeSize)
  {
    /// <summary>Биты на элемент веса (bytes-per-weight) этого типа: type_size * 8 / blck_size.</summary>
    public double Bpw => TypeSize * 8.0 / BlckSize;
  }

  /// <summary>
  /// Единственный авторитетный источник перевода `ggml_type id → (имя, blck_size, type_size)`
  /// и вычисления размеров тензоров. Таблица строго соответствует §4 контекста формата GGUF
  /// (переписана со static_assert в ggml-common.h). Неизвестный id → TryGet == false.
  /// </summary>
  public static class GgmlTypes
  {
    // id | blck | type_size (сверено с block_* static_assert; QK_K = 256, K_SCALE_SIZE = 12)
    private static readonly GgmlTypeInfo[] Table =
    {
      new(0,  "f32",   1, 4),
      new(1,  "f16",   1, 2),
      new(2,  "q4_0",  32, 18),
      new(3,  "q4_1",  32, 20),
      new(6,  "q5_0",  32, 22),
      new(7,  "q5_1",  32, 24),
      new(8,  "q8_0",  32, 34),
      new(9,  "q8_1",  32, 36), // C: 2*ggml_half + QK8_1 = 36 (не 40 как в gguf-py)
      new(10, "q2_K",  256,84),
      new(11, "q3_K",  256,110),
      new(12, "q4_K",  256,144),
      new(13, "q5_K",  256,176),
      new(14, "q6_K",  256,210),
      new(15, "q8_K",  256,292),
      new(16, "iq2_xxs",256,66),
      new(17, "iq2_xs", 256,74),
      new(18, "iq3_xxs",256,98),
      new(19, "iq1_s",  256,50),
      new(20, "iq4_nl", 32, 18),
      new(21, "iq3_s",  256,110),
      new(22, "iq2_s",  256,82),
      new(23, "iq4_xs", 256,136),
      new(24, "i8",     1,  1),
      new(25, "i16",    1,  2),
      new(26, "i32",    1,  4),
      new(27, "i64",    1,  8),
      new(28, "f64",    1,  8),
      new(29, "iq1_m",  256,56),
      new(30, "bf16",   1,  2),
      new(34, "tq1_0",  256,54),
      new(35, "tq2_0",  256,66),
      new(39, "mxfp4",  32, 17),
      new(40, "nvfp4",  64, 36),
      new(41, "q1_0",   128,18),
      new(42, "q2_0",   64, 18),
    };

    /// <summary>
    /// Поиск свойств ggml-типа по id (как в `enum ggml_type` / GGUF).
    /// Типы >= GGML_TYPE_COUNT (43) и любые отсутствующие в таблице id → false.
    /// Отдельного исключения для неизвестного типа нет.
    /// </summary>
    public static bool TryGet(uint typeId, out GgmlTypeInfo info)
    {
      info = default;
      // id не обязан совпадать с индексом в таблице: в ggml_type есть разрывы
      // (4, 5, 31–33, 36–38 и т.п.), поэтому ищем по полю Id, а не по индексу.
      foreach (var t in Table)
        if (t.Id == (int)typeId)
        {
          info = t;
          return true;
        }
      return false;
    }

    /// <summary>Биты на элемент веса для типа: type_size * 8 / blck_size.</summary>
    public static double Bpw(uint typeId) =>
        TryGet(typeId, out var info) ? info.Bpw : 0.0;

    /// <summary>
    /// Размер тензора в байтах по формуле ggml:
    /// (ne0 / blck) * typeSize * ne1 * ne2 * ne3 (для blck == 1 это ne0 * typeSize).
    /// Неизвестный тип → ArgumentException (размер нельзя оценивать «на глаз»).
    /// Переполнение long не даёт отрицательного результата — насыщается до long.MaxValue.
    /// </summary>
    public static long Bytes(long[] dims, uint typeId)
    {
      if (!TryGet(typeId, out var info))
        throw new ArgumentException(
            $"неизвестный ggml-тип {typeId} — вызывайте Bytes после GgmlTypes.TryGet");

      long ne0 = dims.Length > 0 ? dims[0] : 1;
      long ne1 = dims.Length > 1 ? dims[1] : 1;
      long ne2 = dims.Length > 2 ? dims[2] : 1;
      long ne3 = dims.Length > 3 ? dims[3] : 1;

      try
      {
        return checked(checked(checked(ne0 / info.BlckSize) * info.TypeSize) * ne1)
                   * ne2 * ne3;
      }
      catch (OverflowException)
      {
        // Переполнение не должно превращаться в отрицательный размер.
        return long.MaxValue;
      }
    }
  }
}
