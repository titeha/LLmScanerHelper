using System.Windows.Input;

using LlmScanHelper.ViewModels;

using Xunit;

namespace LlmScanHelper.Tests;

public class HeaderCommandsTests
{
    private static MainViewModel NewVm() => new MainViewModel();

    [Fact]
    public void OpenSettingsCommand_is_exposed_on_viewmodel()
    {
        var vm = NewVm();
        Assert.NotNull(vm.OpenSettingsCommand);
        Assert.True(vm.OpenSettingsCommand is ICommand);
    }

    [Fact]
    public void OpenHelpCommand_is_exposed_on_viewmodel()
    {
        var vm = NewVm();
        Assert.NotNull(vm.OpenHelpCommand);
        Assert.True(vm.OpenHelpCommand is ICommand);
    }

    [Fact]
    public void Header_commands_are_distinct()
    {
        var vm = NewVm();
        var settings = vm.OpenSettingsCommand;
        var help = vm.OpenHelpCommand;
        Assert.NotNull(settings);
        Assert.NotNull(help);
        Assert.NotSame(settings, help);
    }
}
