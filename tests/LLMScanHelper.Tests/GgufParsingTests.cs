using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using LlmScanHelper.Models;

using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// S2: честный разбор одного GGUF-файла. Размеры — по таблице S1, а не по дельте offset'ов;
/// каждая строка таблицы валидаций — отдельный тест. Фикстуры — из <see cref="GgufFixtures"/>.
/// </summary>
public class GgufParsingTests
{
    // ===================== вспомогательное =====================

    private static Dictionary<string, (int, object)> Meta(string arch, uint blockCount,
        params (string key, int type, object value)[] extra)
    {
        var kv = new Dictionary<string, (int, object)>
        {
            ["general.architecture"] = (8, arch),
            [arch + ".block_count"] = (4, blockCount),
        };
        foreach (var e in extra) kv[e.key] = (e.type, e.value);
        return kv;
    }

    private static GgufFixtures.TensorInfo T(string name, int typeId, params ulong[] dims) =>
        new() { Name = name, TypeId = typeId, Dims = dims };

    private static string WriteFile(TempDir tmp, IDictionary<string, (int, object)> kv,
        IList<GgufFixtures.TensorInfo> tensors, uint alignment = 32, uint version = 3,
        uint tailPadding = 0, long lastTensorOffsetBump = 0, bool writeData = true)
    {
        var path = Path.Combine(tmp.Dir, "model.gguf");
        GgufFixtures.Write(path, kv, tensors, alignment, version, tailPadding, lastTensorOffsetBump, writeData);
        return path;
    }

    // ===================== 1. Tensor-info: dims + Bytes =====================

    [Fact]
    public void Tensors_отдают_размеры_по_формуле_ggml()
    {
        using var tmp = new TempDir();
        var tensors = new List<GgufFixtures.TensorInfo>
        {
            T("token_embd.weight", 8, 4096, 4096),   // q8_0
            T("blk.0.attn_q.weight", 0, 4096),       // f32, 1-D
            T("blk.1.attn_q.weight", 14, 256, 4),    // q6_K
        };
        var path = WriteFile(tmp, Meta("llama", 2), tensors);

        var g = GgufInfo.Read(path);

        Assert.Equal(3, g.Tensors.Count);
        Assert.Equal(4096L * 4096L / 32 * 34, g.Tensors[0].Bytes);
        // dims дополнены единицами: [4096] → [4096,1,1,1]
        Assert.Equal(new long[] { 4096, 1, 1, 1 }, g.Tensors[1].Dims);
        Assert.Equal(4096L * 4, g.Tensors[1].Bytes);
        Assert.Equal(256L / 256 * 210 * 4, g.Tensors[2].Bytes);

        Assert.Equal(4096L * 4096L / 32 * 34, g.EmbdSize);
        Assert.Equal(4096L * 4, g.LayerSize[0]);
    }

    [Fact]
    public void Последний_тензор_не_включает_хвост_файла()
    {
        using var tmp = new TempDir();
        // Один слой 400 Б, «хвост» 999 Б не должен попасть в размер слоя.
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 0, 100) },
            tailPadding: 999);

        var g = GgufInfo.Read(path);

        Assert.Equal(400L, g.LayerSize[0]);      // 100 * 4, без padding
        Assert.Equal(0L, g.UnknownBytes);
    }

    // ===================== 2. dataStart: alignment и metadata-only =====================

    [Theory]
    [InlineData(32u)]
    [InlineData(64u)]
    public void Инвариант_padding_и_data_start_для_alignment(uint alignment)
    {
        using var tmp = new TempDir();
        var layout = GgufFixtures.Write(Path.Combine(tmp.Dir, "m.gguf"),
            Meta("llama", 1, ("general.alignment", 4, alignment)),
            new List<GgufFixtures.TensorInfo>
            {
                T("blk.0.a.weight", 14, 256, 2),  // q6_K: 210*2 = 420
                T("token_embd.weight", 8, 32),     // q8_0: 34
                T("output.weight", 0, 33),         // f32: 132
            }, alignment: alignment);

        var g = GgufInfo.Read(Path.Combine(tmp.Dir, "m.gguf"));

        Assert.Equal("", g.IntegrityNote);
        Assert.Equal(layout.DataStart, g.DataStart);
        Assert.Equal(0L, g.DataStart % alignment);
        long sum = g.EmbdSize + g.MtpSize + g.LayerSize.Sum() + g.UnknownBytes;
        Assert.Equal(layout.TotalTensorBytes, sum);
        Assert.Equal(420L, g.LayerSize[0]);
        Assert.Equal(34L, g.EmbdSize);
        Assert.Equal(132L, g.UnknownBytes);
    }

    [Fact]
    public void Metadata_only_не_выравнивает_data_start()
    {
        using var tmp = new TempDir();
        var kv = new Dictionary<string, (int, object)> { ["general.architecture"] = (8, "llama") };
        var layout = GgufFixtures.Write(Path.Combine(tmp.Dir, "m.gguf"), kv,
            new List<GgufFixtures.TensorInfo>(), alignment: 32);

        var g = GgufInfo.Read(Path.Combine(tmp.Dir, "m.gguf"));

        Assert.Empty(g.Tensors);
        Assert.NotEqual(0L, layout.HeaderEnd % 32);       // гарантия осмысленности теста
        Assert.Equal(layout.HeaderEnd, g.HeaderEnd);
        Assert.Equal(layout.HeaderEnd, g.DataStart);      // НЕ выровнено, т.к. n_tensors == 0
        Assert.Equal("", g.IntegrityNote);
    }

    // ===================== 3. Версия =====================

    [Theory]
    [InlineData(1u, "v1")]
    [InlineData(2u, "v2")]
    public void Неподдерживаемая_версия_бросает(uint version, string fragment)
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1), new List<GgufFixtures.TensorInfo>(), version: version);

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains(fragment, ex.Message);
    }

    [Fact]
    public void Версия_выше_максимальной_указывает_числа()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1), new List<GgufFixtures.TensorInfo>(), version: 4);

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("4", ex.Message);
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void Не_GGUF_бросает()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Dir, "bad.bin");
        File.WriteAllBytes(path, new byte[] { 0x47, 0x47, 0x55, 0x00, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("GGUF", ex.Message);
    }

    // ===================== 4. general.alignment =====================

    [Fact]
    public void Alignment_не_uint32_бросает()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1, ("general.alignment", 10, (ulong)32)),
            new List<GgufFixtures.TensorInfo>());

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("uint32", ex.Message);
    }

    [Fact]
    public void Alignment_ноль_бросает()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1, ("general.alignment", 4, (uint)0)),
            new List<GgufFixtures.TensorInfo>());

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("степень", ex.Message);
    }

    [Fact]
    public void Alignment_не_степень_двойки_бросает()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1, ("general.alignment", 4, (uint)33)),
            new List<GgufFixtures.TensorInfo>());

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("степень", ex.Message);
    }

    // ===================== 5. Валидации tensor-info =====================

    [Fact]
    public void NDims_больше_4_бросает()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("t", 0, 1, 1, 1, 1, 1) });

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("n_dims", ex.Message);
    }

    [Fact]
    public void Дубликат_имени_тензора_бросает()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("dup", 8, 32), T("dup", 8, 32) });

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("дубликат", ex.Message);
    }

    [Fact]
    public void Переполнение_произведения_размерностей_бросает()
    {
        using var tmp = new TempDir();
        // f32 (blck=1): ne[0] = long.MaxValue, ne[1] = 2 → произведение переполняет long.
        // Данные не пишем (иначе фикстура пыталась бы записать петабайты).
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("t", 0, (ulong)long.MaxValue, 2) },
            writeData: false);

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("переполня", ex.Message);
    }

    [Fact]
    public void Ne0_не_делится_на_blck_бросает_с_именем_и_типом()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("blk.0.bad.weight", 8, 33) }); // q8_0, blck=32

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("blk.0.bad.weight", ex.Message);
        Assert.Contains("q8_0", ex.Message);
    }

    [Theory]
    [InlineData(31)]   // в enum ggml, но нет в таблице S1
    [InlineData(99)]   // >= GGML_TYPE_COUNT (43)
    public void Неизвестный_тип_не_бросает(int typeId)
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("blk.0.x.weight", typeId, 32) });

        var g = GgufInfo.Read(path);

        Assert.Single(g.Tensors);
        Assert.Equal(0L, g.Tensors[0].Bytes);
        Assert.Equal(1, g.UnknownTypeTensors);
        Assert.Equal(0L, g.UnknownBytes);
        Assert.Equal(0L, g.LayerSize[0]);
    }

    [Fact]
    public void Offset_за_данными_файла_бросает()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 8, 32) },
            lastTensorOffsetBump: 1_000_000);

        var ex = Assert.Throws<Exception>(() => GgufInfo.Read(path));

        Assert.Contains("выходит за данные", ex.Message);
    }

    // ===================== 6. Учёт байт: UnknownBytes / per_layer =====================

    [Fact]
    public void UnknownBytes_учитывает_нераспознанные_тензоры()
    {
        using var tmp = new TempDir();
        var path = WriteFile(tmp, Meta("llama", 2),
            new List<GgufFixtures.TensorInfo>
            {
                T("per_layer_token_embd.weight", 8, 32),  // q8_0: 34 — не слой, не эмбеддинг
                T("blk.0.attn_q.weight", 0, 10),          // f32: 40
                T("token_embd.weight", 8, 32),            // q8_0: 34
            });

        var g = GgufInfo.Read(path);

        Assert.Equal(34L, g.UnknownBytes);
        Assert.Equal(1, g.UnknownTensors);
        Assert.Equal(40L, g.LayerSize[0]);            // per_layer НЕ попал в слой
        Assert.Equal(34L, g.EmbdSize);

        long classified = g.EmbdSize + g.MtpSize + g.LayerSize.Sum() + g.UnknownBytes;
        Assert.Equal(g.Tensors.Sum(t => t.Bytes), classified);
    }

    [Fact]
    public void Одинаковые_слои_дают_одинаковый_LayerSize()
    {
        using var tmp = new TempDir();
        const int layers = 4;
        var tensors = new List<GgufFixtures.TensorInfo>();
        for (int b = 0; b < layers; b++)
        {
            tensors.Add(T($"blk.{b}.attn_q.weight", 14, 256));  // q6_K: 210
            tensors.Add(T($"blk.{b}.ffn.weight", 14, 512));     // q6_K: 420
        }
        var path = WriteFile(tmp, Meta("llama", layers), tensors);

        var g = GgufInfo.Read(path);

        Assert.Equal(layers, g.LayerSize.Length);
        Assert.All(g.LayerSize, size => Assert.Equal(630L, size));
    }

    // ===================== 7. Инвариант целостности: заметка, не исключение =====================

    [Fact]
    public void Нарушение_инварианта_даёт_заметку_а_не_исключение()
    {
        using var tmp = new TempDir();
        // Данных больше, чем Σ nbytes + n·(align−1) — «файл дописан снаружи».
        var path = WriteFile(tmp, Meta("llama", 1),
            new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 8, 32) },
            tailPadding: 1000);

        var g = GgufInfo.Read(path);

        Assert.NotEqual("", g.IntegrityNote);
        Assert.Equal(34L, g.LayerSize[0]);
    }
}
