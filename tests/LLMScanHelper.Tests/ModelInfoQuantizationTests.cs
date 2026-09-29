using System.Collections.Generic;
using System.Linq;

using LlmScanHelper.Models;
using LlmScanHelper.Models.Command;
using LlmScanHelper.ViewModels;

using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// S6: вывод инфо-строк в VM (квантование из Core, а не KV-кэш) и строка «Файлы шарда».
/// GgufInfo создаётся в памяти и подставляется в VM через internal-хук _gguf/_currentPath;
/// реальный GGUF-файл при этом не парсится.
/// </summary>
public class ModelInfoQuantizationTests
{
    // ---- вспомогательные ----

    private static GgufTensorInfo T(string name, int typeId, params long[] dims)
    {
        var d = new long[] { 1, 1, 1, 1 };
        for (int i = 0; i < dims.Length && i < 4; i++) d[i] = dims[i];
        return new GgufTensorInfo(name, d, (uint)typeId, 0, GgmlTypes.Bytes(d, (uint)typeId));
    }

    private static GgufInfo MetaGguf(int fileType, params GgufTensorInfo[] tensors)
        => new() { FileType = fileType, BlockCount = 48, Tensors = tensors.ToList() };

    private static MainViewModel VmWith(GgufInfo g, string fileName)
    {
        var vm = TestVm.New();
        vm.RefreshModelInfoForTest(g, fileName);
        return vm;
    }

    // ==================== 1. Кантование из Core, а не KV-кэш (Q1) ====================

    [Fact]
    public void InfoQuantization_равно_метке_из_Core_а_не_KvK()
    {
        // file_type=18 → Q6_K; метаданные, источник «из GGUF».
        var g = MetaGguf(18,
            T("blk.0.attn_q.weight", 14, 256, 256),   // q6_K
            T("blk.0.attn_k.weight", 14, 256, 256),
            T("blk.0.attn_v.weight", 8, 256, 256));    // q8_0 (для распределения)

        var vm = VmWith(g, "Qwen3.8-Flash-Next-UD-Q6_K_XL");

        Assert.Equal("Q6_K", vm.InfoQuantization);
        Assert.NotEqual(vm.KvK, vm.InfoQuantization); // KV-кэш при этом «q8_0», не подменено
    }

    [Fact]
    public void Тултип_кантования_содержит_источник_и_details()
    {
        var g = MetaGguf(18,
            T("blk.0.attn_q.weight", 14, 256, 256),
            T("blk.0.attn_v.weight", 8, 256, 256));

        var vm = VmWith(g, "Qwen3.8-Flash-Next-UD-Q6_K_XL");

        Assert.Contains("из GGUF", vm.InfoQuantizationTooltip);      // источник
        Assert.Contains("bpw", vm.InfoQuantizationTooltip);          // bpw
        Assert.Contains("распределение типов", vm.InfoQuantizationTooltip); // доли типов
    }

    [Fact]
    public void Тултип_оценка_по_весам_при_отсутствии_файл_тайп()
    {
        // general.file_type нет → догадка по body-тензорам, источник «оценка».
        var g = MetaGguf(-1,
            T("blk.0.attn_q.weight", 8, 32, 32),
            T("blk.1.attn_q.weight", 8, 32, 32));

        var vm = VmWith(g, "Ornith-1.5-9B-Q8_0");

        Assert.Equal("Q8_0", vm.InfoQuantization);
        Assert.Contains("оценка по весам", vm.InfoQuantizationTooltip);
    }

    // ==================== 2. Строка «Файлы шарда» (S3/S4) ====================

    [Fact]
    public void Одиночная_модель_показывает_1_файл()
    {
        var g = MetaGguf(18, T("blk.0.attn_q.weight", 14, 256, 256));

        var vm = VmWith(g, "Qwen3.8-27B-Q6_K.gguf");

        Assert.Equal("1 файл", vm.InfoShards);
        Assert.Equal("", vm.InfoShardWarning);
    }

    [Fact]
    public void Шесть_шардов_показывает_число_с_маркером_shards()
    {
        var g = new GgufInfo
        {
            FileType = 18,
            BlockCount = 48,
            SplitStatus = SplitStatus.Complete,
            SplitCount = 6,
            MissingShardPaths = Array.Empty<string>(),
        };

        var vm = VmWith(g, "Qwen3.8-Flash-Next-00001-of-00006.gguf");

        Assert.Equal("6 файлов (shards)", vm.InfoShards);
        Assert.Equal("", vm.InfoShardWarning);
    }

    [Fact]
    public void Неполный_набор_даёт_предупреждение_со_списком()
    {
        var missing = new[]
        {
            "Qwen3.8-Flash-Next-00002-of-00006.gguf",
            "Qwen3.8-Flash-Next-00004-of-00006.gguf",
        };
        var g = new GgufInfo
        {
            FileType = 18,
            BlockCount = 48,
            SplitStatus = SplitStatus.Incomplete,
            SplitCount = 6,
            MissingShardPaths = missing,
        };

        var vm = VmWith(g, "Qwen3.8-Flash-Next-00001-of-00006.gguf");

        Assert.Contains("⚠", vm.InfoShardWarning);
        Assert.Contains("00002-of-00006.gguf", vm.InfoShardWarning);
        Assert.Contains("00004-of-00006.gguf", vm.InfoShardWarning);
    }

    // ==================== 3. «Учтено N GiB» (Q5) ====================

    [Fact]
    public void InfoUnaccounted_заполняется_только_при_наличии_байт()
    {
        var withBytes = VmWith(MetaGguf(18, T("blk.0.a.weight", 14, 256, 256)), "m.gguf");
        withBytes._gguf!.UnknownBytes = 2L * 1024 * 1024 * 1024;
        withBytes.RefreshModelInfoForTest(withBytes._gguf, "m.gguf");
        Assert.Contains("не учтено", withBytes.InfoUnaccounted);

        var empty = VmWith(MetaGguf(18, T("blk.0.a.weight", 14, 256, 256)), "m.gguf");
        Assert.Equal("", empty.InfoUnaccounted);
    }

    // ==================== 4. BuildWarnings: нехватка шарда (S6) ====================

    private static List<string> Warns(GgufInfo g)
        => LlamaServerCommandBuilder.BuildWarnings(new LlamaServerParams(), g);

    private static bool IsShardWarning(string w)
        => w.Contains("шард", StringComparison.Ordinal) ||
           w.Contains("не хватает", StringComparison.Ordinal);

    [Fact]
    public void BuildWarnings_Incomplete_даёт_предупреждение_о_шардах()
    {
        var g = new GgufInfo
        {
            SplitStatus = SplitStatus.Incomplete,
            MissingShardPaths = new[] { "model-00002-of-00006.gguf" },
        };

        var warnings = Warns(g);

        Assert.Contains(warnings, IsShardWarning);
        Assert.Contains("00002-of-00006.gguf", string.Join("\n", warnings));
    }

    [Fact]
    public void BuildWarnings_Mismatch_даёт_предупреждение_о_шардах()
    {
        var g = new GgufInfo { SplitStatus = SplitStatus.Mismatch };

        Assert.Contains(Warns(g), IsShardWarning);
    }

    [Fact]
    public void BuildWarnings_Complete_без_предупреждения_о_шардах()
    {
        var g = new GgufInfo { SplitStatus = SplitStatus.Complete, SplitCount = 6 };

        Assert.DoesNotContain(Warns(g), IsShardWarning);
    }

    [Fact]
    public void BuildWarnings_Один_файл_без_предупреждения_о_шардах()
    {
        var g = new GgufInfo { SplitStatus = SplitStatus.NotApplicable };

        Assert.DoesNotContain(Warns(g), IsShardWarning);
    }
}
