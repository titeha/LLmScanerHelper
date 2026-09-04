using LlmScanHelper.Models;
using LlmScanHelper.ViewModels;
using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// ТЗ2 + ТЗ3: сборка reasoning-флагов в команду.
///   ТЗ2: on → --reasoning on; off → --reasoning off; auto → флаг не передаётся.
///        Бюджет/сообщение — при on и auto (не off).
///   ТЗ3: --reasoning-budget — только при &gt; 0.
///        --reasoning-budget-message — **обязательно** при budget &gt; 0 (если поле пусто — дефолт).
///        Если budget = 0 → оба параметра не передаются.
///        Если reasoning = off → только --reasoning off, без budget/message.
/// GgufInfo создаётся в памяти (без парсинга файла) — тестируем логику сборки команды.
/// </summary>
public class BuildCommandReasoningTests
{
  private const string ModelPath = @"W:\LLStudio\Models\test\model.gguf";

  // Trailing space у budget-флага отличает его от --reasoning-budget-message.
  private const string BudgetFlag = "--reasoning-budget ";
  private const string MsgFlag = "--reasoning-budget-message ";

  private static MainViewModel CreateVm(bool hasReasoning = true)
  {
    var vm = new MainViewModel();
    vm._gguf = new GgufInfo
    {
      Arch = "llama", BlockCount = 48, ContextLength = 131072,
      FileSize = 10_000_000_000L, HasReasoning = hasReasoning,
    };
    vm._currentPath = ModelPath;
    return vm;
  }

  // ==================== ТЗ2: режим on ====================

  [Fact]
  public void On_BudgetPositive_MessageSet_PassesAllThree()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "on";
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.Contains(BudgetFlag + "4096", cmd);
    Assert.Contains(MsgFlag + "\"" + AppDefaults.DefaultReasonBudgetMessage + "\"", cmd);
  }

  [Fact]
  public void On_BudgetPositive_MessageEmpty_UsesDefaultMessage()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "on";
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = "";  // пусто → ставим дефолт

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.Contains(BudgetFlag + "4096", cmd);
    Assert.Contains(MsgFlag + "\"" + AppDefaults.DefaultReasonBudgetMessage + "\"", cmd);
  }

  [Fact]
  public void On_BudgetZero_OmitsBothBudgetAndMessage()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "on";
    vm.ReasonBudget = 0;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  [Fact]
  public void On_BudgetZero_MessageEmpty_OnlyReasoningOn()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "on";
    vm.ReasonBudget = 0;
    vm.ReasonBudgetMessage = "";

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  // ==================== ТЗ2: режим off ====================

  [Fact]
  public void Off_OnlyReasoningOff_NoBudgetOrMessage()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "off";
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning off", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  // ==================== ТЗ2: режим auto ====================

  [Fact]
  public void Auto_NoReasoningFlag_ButBudgetAndMessage()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "auto";
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    // auto → --reasoning не передаётся (дефолт runtime)
    Assert.DoesNotContain("--reasoning ", cmd);
    // но бюджет и сообщение всё равно передаются
    Assert.Contains(BudgetFlag + "4096", cmd);
    Assert.Contains(MsgFlag, cmd);
  }

  [Fact]
  public void Auto_BudgetPositive_MessageEmpty_UsesDefaultMessage()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "auto";
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = "";  // пусто → дефолт

    string cmd = vm.BuildCommand(ModelPath);

    Assert.DoesNotContain("--reasoning ", cmd);
    Assert.Contains(BudgetFlag + "4096", cmd);
    Assert.Contains(MsgFlag + "\"" + AppDefaults.DefaultReasonBudgetMessage + "\"", cmd);
  }

  [Fact]
  public void Auto_BudgetZero_OmitsBothFlags()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "auto";
    vm.ReasonBudget = 0;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.DoesNotContain("--reasoning ", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  [Fact]
  public void Auto_BudgetZero_MessageEmpty_NoReasoningFlags()
  {
    var vm = CreateVm();
    vm.ReasoningMode = "auto";
    vm.ReasonBudget = 0;
    vm.ReasonBudgetMessage = "";

    string cmd = vm.BuildCommand(ModelPath);

    Assert.DoesNotContain("--reasoning ", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  // ==================== Модель без reasoning ====================

  [Fact]
  public void ModelWithoutReasoning_NoReasoningFlags()
  {
    var vm = CreateVm(hasReasoning: false);
    vm.ReasoningMode = "on";
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.DoesNotContain("--reasoning", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }
}
