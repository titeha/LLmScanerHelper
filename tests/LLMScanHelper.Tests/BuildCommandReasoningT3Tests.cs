using LlmScanHelper.Models;
using LlmScanHelper.ViewModels;
using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// ТЗ3: --reasoning-budget — только при значении &gt; 0 (0 → не передаётся = unlimited в runtime).
/// --reasoning-budget-message — только при непустом значении.
/// Поле message по-прежнему предзаполнено стандартным текстом (AppDefaults).
/// </summary>
public class BuildCommandReasoningT3Tests
{
  private const string ModelPath = @"W:\LLStudio\Models\test\model.gguf";

  // Тailing space у budget-флага отличает его от --reasoning-budget-message.
  private const string BudgetFlag = "--reasoning-budget ";
  private const string MsgFlag = "--reasoning-budget-message ";

  private static MainViewModel CreateVm()
  {
    var vm = new MainViewModel();
    vm._gguf = new GgufInfo
    {
      Arch = "llama", BlockCount = 48, ContextLength = 131072,
      FileSize = 10_000_000_000L, HasReasoning = true,
    };
    vm._currentPath = ModelPath;
    return vm;
  }

  [Fact]
  public void On_BudgetPositive_MessageSet_PassesAllThree()
  {
    var vm = CreateVm();
    vm.ReasoningChecked = true;
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.Contains(BudgetFlag + "4096", cmd);
    Assert.Contains(MsgFlag + "\"" + AppDefaults.DefaultReasonBudgetMessage + "\"", cmd);
  }

  [Fact]
  public void On_BudgetZero_OmitsBudgetFlag()
  {
    var vm = CreateVm();
    vm.ReasoningChecked = true;
    vm.ReasonBudget = 0;                 // 0 → не передавать (unlimited)
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.Contains(MsgFlag, cmd);       // message всё равно передаётся
  }

  [Fact]
  public void On_MessageEmpty_OmitsMessageFlag()
  {
    var vm = CreateVm();
    vm.ReasoningChecked = true;
    vm.ReasonBudget = 8192;
    vm.ReasonBudgetMessage = "";         // пусто → не передавать

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.Contains(BudgetFlag + "8192", cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  [Fact]
  public void On_BudgetZero_MessageEmpty_OnlyReasoningOn()
  {
    var vm = CreateVm();
    vm.ReasoningChecked = true;
    vm.ReasonBudget = 0;
    vm.ReasonBudgetMessage = "";

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning on", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  [Fact]
  public void Off_NoBudgetOrMessage()
  {
    var vm = CreateVm();
    vm.ReasoningChecked = false;
    vm.ReasonBudget = 4096;
    vm.ReasonBudgetMessage = AppDefaults.DefaultReasonBudgetMessage;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.Contains("--reasoning off", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }

  [Fact]
  public void ModelWithoutReasoning_NoReasoningFlags()
  {
    var vm = CreateVm();
    vm._gguf = new GgufInfo
    {
      Arch = "llama", BlockCount = 48, ContextLength = 131072,
      FileSize = 10_000_000_000L, HasReasoning = false,
    };
    vm.ReasoningChecked = true;
    vm.ReasonBudget = 4096;

    string cmd = vm.BuildCommand(ModelPath);

    Assert.DoesNotContain("--reasoning", cmd);
    Assert.DoesNotContain(BudgetFlag, cmd);
    Assert.DoesNotContain(MsgFlag, cmd);
  }
}
