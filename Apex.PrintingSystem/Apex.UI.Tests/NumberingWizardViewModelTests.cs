using Apex.Services.Numbering;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Characterization tests for <see cref="NumberingWizardViewModel"/> — they lock the
/// current behaviour of the pure, dialog-free logic (workflow modes, digit-style and
/// numbering-mode mutual exclusion, copies flag) so the file can later be decomposed
/// safely without changing behaviour. The ViewModel is constructed with a real
/// <see cref="NumberingService"/> (parameterless).
/// </summary>
public class NumberingWizardViewModelTests
{
    private static NumberingWizardViewModel NewVm() => new(new NumberingService());

    [Fact]
    public void DefaultState_IsPrepareMode()
    {
        var vm = NewVm();
        Assert.True(vm.IsPrepareMode);
        Assert.False(vm.IsLayoutMode);
        Assert.False(vm.IsExecuteMode);
    }

    [Fact]
    public void Defaults_LinearAndWesternDigits()
    {
        var vm = NewVm();
        Assert.True(vm.IsLinearMode);
        Assert.False(vm.IsImposedMode);
        Assert.True(vm.UseWesternDigits);
        Assert.False(vm.UseArabicDigits);
    }

    [Fact]
    public void DigitStyle_IsMutuallyExclusive()
    {
        var vm = NewVm();

        vm.UseArabicDigits = true;
        Assert.True(vm.UseArabicDigits);
        Assert.False(vm.UseWesternDigits);

        vm.UseWesternDigits = true;
        Assert.True(vm.UseWesternDigits);
        Assert.False(vm.UseArabicDigits);
    }

    [Fact]
    public void NumberingMode_IsMutuallyExclusive()
    {
        var vm = NewVm();

        vm.IsImposedMode = true;
        Assert.True(vm.IsImposedMode);
        Assert.False(vm.IsLinearMode);

        vm.IsLinearMode = true;
        Assert.True(vm.IsLinearMode);
        Assert.False(vm.IsImposedMode);
    }

    [Fact]
    public void HasMultipleCopies_ReflectsCopiesCount()
    {
        var vm = NewVm();
        Assert.False(vm.HasMultipleCopies);     // default 1

        vm.CopiesCount = 3;
        Assert.True(vm.HasMultipleCopies);

        vm.CopiesCount = 1;
        Assert.False(vm.HasMultipleCopies);
    }

    [Fact]
    public void NumberingRange_DefaultsAreSane()
    {
        var vm = NewVm();
        Assert.Equal(1, vm.StartNumber);
        Assert.Equal(100, vm.TotalNumbers);
    }
}
