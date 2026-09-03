using System.IO;
using LlmScanHelper.Models.Settings;
using Xunit;

namespace LlmScanHelper.Tests;

/// <summary>
/// ТЗ2: миграция старых settings.json (bool ReasoningChecked) → ReasoningMode (on/off/auto).
/// Гарантирует, что при обновлении приложение не потеряет выбранный режим рассуждений.
/// </summary>
public class SettingsMigrationTests
{
  private static string TempSettingsFile(string json)
  {
    string tmp = Path.Combine(Path.GetTempPath(), "llmscan_test_" + Guid.NewGuid().ToString("N") + ".json");
    File.WriteAllText(tmp, json);
    return tmp;
  }

  [Fact]
  public void Migrates_Old_ReasoningChecked_To_ReasoningMode()
  {
    // Старый формат: ReasoningChecked (bool), поля ReasoningMode нет.
    string oldJson = """
      {
        "SettingsVersion": "4.0",
        "Catalogs": ["W:\\Models"],
        "PerModel": {
          "W:\\Models\\on.gguf":  { "ReasoningChecked": true,  "ReasonBudget": 4096 },
          "W:\\Models\\off.gguf": { "ReasoningChecked": false, "ReasonBudget": 8192 }
        }
      }
      """;
    string tmp = TempSettingsFile(oldJson);
    try
    {
      var store = new SettingsStore(tmp);
      store.Load();

      var on = store.Settings.PerModel["W:\\Models\\on.gguf"];
      var off = store.Settings.PerModel["W:\\Models\\off.gguf"];

      Assert.Equal("on", on.ReasoningMode);
      Assert.Null(on.ReasoningChecked);
      Assert.Equal(4096, on.ReasonBudget);

      Assert.Equal("off", off.ReasoningMode);
      Assert.Null(off.ReasoningChecked);
      Assert.Equal(8192, off.ReasonBudget);
    }
    finally
    {
      if (File.Exists(tmp)) File.Delete(tmp);
    }
  }

  [Fact]
  public void New_File_Keeps_Default_Auto_Mode()
  {
    // Новый формат: ReasoningMode уже задан, мостика нет.
    string newJson = """
      {
        "SettingsVersion": "4.0",
        "Catalogs": ["W:\\Models"],
        "PerModel": {
          "W:\\Models\\auto.gguf": { "ReasoningMode": "auto", "ReasonBudget": 0 }
        }
      }
      """;
    string tmp = TempSettingsFile(newJson);
    try
    {
      var store = new SettingsStore(tmp);
      store.Load();

      var auto = store.Settings.PerModel["W:\\Models\\auto.gguf"];
      Assert.Equal("auto", auto.ReasoningMode);
      Assert.Null(auto.ReasoningChecked);
    }
    finally
    {
      if (File.Exists(tmp)) File.Delete(tmp);
    }
  }

  [Fact]
  public void Migrated_File_RoundTrips_Without_ReasoningChecked()
  {
    // После миграции и сохранения в JSON не должно остаться мостика ReasoningChecked.
    string oldJson = """
      {
        "SettingsVersion": "4.0",
        "Catalogs": ["W:\\Models"],
        "PerModel": { "W:\\Models\\on.gguf": { "ReasoningChecked": true } }
      }
      """;
    string tmp = TempSettingsFile(oldJson);
    try
    {
      var store = new SettingsStore(tmp);
      store.Load();
      store.Save(store.Settings);   // сохраняем смигрированные настройки

      string saved = File.ReadAllText(tmp);
      Assert.Contains("\"ReasoningMode\": \"on\"", saved);
      Assert.DoesNotContain("ReasoningChecked", saved);
    }
    finally
    {
      if (File.Exists(tmp)) File.Delete(tmp);
    }
  }
}
