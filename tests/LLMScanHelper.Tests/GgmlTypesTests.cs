using System;
using System.Collections.Generic;
using LlmScanHelper.Models;

using Xunit;

namespace LlmScanHelper.Tests
{
    /// <summary>
    /// Тесты для GgmlTypes (размеры ggml-типов, ggml_nbytes, bpw) и LlamaFtype
    /// (мета-метки general.file_type и отображение ggml-тип → ftype).
    /// Значения сверены с ggml-common.h static_assert и §4 контекста формата GGUF.
    /// </summary>
    public class GgmlTypesTests
    {
        // ===================== Размеры блоков (blck_size / type_size) =====================
        // Зачистка «интуитивных» неверных значений (см. предупреждение в задаче S1).

        [Theory]
        [InlineData((uint)6,  "q5_0",   32, 22)]
        [InlineData((uint)7,  "q5_1",   32, 24)]
        [InlineData((uint)13, "q5_K",   256,176)]
        [InlineData((uint)9,  "q8_1",   32, 36)]   // C: 36, НЕ 40 как в gguf-py
        [InlineData((uint)15, "q8_K",   256,292)]
        [InlineData((uint)23, "iq4_xs", 256,136)]
        [InlineData((uint)39, "mxfp4",  32, 17)]
        [InlineData((uint)40, "nvfp4",  64, 36)]
        [InlineData((uint)41, "q1_0",   128,18)]
        public void TryGet_block_sizes_match_authority(uint typeId, string name, int blck, int typeSize)
        {
            Assert.True(GgmlTypes.TryGet(typeId, out var info));
            Assert.Equal(name, info.Name);
            Assert.Equal(blck, info.BlckSize);
            Assert.Equal(typeSize, info.TypeSize);
        }

        [Theory]
        // id, name, blck, type_size — вся таблица §4 (включая разрывы в нумерации).
        [InlineData((uint)0,  "f32",   1, 4)]
        [InlineData((uint)1,  "f16",   1, 2)]
        [InlineData((uint)2,  "q4_0",  32, 18)]
        [InlineData((uint)3,  "q4_1",  32, 20)]
        [InlineData((uint)8,  "q8_0",  32, 34)]
        [InlineData((uint)10, "q2_K",  256,84)]
        [InlineData((uint)11, "q3_K",  256,110)]
        [InlineData((uint)12, "q4_K",  256,144)]
        [InlineData((uint)14, "q6_K",  256,210)]
        [InlineData((uint)16, "iq2_xxs",256,66)]
        [InlineData((uint)17, "iq2_xs", 256,74)]
        [InlineData((uint)18, "iq3_xxs",256,98)]
        [InlineData((uint)19, "iq1_s",  256,50)]
        [InlineData((uint)20, "iq4_nl", 32, 18)]
        [InlineData((uint)21, "iq3_s",  256,110)]
        [InlineData((uint)22, "iq2_s",  256,82)]
        [InlineData((uint)24, "i8",     1, 1)]
        [InlineData((uint)25, "i16",    1, 2)]
        [InlineData((uint)26, "i32",    1, 4)]
        [InlineData((uint)27, "i64",    1, 8)]
        [InlineData((uint)28, "f64",    1, 8)]
        [InlineData((uint)29, "iq1_m",  256,56)]
        [InlineData((uint)30, "bf16",   1, 2)]
        [InlineData((uint)34, "tq1_0",  256,54)]
        [InlineData((uint)35, "tq2_0",  256,66)]
        public void TryGet_full_table(uint typeId, string name, int blck, int typeSize)
        {
            Assert.True(GgmlTypes.TryGet(typeId, out var info), $"тип {typeId} должен быть в таблице");
            Assert.Equal(name, info.Name);
            Assert.Equal(blck, info.BlckSize);
            Assert.Equal(typeSize, info.TypeSize);
        }

        [Theory]
        // Разрывы в numерации ggml_type: 4, 5, 31–33, 36–38 отсутствуют.
        [InlineData((uint)4)]
        [InlineData((uint)5)]
        [InlineData((uint)31)]
        [InlineData((uint)32)]
        [InlineData((uint)33)]
        [InlineData((uint)36)]
        [InlineData((uint)37)]
        [InlineData((uint)38)]
        // Типы >= GGML_TYPE_COUNT (43) — «неизвестный тип», не исключение.
        [InlineData((uint)43)]
        [InlineData((uint)1000)]
        public void TryGet_unknown_type_returns_false(uint typeId)
        {
            Assert.False(GgmlTypes.TryGet(typeId, out var info));
            Assert.Equal(default, info);
        }

        // ===================== bpw (bytes-per-weight) =====================
        // Сверка с метками в tools/quantize/quantize.cpp.

        [Theory]
        [InlineData((uint)23, 4.25)]   // IQ4_XS
        [InlineData((uint)18, 3.0625)] // IQ3_XXS
        [InlineData((uint)19, 1.5625)] // IQ1_S
        [InlineData((uint)29, 1.75)]   // IQ1_M
        [InlineData((uint)41, 1.125)]  // Q1_0
        [InlineData((uint)14, 6.5625)] // Q6_K
        public void Bpw_matches_quantize_labels(uint typeId, double expectedBpw)
        {
            Assert.Equal(expectedBpw, GgmlTypes.Bpw(typeId), 6);
        }

        [Theory]
        [InlineData((uint)0,  32.0)]   // f32
        [InlineData((uint)1,  16.0)]   // f16
        [InlineData((uint)8,  8.5)]    // q8_0
        [InlineData((uint)15, 9.125)]  // q8_K
        [InlineData((uint)40, 4.5)]    // nvfp4 (bpw как у iq4_xs, но другой id/blck)
        public void Bpw_for_common_types(uint typeId, double expectedBpw)
        {
            Assert.Equal(expectedBpw, GgmlTypes.Bpw(typeId), 6);
        }

        [Fact]
        public void Bpw_unknown_type_returns_zero()
        {
            Assert.Equal(0.0, GgmlTypes.Bpw(999));
        }

        // ===================== Bytes (ggml_nbytes) =====================

        [Fact]
        public void Bytes_2d_q8_0_realistic_tensor()
        {
            // [4096, 4096] q8_0 = 4096*4096/32*34
            long expected = 4096L * 4096L / 32 * 34;
            Assert.Equal(expected, GgmlTypes.Bytes(new long[] { 4096, 4096 }, 8));
        }

        [Fact]
        public void Bytes_real_shard_embedding_tensor()
        {
            // per_layer_token_embd.weight [160, 320001536] q8_0 (~50.66 GiB).
            long expected = 160L * 320001536L / 32 * 34;
            Assert.Equal(expected, GgmlTypes.Bytes(new long[] { 160, 320001536 }, 8));
            // Приблизительно 50.66 GiB — проверяем порядок величины, а не абсолютный ноль.
            Assert.True(GgmlTypes.Bytes(new long[] { 160, 320001536 }, 8) > 50L * 1024 * 1024 * 1024);
        }

        [Theory]
        // blck == 1: ne0 * typeSize (1-D тензор, остальные dims = 1).
        [InlineData(new long[] { 10 }, (uint)0, 40L)]    // f32
        [InlineData(new long[] { 10 }, (uint)1, 20L)]    // f16
        [InlineData(new long[] { 10 }, (uint)24, 10L)]   // i8
        // 4-D произвольного размера.
        [InlineData(new long[] { 256, 2, 3, 4 }, (uint)14, 210L * 2 * 3 * 4)] // q6_K (ne0 % blck == 0)
        public void Bytes_general_formula(long[] dims, uint typeId, long expected)
        {
            Assert.Equal(expected, GgmlTypes.Bytes(dims, typeId));
        }

        [Fact]
        public void Bytes_overflow_does_not_become_negative()
        {
            // Заведомо переполняющее произведение: не должно давать отрицательный размер.
            long result = GgmlTypes.Bytes(new[] { long.MaxValue, long.MaxValue }, 8);
            Assert.True(result >= 0);
            Assert.Equal(long.MaxValue, result);
        }

        [Fact]
        public void Bytes_unknown_type_throws()
        {
            // По контракту для неизвестного типа сначала вызывает TryGet; Bytes не вызывается.
            Assert.False(GgmlTypes.TryGet(1000, out _));
            Assert.Throws<ArgumentException>(() => GgmlTypes.Bytes(new long[] { 32, 32 }, 1000));
        }

        // ===================== LlamaFtype::TryGetName =====================

        [Theory]
        [InlineData((uint)0,  "F32")]
        [InlineData((uint)1,  "F16")]
        [InlineData((uint)2,  "Q4_0")]
        [InlineData((uint)3,  "Q4_1")]
        [InlineData((uint)7,  "Q8_0")]
        [InlineData((uint)8,  "Q5_0")]
        [InlineData((uint)9,  "Q5_1")]
        [InlineData((uint)10, "Q2_K")]
        [InlineData((uint)11, "Q3_K_S")]
        [InlineData((uint)12, "Q3_K_M")]
        [InlineData((uint)13, "Q3_K_L")]
        [InlineData((uint)14, "Q4_K_S")]
        [InlineData((uint)15, "Q4_K_M")]
        [InlineData((uint)16, "Q5_K_S")]
        [InlineData((uint)17, "Q5_K_M")]
        [InlineData((uint)18, "Q6_K")]
        [InlineData((uint)24, "IQ1_S")]
        [InlineData((uint)25, "IQ4_NL")]
        [InlineData((uint)30, "IQ4_XS")]
        [InlineData((uint)31, "IQ1_M")]
        [InlineData((uint)32, "BF16")]
        [InlineData((uint)38, "MXFP4_MOE")]
        [InlineData((uint)39, "NVFP4")]
        [InlineData((uint)40, "Q1_0")]
        [InlineData((uint)41, "Q2_0")]
        public void TryGetName_known_types(uint ftype, string expectedName)
        {
            Assert.True(LlamaFtype.TryGetName(ftype, out var name));
            Assert.Equal(expectedName, name);
        }

        [Theory]
        [InlineData((uint)4)]   // зарезервировано/удалено
        [InlineData((uint)500)]
        public void TryGetName_unknown_returns_false(uint ftype)
        {
            Assert.False(LlamaFtype.TryGetName(ftype, out var name));
            Assert.Equal("", name);
        }

        [Fact]
        public void TryGetName_ignores_guessed_flag()
        {
            // llama.cpp помечает догадку битом 1024 — наружу он не отдаётся.
            Assert.True(LlamaFtype.TryGetName(7 | 1024, out var name));
            Assert.Equal("Q8_0", name);
        }

        // ===================== LlamaFtype::TryGetFtypeForGgmlType =====================
        // Копия switch(type_max) из llama-model-loader.cpp:755-790.

        [Theory]
        [InlineData((uint)0,  0)]   // F32     → ALL_F32
        [InlineData((uint)1,  1)]   // F16     → MOSTLY_F16
        [InlineData((uint)2,  2)]   // Q4_0
        [InlineData((uint)3,  3)]   // Q4_1
        [InlineData((uint)6,  8)]   // Q5_0
        [InlineData((uint)7,  9)]   // Q5_1
        [InlineData((uint)8,  7)]   // Q8_0
        [InlineData((uint)10, 10)]  // Q2_K
        [InlineData((uint)11, 12)]  // Q3_K    → Q3_K_M
        [InlineData((uint)12, 15)]  // Q4_K    → Q4_K_M
        [InlineData((uint)13, 17)]  // Q5_K    → Q5_K_M
        [InlineData((uint)14, 18)]  // Q6_K
        [InlineData((uint)19, 24)]  // IQ1_S
        [InlineData((uint)20, 25)]  // IQ4_NL
        [InlineData((uint)21, 26)]  // IQ3_S
        [InlineData((uint)22, 28)]  // IQ2_S
        [InlineData((uint)23, 30)]  // IQ4_XS
        [InlineData((uint)29, 31)]  // IQ1_M
        [InlineData((uint)30, 32)]  // BF16
        [InlineData((uint)34, 36)]  // TQ1_0
        [InlineData((uint)35, 37)]  // TQ2_0
        [InlineData((uint)40, 39)]  // NVFP4
        [InlineData((uint)41, 40)]  // Q1_0
        [InlineData((uint)42, 41)]  // Q2_0
        public void TryGetFtypeForGgmlType_known(uint ggmlTypeId, uint expectedFtype)
        {
            Assert.True(LlamaFtype.TryGetFtypeForGgmlType(ggmlTypeId, out var ftype));
            Assert.Equal(expectedFtype, ftype);
        }

        [Theory]
        // Не участвующие в switch типы → false (i8, i16…, f64, mxfp4-массивы).
        [InlineData((uint)24)] // i8
        [InlineData((uint)25)] // i16
        [InlineData((uint)26)] // i32
        [InlineData((uint)27)] // i64
        [InlineData((uint)28)] // f64
        [InlineData((uint)39)] // mxfp4 — в switch нет, → false
        public void TryGetFtypeForGgmlType_unknown_returns_false(uint ggmlTypeId)
        {
            Assert.False(LlamaFtype.TryGetFtypeForGgmlType(ggmlTypeId, out var ftype));
            Assert.Equal(0u, ftype);
        }
    }
}
