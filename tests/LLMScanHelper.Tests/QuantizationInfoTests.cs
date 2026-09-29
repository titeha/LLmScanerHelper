using System.Collections.Generic;
using System.Linq;

using LlmScanHelper.Models;

using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// S5: определение квантования модели. Значение — из general.file_type; догадка по байтам
/// body-тензоров — только при отсутствии ключа. Кейсы — из §7 контекста формата.
/// </summary>
public class QuantizationInfoTests
{
    private static GgufTensorInfo T(string name, int typeId, params long[] dims)
    {
        var d = new long[] { 1, 1, 1, 1 };
        for (int i = 0; i < dims.Length && i < 4; i++) d[i] = dims[i];
        return new GgufTensorInfo(name, d, (uint)typeId, 0, GgmlTypes.Bytes(d, (uint)typeId));
    }

    private static GgufInfo G(int fileType, params GgufTensorInfo[] tensors)
    {
        var g = new GgufInfo { FileType = fileType, BlockCount = 32 };
        g.Tensors = tensors;
        return g;
    }

    // ===================== 1. file_type=18 + UD-Q6_K_XL + байтовая доминанта q8_0 =====================

    [Fact]
    public void File_type_18_и_байтовая_доминанта_q8_0()
    {
        var g = G(18,
            T("per_layer_token_embd.weight", 8, 160, 1000),  // q8_0, не body
            T("token_embd.weight", 8, 64, 10),               // q8_0, не body
            T("blk.0.attn_q.weight", 14, 256, 256),          // q6_K, body
            T("blk.0.attn_k.weight", 14, 256, 256));         // q6_K, body

        var info = QuantizationAnalyzer.Analyze(g, "Qwen3.8-Flash-Next-UD-Q6_K_XL");

        Assert.Equal("Q6_K", info.Label);
        Assert.Equal(QuantizationSource.Metadata, info.Source);
        Assert.True(info.Bpw > 6.5625 + 0.15, $"bpw={info.Bpw}");   // ~7.63 → повышенная точность
        Assert.Contains(info.Notes, n => n.Contains("повышенн"));
        Assert.Contains(info.TypeShares, s => s.TypeName == "q8_0");
        Assert.Contains(info.TypeShares, s => s.TypeName == "q6_K");
        Assert.Equal("Q6_K_XL", info.NameToken);
        Assert.True(info.NameMatches);
    }

    // ===================== 2. gpt-oss-120b-F16: метка F16, веса mxfp4 =====================

    [Fact]
    public void Gpt_oss_F16_не_подтверждается_весами()
    {
        var g = G(1,
            T("blk.0.ffn.weight", 39, 32, 32),   // mxfp4, body
            T("blk.0.attn_q.weight", 1, 10));    // f16, 1-D

        var info = QuantizationAnalyzer.Analyze(g, "gpt-oss-120b-F16");

        Assert.Equal("F16", info.Label);
        Assert.Equal(QuantizationSource.Metadata, info.Source);
        Assert.True(info.Bpw < 16 - 0.15);
        Assert.Contains(info.Notes, n => n.Contains("не подтверждается"));
        Assert.Contains(info.Notes, n => n.Contains("mxfp4"));
        Assert.Contains(info.Notes, n => n.Contains("эксперты"));      // метрика описывает не-экспертные веса
        Assert.True(info.NameMatches);
    }

    // ===================== 3. gpt-oss-20b-MXFP4: file_type=38 =====================

    [Fact]
    public void Gpt_oss_MXFP4_метка_совпадает_с_именем()
    {
        var g = G(38,
            T("blk.0.ffn.weight", 39, 32, 32),        // mxfp4, body
            T("blk.0.attn_norm.weight", 1, 36));      // f16, 1-D

        var info = QuantizationAnalyzer.Analyze(g, "gpt-oss-20b-MXFP4");

        Assert.Equal("MXFP4_MOE", info.Label);
        Assert.Equal(QuantizationSource.Metadata, info.Source);
        Assert.InRange(info.Bpw, 4.4, 4.8);           // ≈ 4.63
        Assert.Equal("MXFP4", info.NameToken);
        Assert.True(info.NameMatches);                // алиас MXFP4 → MXFP4_MOE
    }

    // ===================== 4. gpt-oss-20b-UD-Q8_K_XL: метка Q8_0, имя Q8_K_XL =====================

    [Fact]
    public void UD_Q8_K_XL_расходится_с_меткой_Q8_0()
    {
        var g = G(7,
            T("blk.0.ffn.weight", 39, 32, 32),
            T("blk.0.attn_q.weight", 1, 32));

        var info = QuantizationAnalyzer.Analyze(g, "gpt-oss-20b-UD-Q8_K_XL");

        Assert.Equal("Q8_0", info.Label);
        Assert.Equal(QuantizationSource.Metadata, info.Source);
        Assert.Equal("Q8_K_XL", info.NameToken);
        Assert.False(info.NameMatches);
        Assert.Contains(info.Notes, n => n.Contains("в имени: Q8_K_XL"));
    }

    // ===================== 5. нет file_type: правило body важнее «по числу» =====================

    [Fact]
    public void Без_file_type_правило_body_даёт_q8_0_а_не_f32()
    {
        var tensors = new List<GgufTensorInfo>();
        for (int i = 0; i < 3; i++) tensors.Add(T($"blk.0.norm{i}.weight", 0, 10));  // f32, 1-D (по числу больше)
        for (int i = 0; i < 2; i++) tensors.Add(T($"blk.{i}.attn_q.weight", 8, 32, 32)); // q8_0, body

        var info = QuantizationAnalyzer.Analyze(G(-1, tensors.ToArray()), "Ornith-1.5-9B-Q8_0");

        Assert.Equal("Q8_0", info.Label);
        Assert.Equal(QuantizationSource.Guessed, info.Source);
        Assert.Contains(info.Notes, n => n.Contains("оценка"));
    }

    // ===================== 6. нет данных =====================

    [Fact]
    public void Нет_данных_даёт_NoData()
    {
        var info = QuantizationAnalyzer.Analyze(G(-1), "meta-only.gguf");

        Assert.Equal(QuantizationSource.NoData, info.Source);
        Assert.Contains(info.Notes, n => n.Contains("нет данных"));
    }

    // ===================== 7. нормализация имени =====================

    [Theory]
    [InlineData("Qwen3.6-27B-A3B-CoderX-Q6_K_L", "Q6_K_L")]
    [InlineData("Ornith-1.5-27B-A3B-CoderX.Q6_K", "Q6_K")]
    [InlineData("Qwen3-Embedding-8B.i1-Q4_K_M", "Q4_K_M")]
    [InlineData("Qwen3.8-27B", "")]
    [InlineData("Ornith-1.5-35B-A3B", "")]
    public void Извлечение_токена_из_имени(string name, string expected)
    {
        var info = QuantizationAnalyzer.Analyze(G(18, T("blk.0.a.weight", 14, 256, 256)), name);
        Assert.Equal(expected, info.NameToken);
    }

    [Fact]
    public void Q6_K_L_нормализуется_в_метку_Q6_K()
    {
        var info = QuantizationAnalyzer.Analyze(
            G(18, T("blk.0.a.weight", 14, 256, 256)), "Qwen3.6-27B-A3B-CoderX-Q6_K_L");
        Assert.Equal("Q6_K_L", info.NameToken);
        Assert.True(info.NameMatches);
    }

    // ===================== приоритет метаданных над догадкой =====================

    [Fact]
    public void Метаданные_приоритетнее_догадки()
    {
        // file_type=Q4_K_M (15), но все веса q8_0 → значение всё равно из метаданных.
        var g = G(15,
            T("blk.0.a.weight", 8, 32, 32),
            T("blk.1.a.weight", 8, 32, 32));

        var info = QuantizationAnalyzer.Analyze(g, "model.gguf");

        Assert.Equal("Q4_K_M", info.Label);
        Assert.Equal(QuantizationSource.Metadata, info.Source);
        Assert.Contains(info.Notes, n => n.Contains("доминирует q8_0")); // расхождение озвучено, но не подменило метку
    }
}
