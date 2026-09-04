using System.IO;
using LlmScanHelper.Models;
using LlmScanHelper.Models.Settings;
using LlmScanHelper.ViewModels;
using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// ТЗ1: общий список сообщений бюджета reasoning (settings.json, раздел ReasonBudgetMessages).
/// Пустой список сидится стандартным текстом; ручная правка добавляет значение (без дублей).
/// </summary>
public class ReasonBudgetMessageListTests
{
  [Fact]
  public void Empty_List_Seeds_With_Default_Message()
  {
    var vm = new MainViewModel();

    // Пустой список сидится стандартным текстом (поле предзаполнено им же)
    Assert.Single(vm.ReasonBudgetMessages);
    Assert.Equal(AppDefaults.DefaultReasonBudgetMessage, vm.ReasonBudgetMessages[0]);
    Assert.Equal(AppDefaults.DefaultReasonBudgetMessage, vm.ReasonBudgetMessage);
  }

  [Fact]
  public void Editing_Message_Adds_To_Shared_List()
  {
    var vm = new MainViewModel();

    // Ручная правка: новое значение добавляется в общий список
    vm.ReasonBudgetMessage = "Custom budget message";
    Assert.Contains("Custom budget message", vm.ReasonBudgetMessages);

    // Повторное значение не дублируется
    int count = vm.ReasonBudgetMessages.Count;
    vm.ReasonBudgetMessage = "Custom budget message";
    Assert.Equal(count, vm.ReasonBudgetMessages.Count);
  }

  [Fact]
  public void Empty_Message_Not_Added_To_List()
  {
    var vm = new MainViewModel();

    // Пустое значение не попадает в список
    int count = vm.ReasonBudgetMessages.Count;
    vm.ReasonBudgetMessage = "";
    Assert.Equal(count, vm.ReasonBudgetMessages.Count);
  }

  [Fact]
  public void Shared_List_RoundTrips_Through_Store()
  {
    string tmp = Path.Combine(Path.GetTempPath(), "llmscan_test_" + Guid.NewGuid().ToString("N") + ".json");
    try
    {
      string json = """
        {
          "SettingsVersion": "4.0",
          "Catalogs": ["W:\\Models"],
          "ReasonBudgetMessages": ["msg one", "msg two"]
        }
        """;
      File.WriteAllText(tmp, json);

      var store = new SettingsStore(tmp);
      store.Load();

      Assert.Equal(new[] { "msg one", "msg two" }, store.Settings.ReasonBudgetMessages);
    }
    finally
    {
      if (File.Exists(tmp)) File.Delete(tmp);
    }
  }
}
