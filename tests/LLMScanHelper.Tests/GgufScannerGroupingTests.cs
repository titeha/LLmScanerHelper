using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using LlmScanHelper.Models;

using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// S3: группировка шардов в сканере каталога. Работает только по именам и размерам,
/// содержимое файлов не читается. Каталоги — временные (<see cref="TempDir"/>).
/// </summary>
public class GgufScannerGroupingTests
{
    private static string MakeFile(string dir, string name, int size = 0)
    {
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllBytes(path, new byte[size]);
        return path;
    }

    // ===================== группировка шардов =====================

    [Fact]
    public void Шесть_шардов_дают_одну_запись()
    {
        using var tmp = new TempDir();
        for (int i = 1; i <= 6; i++)
            MakeFile(tmp.Dir, $"model-{i:D5}-of-{6:D5}.gguf", i);

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Null(res.Error);
        var m = Assert.Single(res.Models);
        Assert.True(m.IsSplit);
        Assert.Equal("model", m.FileName);
        Assert.Equal(6, m.ShardCount);
        Assert.Equal(6, m.ShardPaths.Count);
        Assert.Equal(21L, m.TotalSizeBytes);
        Assert.Empty(m.MissingShards);
        Assert.Equal(ModelKind.Main, m.Kind);

        // FullPath — именно шард 00001 (его грузит llama.cpp), ShardPaths упорядочены.
        Assert.Equal(Path.Combine(tmp.Dir, "model-00001-of-00006.gguf"), m.FullPath);
        Assert.Equal("model-00001-of-00006.gguf", Path.GetFileName(m.ShardPaths[0]));
        Assert.Equal("model-00006-of-00006.gguf", Path.GetFileName(m.ShardPaths[5]));
    }

    [Fact]
    public void Неполный_набор_называет_отсутствующие_номера()
    {
        using var tmp = new TempDir();
        foreach (int i in new[] { 1, 3, 4 })
            MakeFile(tmp.Dir, $"model-{i:D5}-of-{6:D5}.gguf", 1);

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Null(res.Error);
        var m = Assert.Single(res.Models);
        Assert.Equal(6, m.ShardCount);
        Assert.Equal(new[] { 2, 5, 6 }, m.MissingShards);
        Assert.Equal(3, m.ShardPaths.Count);
        Assert.Equal(3L, m.TotalSizeBytes);
    }

    [Fact]
    public void Отсутствие_шарда_1_не_ломает_запись()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "model-00002-of-00004.gguf", 1);
        MakeFile(tmp.Dir, "model-00003-of-00004.gguf", 1);

        var res = GgufScannerService.Scan(tmp.Dir);

        var m = Assert.Single(res.Models);
        Assert.True(m.IsSplit);
        // FullPath = минимальный найденный шард, а отсутствие 1 видно в MissingShards.
        Assert.Equal(Path.Combine(tmp.Dir, "model-00002-of-00004.gguf"), m.FullPath);
        Assert.Equal(new[] { 1, 4 }, m.MissingShards);
    }

    [Fact]
    public void Смешанная_папка_группирует_шарды_и_mmproj()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "alpha-00001-of-00002.gguf", 10);
        MakeFile(tmp.Dir, "alpha-00002-of-00002.gguf", 20);
        MakeFile(tmp.Dir, "beta-00001-of-00003.gguf", 5);
        MakeFile(tmp.Dir, "lonely.gguf", 7);
        MakeFile(tmp.Dir, "mmproj-BF16.gguf", 3);
        MakeFile(tmp.Dir, "mmproj-proj-00001-of-00002.gguf", 1);
        MakeFile(tmp.Dir, "mmproj-proj-00002-of-00002.gguf", 2);

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Null(res.Error);
        Assert.Equal(3, res.Models.Count); // alpha, beta, lonely — mmproj не модели
        Assert.DoesNotContain(res.Models, m => m.FileName.StartsWith("mmproj", StringComparison.OrdinalIgnoreCase));

        var alpha = res.Models.Single(m => m.FileName == "alpha");
        Assert.Equal(30L, alpha.TotalSizeBytes);

        // В LocalMmproj — по одному представителю на набор проектора (первый шард).
        Assert.Equal(2, alpha.LocalMmproj.Count);
        Assert.Contains(Path.Combine(tmp.Dir, "mmproj-BF16.gguf"), alpha.LocalMmproj);
        Assert.Contains(Path.Combine(tmp.Dir, "mmproj-proj-00001-of-00002.gguf"), alpha.LocalMmproj);
        Assert.DoesNotContain(Path.Combine(tmp.Dir, "mmproj-proj-00002-of-00002.gguf"), alpha.LocalMmproj);

        // Полный список (все шарды проекторов) доступен отдельно.
        Assert.Equal(3, alpha.MmprojShardPaths.Count);
        Assert.Contains(Path.Combine(tmp.Dir, "mmproj-proj-00002-of-00002.gguf"), alpha.MmprojShardPaths);

        var beta = res.Models.Single(m => m.FileName == "beta");
        Assert.Equal(new[] { 2, 3 }, beta.MissingShards);
    }

    // ===================== draft =====================

    [Fact]
    public void Draft_файлы_помечаются_но_остаются_в_списке()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "Model-Q8_0.gguf");
        MakeFile(tmp.Dir, "mtp-Model-shared-Q8_0.gguf");
        MakeFile(tmp.Dir, "draft-Model.gguf");
        MakeFile(tmp.Dir, "Foo-draft-Bar.gguf");

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Equal(4, res.Models.Count); // скрытия нет
        Assert.Equal(ModelKind.Main, res.Models.Single(m => m.FileName == "Model-Q8_0").Kind);
        Assert.DoesNotContain("[draft]", res.Models.Single(m => m.FileName == "Model-Q8_0").DisplayName);
        Assert.Equal(ModelKind.Draft, res.Models.Single(m => m.FileName == "mtp-Model-shared-Q8_0").Kind);
        Assert.Equal(ModelKind.Draft, res.Models.Single(m => m.FileName == "draft-Model").Kind);
        Assert.Equal(ModelKind.Draft, res.Models.Single(m => m.FileName == "Foo-draft-Bar").Kind);
        // Q3 (S6): все draft-записи помечены суфксом [draft] в DisplayName.
        foreach (var d in new[] { "mtp-Model-shared-Q8_0", "draft-Model", "Foo-draft-Bar" })
            Assert.EndsWith(" [draft]", res.Models.Single(m => m.FileName == d).DisplayName);
    }

    [Fact]
    public void Draft_шард_помечается_по_stem()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "mtp-Model-00001-of-00002.gguf");
        MakeFile(tmp.Dir, "mtp-Model-00002-of-00002.gguf");

        var res = GgufScannerService.Scan(tmp.Dir);

        var m = Assert.Single(res.Models);
        Assert.True(m.IsSplit);
        Assert.Equal("mtp-Model", m.FileName);
        Assert.Equal(ModelKind.Draft, m.Kind);
        // Q3 (S6): draft-шард-набор тоже помечен суфксом [draft] в DisplayName.
        Assert.EndsWith(" [draft]", m.DisplayName);
    }

    // ===================== ложная группировка =====================

    [Fact]
    public void Имя_с_дефисами_и_цифрами_не_группируется()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "Qwen3.6-27B-A3B-CoderX-Q6_K_L.gguf");

        var res = GgufScannerService.Scan(tmp.Dir);

        var m = Assert.Single(res.Models);
        Assert.False(m.IsSplit);
        Assert.Equal("Qwen3.6-27B-A3B-CoderX-Q6_K_L", m.FileName);
        Assert.Equal(1, m.ShardCount);
        Assert.Single(m.ShardPaths);
    }

    [Fact]
    public void Суффикс_copy_и_простое_имя_не_группируются()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "model-00001-of-00006 copy.gguf");
        MakeFile(tmp.Dir, "model.gguf");

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Equal(2, res.Models.Count);
        Assert.All(res.Models, m => Assert.False(m.IsSplit));
    }

    [Fact]
    public void Патологический_номер_шарда_не_падает()
    {
        using var tmp = new TempDir();
        MakeFile(tmp.Dir, "model-12345-of-67890.gguf");

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Null(res.Error);
        var m = Assert.Single(res.Models);
        Assert.True(m.IsSplit);
        Assert.Contains(1, m.MissingShards);
        Assert.Equal(67889, m.MissingShards.Count);
    }

    [Fact]
    public void Одинаковый_stem_в_разных_папках_не_сливается()
    {
        using var tmp = new TempDir();
        var a = Path.Combine(tmp.Dir, "a");
        var b = Path.Combine(tmp.Dir, "b");
        MakeFile(a, "x-00001-of-00002.gguf");
        MakeFile(a, "x-00002-of-00002.gguf");
        MakeFile(b, "x-00001-of-00002.gguf");

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Equal(2, res.Models.Count);
        Assert.All(res.Models, m => Assert.True(m.IsSplit));

        var setA = res.Models.Single(m => m.FullPath == Path.Combine(a, "x-00001-of-00002.gguf"));
        var setB = res.Models.Single(m => m.FullPath == Path.Combine(b, "x-00001-of-00002.gguf"));
        Assert.Empty(setA.MissingShards);
        Assert.Equal(new[] { 2 }, setB.MissingShards);
    }

    // ===================== прочее =====================

    [Fact]
    public void Скан_не_читает_содержимое_файла()
    {
        using var tmp = new TempDir();
        // Заведомо не-GGUF содержимое: скан обязан выдать запись, не пытаясь разобрать файл.
        File.WriteAllBytes(Path.Combine(tmp.Dir, "broken.gguf"), new byte[] { 1, 2, 3, 4, 5 });

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Null(res.Error);
        Assert.Equal(5L, Assert.Single(res.Models).TotalSizeBytes);
    }

    [Fact]
    public void Регресс_одиночные_файлы_список_не_изменился()
    {
        using var tmp = new TempDir();
        var names = new[] { "aaa.gguf", "bbb.gguf", "ccc.gguf" };
        foreach (var n in names) MakeFile(tmp.Dir, n, 4);

        var res = GgufScannerService.Scan(tmp.Dir);

        Assert.Null(res.Error);
        Assert.Equal(names.Length, res.Models.Count);
        foreach (var n in names)
        {
            var stem = Path.GetFileNameWithoutExtension(n);
            var m = res.Models.Single(x => x.FileName == stem);
            Assert.Equal(Path.Combine(tmp.Dir, n), m.FullPath);
            Assert.False(m.IsSplit);
            Assert.Equal(1, m.ShardCount);
            Assert.Equal(4L, m.TotalSizeBytes);
            Assert.Equal("", m.Publisher);
            Assert.Equal(stem, m.DisplayName);
            Assert.Equal(Path.Combine(tmp.Dir, n), Assert.Single(m.ShardPaths));
        }

        // сортировка по DisplayName
        Assert.Equal(new[] { "aaa", "bbb", "ccc" }, res.Models.Select(m => m.FileName));
    }
}
