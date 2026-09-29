using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using LlmScanHelper.Models;

using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// S4: агрегация multi-file (splits). Метаданные — из шарда split.no == 0, тензоры — со всех шардов.
/// Фикстуры — из <see cref="GgufFixtures"/> (метаданные только в шарде 00001).
/// </summary>
public class GgufSplitReaderTests
{
    private static Dictionary<string, (int, object)> ModelKv(string arch = "llama", uint blockCount = 2, uint ctx = 4096)
        => new()
        {
            ["general.architecture"] = (8, arch),
            [arch + ".block_count"] = (4, blockCount),
            [arch + ".context_length"] = (4, ctx),
        };

    private static GgufFixtures.TensorInfo T(string name, int typeId, params ulong[] dims)
        => new() { Name = name, TypeId = typeId, Dims = dims };

    private static string Shard(string dir, int no, int count) =>
        Path.Combine(dir, $"model-{no:D5}-of-{count:D5}.gguf");

    private static long FileLen(string path) => new FileInfo(path).Length;

    // ===================== 1. полный набор «как жизнь» =====================

    [Fact]
    public void Полный_набор_агрегируется()
    {
        using var tmp = new TempDir();
        var tensors = new Dictionary<int, IList<GgufFixtures.TensorInfo>>
        {
            // шард 00001 — только метаданные
            [2] = new List<GgufFixtures.TensorInfo> { T("per_layer_token_embd.weight", 8, 64) }, // q8_0: 68
            [3] = new List<GgufFixtures.TensorInfo>
            {
                T("blk.0.attn_q.weight", 0, 10), // f32: 40
                T("blk.1.attn_q.weight", 0, 10),
            },
        };
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 3, ModelKv("qwen4exp", 3),
            tensorsByShard: tensors, splitTensorsCount: 3);

        var g = GgufInfo.Read(Shard(tmp.Dir, 1, 3));

        Assert.Equal(SplitStatus.Complete, g.SplitStatus);
        Assert.Equal("qwen4exp", g.Arch);
        Assert.Equal(3, g.BlockCount);
        Assert.Equal(4096L, g.ContextLength);
        Assert.Equal(3, g.Tensors.Count);
        Assert.Equal(3, g.SplitTensorsCount);
        Assert.Empty(g.MissingShardPaths);

        Assert.Equal(148L, g.PayloadBytes);          // 68 + 40 + 40
        Assert.Equal(68L, g.UnknownBytes);           // per_layer_token_embd
        Assert.Equal(40L, g.LayerSize[0]);
        Assert.Equal(40L, g.LayerSize[1]);
        Assert.Equal(0L, g.EmbdSize);

        long expectedFileSize = FileLen(Shard(tmp.Dir, 1, 3)) + FileLen(Shard(tmp.Dir, 2, 3)) + FileLen(Shard(tmp.Dir, 3, 3));
        Assert.Equal(expectedFileSize, g.FileSize);
        Assert.Equal("", g.IntegrityNote);
    }

    // ===================== 2. чтение не-первого шарда =====================

    [Fact]
    public void Чтение_второго_шарда_берёт_метаданные_из_первого()
    {
        using var tmp = new TempDir();
        var tensors = new Dictionary<int, IList<GgufFixtures.TensorInfo>>
        {
            [2] = new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 0, 10) },
            [3] = new List<GgufFixtures.TensorInfo> { T("blk.1.a.weight", 0, 10) },
        };
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 3, ModelKv("qwen4exp", 48, 32768),
            tensorsByShard: tensors, splitTensorsCount: 2);

        var fromFirst = GgufInfo.Read(Shard(tmp.Dir, 1, 3));
        var fromSecond = GgufInfo.Read(Shard(tmp.Dir, 2, 3));

        Assert.Equal(SplitStatus.Complete, fromSecond.SplitStatus);
        Assert.Equal(fromFirst.Arch, fromSecond.Arch);
        Assert.Equal("qwen4exp", fromSecond.Arch);        // НЕ "llama" по умолчанию
        Assert.Equal(fromFirst.BlockCount, fromSecond.BlockCount);
        Assert.Equal(48, fromSecond.BlockCount);
        Assert.Equal(fromFirst.ContextLength, fromSecond.ContextLength);
        Assert.Equal(fromFirst.Tensors.Count, fromSecond.Tensors.Count);
        Assert.Equal(fromFirst.PayloadBytes, fromSecond.PayloadBytes);
        Assert.Equal(fromFirst.FileSize, fromSecond.FileSize);
    }

    // ===================== 3. пропущенный шард =====================

    [Fact]
    public void Пропущенный_средний_шард_даёт_Incomplete()
    {
        using var tmp = new TempDir();
        var tensors = new Dictionary<int, IList<GgufFixtures.TensorInfo>>
        {
            [2] = new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 0, 10) },
            [3] = new List<GgufFixtures.TensorInfo> { T("blk.1.a.weight", 0, 10), T("blk.1.b.weight", 0, 10) },
        };
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 3, ModelKv("llama", 3),
            tensorsByShard: tensors, splitTensorsCount: 3);
        File.Delete(Shard(tmp.Dir, 2, 3));

        var g = GgufInfo.Read(Shard(tmp.Dir, 1, 3));

        Assert.Equal(SplitStatus.Incomplete, g.SplitStatus);
        Assert.Equal(new[] { Shard(tmp.Dir, 2, 3) }, g.MissingShardPaths);
        Assert.Contains("split.tensors.count", g.IntegrityNote);
        Assert.Equal(2, g.Tensors.Count); // только шард 00003
    }

    // ===================== 4. чужой split.no =====================

    [Fact]
    public void Чужой_split_no_даёт_Mismatch()
    {
        using var tmp = new TempDir();
        var perShardKv = new Dictionary<int, IDictionary<string, (int, object)>>
        {
            [2] = new Dictionary<string, (int, object)> { ["split.no"] = (2, (ushort)5) },
        };
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 3, ModelKv("llama", 2),
            tensorsByShard: new Dictionary<int, IList<GgufFixtures.TensorInfo>>
            {
                [2] = new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 0, 10) },
            },
            perShardKv: perShardKv, splitTensorsCount: 1);

        var g = GgufInfo.Read(Shard(tmp.Dir, 1, 3));

        Assert.Equal(SplitStatus.Mismatch, g.SplitStatus);
        Assert.Contains("split.no", g.IntegrityNote);
        Assert.Contains("model-00002-of-00003.gguf", g.IntegrityNote);
    }

    // ===================== 5. дубликат имени тензора между шардами =====================

    [Fact]
    public void Дубликат_тензора_между_шардами_даёт_Mismatch()
    {
        using var tmp = new TempDir();
        var tensors = new Dictionary<int, IList<GgufFixtures.TensorInfo>>
        {
            [1] = new List<GgufFixtures.TensorInfo> { T("dup.weight", 0, 10) },
            [2] = new List<GgufFixtures.TensorInfo> { T("dup.weight", 0, 10) },
        };
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 2, ModelKv("llama", 2),
            tensorsByShard: tensors, splitTensorsCount: 2);

        var g = GgufInfo.Read(Shard(tmp.Dir, 1, 2));

        Assert.Equal(SplitStatus.Mismatch, g.SplitStatus);
        Assert.Contains("дублируется", g.IntegrityNote);
    }

    // ===================== 6. лишний файл не ломает агрегат =====================

    [Fact]
    public void Лишний_файл_не_ломает_агрегат()
    {
        using var tmp = new TempDir();
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 3, ModelKv("llama", 2),
            tensorsByShard: new Dictionary<int, IList<GgufFixtures.TensorInfo>>
            {
                [2] = new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 0, 10) },
            },
            splitTensorsCount: 1);

        // Похожий на шард, но из другого набора (count = 9) — не должен ломать.
        GgufFixtures.Write(Path.Combine(tmp.Dir, "model-00009-of-00009.gguf"),
            new Dictionary<string, (int, object)>
            {
                ["split.no"] = (2, (ushort)8),
                ["split.count"] = (2, (ushort)9),
                ["split.tensors.count"] = (5, 0),
            },
            new List<GgufFixtures.TensorInfo>());

        var g = GgufInfo.Read(Shard(tmp.Dir, 1, 3));

        Assert.Equal(SplitStatus.Complete, g.SplitStatus);
        Assert.Single(g.Tensors);
    }

    // ===================== 7. одиночная модель =====================

    [Fact]
    public void Одиночная_модель_NotApplicable_и_совпадает_с_S2()
    {
        using var tmp = new TempDir();
        var path = Path.Combine(tmp.Dir, "single.gguf");
        GgufFixtures.Write(path, ModelKv("llama", 2),
            new List<GgufFixtures.TensorInfo>
            {
                T("blk.0.a.weight", 0, 10),
                T("token_embd.weight", 8, 32),
            });

        var viaReader = GgufInfo.Read(path);
        var viaS2 = GgufInfo.ParseSingle(path);

        Assert.Equal(SplitStatus.NotApplicable, viaReader.SplitStatus);
        Assert.Equal(viaS2.Arch, viaReader.Arch);
        Assert.Equal(viaS2.BlockCount, viaReader.BlockCount);
        Assert.Equal(viaS2.ContextLength, viaReader.ContextLength);
        Assert.Equal(viaS2.Tensors.Count, viaReader.Tensors.Count);
        Assert.Equal(viaS2.EmbdSize, viaReader.EmbdSize);
        Assert.Equal(viaS2.MtpSize, viaReader.MtpSize);
        Assert.Equal(viaS2.PayloadBytes, viaReader.PayloadBytes);
        Assert.Equal(viaS2.FileSize, viaReader.FileSize);
        Assert.Equal(viaS2.IntegrityNote, viaReader.IntegrityNote);
        Assert.Equal(viaS2.LayerSize, viaReader.LayerSize);
    }

    // ===================== 8. перегрузка Read(ModelEntry) =====================

    [Fact]
    public void Read_ModelEntry_использует_ShardPaths()
    {
        using var tmp = new TempDir();
        GgufFixtures.WriteSplitSet(tmp.Dir, "model", 3, ModelKv("qwen4exp", 48),
            tensorsByShard: new Dictionary<int, IList<GgufFixtures.TensorInfo>>
            {
                [3] = new List<GgufFixtures.TensorInfo> { T("blk.0.a.weight", 0, 10) },
            },
            splitTensorsCount: 1);

        var entry = new ModelEntry
        {
            FullPath = Shard(tmp.Dir, 2, 3),
            IsSplit = true,
            ShardPaths = new[] { Shard(tmp.Dir, 1, 3), Shard(tmp.Dir, 2, 3), Shard(tmp.Dir, 3, 3) },
        };

        var g = GgufModelReader.Read(entry);

        Assert.Equal(SplitStatus.Complete, g.SplitStatus);
        Assert.Equal("qwen4exp", g.Arch);
        Assert.Equal(48, g.BlockCount);
        Assert.Single(g.Tensors);
    }
}
