using System.Threading.Tasks;

using LlmScanHelper.UI.Services;
using LlmScanHelper.ViewModels;

namespace LlmScanHelper.Tests;

/// <summary>Пустые реализации для тестов (UI в тестах не нужен).</summary>
public sealed class FakeClipboard : IClipboard { public void SetText(string text) { } }
public sealed class FakeFolderPicker : IFolderPicker { public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null); }
public sealed class FakeUiWindows : IUiWindows { public void OpenSettings(MainViewModel vm) { } public void OpenHelp(MainViewModel vm) { } }

public static class TestVm
{
  public static MainViewModel New() => new(new FakeClipboard(), new FakeUiWindows(), new FakeFolderPicker());
}
