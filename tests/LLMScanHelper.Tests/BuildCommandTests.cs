using LlmScanHelper.Models;
using LlmScanHelper.ViewModels;
using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// Регрессионные тесты сборки строки запуска llama-server (MainViewModel.BuildCommand).
/// GgufInfo создаётся в памяти (без парсинга файла) — тестируем именно логику
/// сборки команды, а не парсер GGUF.
/// </summary>
public class BuildCommandTests
{
  private const string ModelPath = @"W:\LLStudio\Models\test\model.gguf";

  /// <summary>VM с уже «загруженной» моделью (без реального парсинга файла).</summary>
  private static MainViewModel CreateVm(GgufInfo gguf)
  {
    var vm = new MainViewModel();
    vm._gguf = gguf;
    vm._currentPath = ModelPath;
    return vm;
  }

  private static GgufInfo Gguf(bool hasReasoning = false, long mtpSize = 0, bool hasChatTemplate = false)
    => new()
    {
      Arch = "llama",
      BlockCount = 48,
      ContextLength = 131072,
      KvHeads = 8,
      HeadDim = 128,
      FileSize = 10_000_000_000L,
      HasReasoning = hasReasoning,
      MtpSize = mtpSize,          // HasMtp вычисляется как MtpSize > 0
      HasChatTemplate = hasChatTemplate,
    };

  // ==================== Структура команды (не reasoning) ====================

  [Fact]
  public void Builds_Basic_Auto_Command_Without_Reasoning()
  {
    var vm = CreateVm(Gguf(hasReasoning: false));
    string cmd = vm.BuildCommand(ModelPath);

    Assert.StartsWith("llama-server -m \"" + ModelPath + "\"", cmd);

    // AUTO-режим: --fit on, без -ngl и --tensor-split
    Assert.Contains("--split-mode layer", cmd);
    Assert.Contains("--fit on", cmd);
    Assert.DoesNotContain("-ngl", cmd);
    Assert.DoesNotContain("--tensor-split", cmd);

    // Контекст / KV / attention
    Assert.Contains("-c 32768", cmd);
    Assert.Contains("--cache-type-k q8_0", cmd);
    Assert.Contains("--cache-type-v q8_0", cmd);
    Assert.Contains("-fa auto", cmd);

    // Производительность
    Assert.Contains("-b 2048", cmd);
    Assert.Contains("-ub 512", cmd);
    Assert.Contains("-np 1", cmd);

    // Сервер
    Assert.Contains("--host 127.0.0.1", cmd);
    Assert.Contains("--port 12345", cmd);
    Assert.Contains("--timeout 7200", cmd);
    Assert.Contains("--sse-ping-interval 15", cmd);
    Assert.Contains("--cache-prompt", cmd);
    Assert.Contains("--perf", cmd);

    // Отсутствующие по умолчанию
    Assert.DoesNotContain("--reasoning", cmd);
    Assert.DoesNotContain("--jinja", cmd);
    Assert.DoesNotContain("--mmproj", cmd);
    Assert.DoesNotContain("--alias", cmd);
    Assert.DoesNotContain("--spec-type", cmd);
    Assert.DoesNotContain("--temp", cmd);
  }

  [Fact]
  public void Omits_Optional_Flags_When_Zero()
  {
    var vm = CreateVm(Gguf());
    // Threads/ThreadsBatch = 0 по умолчанию → флаги не выдаются
    string cmd = vm.BuildCommand(ModelPath);

    Assert.DoesNotContain(" -t ", " " + cmd + " ");
    Assert.DoesNotContain(" -tb ", " " + cmd + " ");
    // CacheReuse = 0 → нет --cache-reuse
    Assert.DoesNotContain("--cache-reuse", cmd);
  }

  [Fact]
  public void Includes_Alias_When_Set()
  {
    var vm = CreateVm(Gguf());
    vm.AliasText = "My_Model";
    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--alias \"My_Model\"", cmd);
  }
}
