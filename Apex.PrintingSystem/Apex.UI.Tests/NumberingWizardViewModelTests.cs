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

    // ── Recent projects: a shop put an invoice design into the program and could not
    // get it out again. The list could open a project but never forget one, so the only
    // way was to delete the file in Windows and restart. These lock the way out.

    [Fact]
    public void RemoveRecentProject_TakesOnlyThatEntryOffTheList()
    {
        var vm = NewVm();
        vm.RecentProjects.Clear();
        vm.RecentProjects.Add(@"C:\jobs\invoice.apexnum");
        vm.RecentProjects.Add(@"C:\jobs\receipts.apexnum");

        vm.RemoveRecentProjectCommand.Execute(@"C:\jobs\invoice.apexnum");

        Assert.Equal(new[] { @"C:\jobs\receipts.apexnum" }, vm.RecentProjects);
    }

    [Fact]
    public void RemoveRecentProject_IgnoresNullAndEmpty()
    {
        var vm = NewVm();
        vm.RecentProjects.Clear();
        vm.RecentProjects.Add(@"C:\jobs\invoice.apexnum");

        vm.RemoveRecentProjectCommand.Execute(null);
        vm.RemoveRecentProjectCommand.Execute(string.Empty);

        Assert.Single(vm.RecentProjects);
    }

    /// <summary>
    /// Clearing an empty list must not raise the confirmation prompt — this test would
    /// hang on a modal dialog if it did, which is exactly the guard being locked in.
    /// </summary>
    [Fact]
    public void ClearRecentProjects_OnEmptyList_DoesNothingAndDoesNotPrompt()
    {
        var vm = NewVm();
        vm.RecentProjects.Clear();

        vm.ClearRecentProjectsCommand.Execute(null);

        Assert.Empty(vm.RecentProjects);
    }

    [Fact]
    public void HasTemplate_IsFalseUntilADesignIsLoaded()
    {
        var vm = NewVm();
        Assert.False(vm.HasTemplate);
    }

    /// <summary>
    /// With nothing on the canvas there is nothing to confirm, so the command must return
    /// before the prompt. Were the guard removed this test would hang on a modal dialog.
    /// </summary>
    [Fact]
    public void ClearTemplate_WithNothingLoaded_DoesNotPrompt()
    {
        var vm = NewVm();
        vm.Slots.Clear();

        vm.ClearTemplateCommand.Execute(null);

        Assert.False(vm.HasTemplate);
        Assert.Empty(vm.Slots);
    }
}
