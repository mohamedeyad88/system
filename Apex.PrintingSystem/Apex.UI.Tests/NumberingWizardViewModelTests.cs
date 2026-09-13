using System.Drawing.Printing;
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

    // ── Multi-tray printing, reported from the floor as simply not working.
    //
    // Two faults met here. The per-copy tray started null and only the Original was ever
    // put in the mapping, so copies reached the printer with no paper source and came off
    // its default drawer — the same one as the original. And trays were identified by
    // PaperSourceKind, which cannot tell two drawers apart: an EPSON WF-C5210 reports
    // "درج الورق 1" and "تغذية خلفية للورق" both as Custom, with source ids 258 and 261.
    //
    // These seed that exact printer's sources so the tests do not depend on whatever
    // hardware the build machine happens to have installed.

    private const int Auto = 7, RearFeed = 261, PaperTray1 = 258;

    private static NumberingWizardViewModel VmWithEpsonTrays()
    {
        var vm = NewVm();
        vm.AvailableTrayOptions.Clear();
        vm.AvailableTrayOptions.Add(new NumberingWizardViewModel.TrayOption("تحديد تلقائي", Auto));
        vm.AvailableTrayOptions.Add(new NumberingWizardViewModel.TrayOption("تغذية خلفية للورق", RearFeed));
        vm.AvailableTrayOptions.Add(new NumberingWizardViewModel.TrayOption("درج الورق 1", PaperTray1));
        vm.OriginalTray = Auto;
        return vm;
    }

    [Fact]
    public void SingleCopy_LeavesCopyTraysAlone()
    {
        var vm = VmWithEpsonTrays();

        Assert.Equal(1, vm.NumberOfCopies);
        Assert.Null(vm.Copy1Tray);
        Assert.Null(vm.Copy2Tray);
        Assert.False(vm.TraySeparationUnavailable);
    }

    [Fact]
    public void TwoCopies_GiveTheCopyItsOwnDrawer()
    {
        var vm = VmWithEpsonTrays();

        vm.NumberOfCopies = 2;

        Assert.NotNull(vm.Copy1Tray);
        Assert.NotEqual(vm.OriginalTray, vm.Copy1Tray!.Value);

        // "Auto" is not a drawer — leaving the original on it lets the printer pick and
        // defeats the whole point of separating the copies.
        Assert.NotEqual(Auto, vm.OriginalTray);
        Assert.NotEqual(Auto, vm.Copy1Tray!.Value);
    }

    [Fact]
    public void AnOperatorsOwnTrayChoice_IsNeverOverwritten()
    {
        var vm = VmWithEpsonTrays();

        vm.Copy1Tray = RearFeed;
        vm.NumberOfCopies = 2;

        Assert.Equal(RearFeed, vm.Copy1Tray);
        Assert.NotEqual(RearFeed, vm.OriginalTray);
    }

    [Fact]
    public void MoreCopiesThanDrawers_IsReportedRatherThanSilentlySharingPaper()
    {
        var vm = NewVm();
        vm.AvailableTrayOptions.Clear();
        vm.AvailableTrayOptions.Add(new NumberingWizardViewModel.TrayOption("تحديد تلقائي", Auto));
        vm.AvailableTrayOptions.Add(new NumberingWizardViewModel.TrayOption("درج الورق 1", PaperTray1));
        vm.OriginalTray = Auto;

        vm.NumberOfCopies = 2;

        // One real drawer cannot serve two copies. The copy gets nothing rather than a
        // duplicate of the original's tray, and the UI says so.
        Assert.Null(vm.Copy1Tray);
        Assert.True(vm.TraySeparationUnavailable);
    }

    // ── Adding a field used to reset it to Arial 24 black, so an operator who set the
    // colour and size then added a second field had to set them again — for every field
    // on the sheet. Every field in a numbered book carries the same number in the same
    // type; only the position differs.

    /// <summary>
    /// Puts the ViewModel where AddSlot will run: design mode with a template path set.
    /// The file need not exist — AddSlot only refuses when no design has been chosen at
    /// all, and a missing path leaves TemplateImage null without raising a dialog.
    /// </summary>
    private static NumberingWizardViewModel VmReadyToPlaceFields()
    {
        var vm = NewVm();
        vm.WorkflowMode = WorkflowMode.Design;
        vm.TemplatePath = @"C:\designs\not-on-disk.png";
        vm.Slots.Clear();
        return vm;
    }

    [Fact]
    public void ASecondField_InheritsTheFormattingOfTheFirst()
    {
        var vm = VmReadyToPlaceFields();

        vm.AddSlotCommand.Execute(null);
        var first = vm.Slots.Single();
        first.FontColor = "#B42318";
        first.FontSize = 48;
        first.FontFamily = "Tahoma";
        first.IsBold = true;
        first.Alignment = "Right";

        vm.AddSlotCommand.Execute(null);

        var second = vm.Slots.Last();
        Assert.Equal("#B42318", second.FontColor);
        Assert.Equal(48, second.FontSize);
        Assert.Equal("Tahoma", second.FontFamily);
        Assert.True(second.IsBold);
        Assert.Equal("Right", second.Alignment);
    }

    [Fact]
    public void ASecondField_StillLandsSomewhereElseOnTheSheet()
    {
        var vm = VmReadyToPlaceFields();

        vm.AddSlotCommand.Execute(null);
        vm.AddSlotCommand.Execute(null);

        Assert.Equal(2, vm.Slots.Count);
        Assert.NotEqual(vm.Slots[0].Y, vm.Slots[1].Y);
    }

    [Fact]
    public void TheFirstField_UsesTheDefaultsWhenThereIsNothingToCopy()
    {
        var vm = VmReadyToPlaceFields();

        vm.AddSlotCommand.Execute(null);

        var only = vm.Slots.Single();
        Assert.Equal("Arial", only.FontFamily);
        Assert.Equal(24, only.FontSize);
        Assert.Equal("#000000", only.FontColor);
    }

    // ── Cutting mode: four A5 books on one A3, printed then guillotined into four piles.
    // Each pile is its own book, and a wrong range is only discovered after the paper is
    // cut. These lock the arithmetic the operator is shown before committing the run.

    private static (long From, long To, long Count) Pile(NumberingWizardViewModel vm, int index)
    {
        var p = vm.ImposedRanges[index];
        return (p.From, p.To, p.Count);
    }

    private static NumberingWizardViewModel VmInCuttingMode(int fields, long from, long to)
    {
        var vm = VmReadyToPlaceFields();
        vm.IsImposedMode = true;
        vm.StartNumber = from;
        vm.EndNumber = to;
        for (int i = 0; i < fields; i++) vm.AddSlotCommand.Execute(null);
        return vm;
    }

    [Fact]
    public void CuttingMode_SplitsTheRangeEvenlyWhenItDivides()
    {
        var vm = VmInCuttingMode(fields: 4, from: 1, to: 100);

        Assert.True(vm.HasImposedRanges);
        Assert.Equal(4, vm.ImposedRanges.Count);

        // 100 over 4 fields = 25 sheets, so the piles are 1-25, 26-50, 51-75, 76-100.
        Assert.Equal((1L, 25L, 25L), Pile(vm, 0));
        Assert.Equal((26L, 50L, 25L), Pile(vm, 1));
        Assert.Equal((51L, 75L, 25L), Pile(vm, 2));
        Assert.Equal((76L, 100L, 25L), Pile(vm, 3));
    }

    /// <summary>
    /// 100 numbers across 3 fields needs 34 sheets, so the first two piles hold 34 each and
    /// the last holds only 32. The short pile is exactly what an operator needs to see
    /// before the run rather than after the guillotine.
    /// </summary>
    [Fact]
    public void CuttingMode_ShowsTheShortLastPile()
    {
        var vm = VmInCuttingMode(fields: 3, from: 1, to: 100);

        Assert.Equal(3, vm.ImposedRanges.Count);
        Assert.Equal((1L, 34L, 34L), Pile(vm, 0));
        Assert.Equal((35L, 68L, 34L), Pile(vm, 1));
        Assert.Equal((69L, 100L, 32L), Pile(vm, 2));   // the short one
    }

    [Fact]
    public void LinearMode_HasNoCutPieces()
    {
        var vm = VmReadyToPlaceFields();
        vm.IsLinearMode = true;
        vm.AddSlotCommand.Execute(null);
        vm.AddSlotCommand.Execute(null);

        Assert.False(vm.HasImposedRanges);
        Assert.Empty(vm.ImposedRanges);
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
