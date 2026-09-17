using Apex.Services.Numbering;
using Apex.UI.ViewModels;
using Xunit;

namespace Apex.UI.Tests;

/// <summary>
/// The buttons that need a design are disabled until there is one, instead of answering
/// every click with a modal warning — reported from the floor as the "أدرج التصميم الأول"
/// message popping up again and again.
/// </summary>
public class AddFieldAvailabilityTests
{
    private static NumberingWizardViewModel NewVm() => new(new NumberingService());

    [Fact]
    public void WithNoDesign_AddingAFieldIsDisabled()
    {
        var vm = NewVm();

        Assert.False(vm.HasTemplate);
        Assert.False(vm.CanEditDesign);
        Assert.False(vm.AddSlotCommand.CanExecute(null));
        Assert.False(vm.StartLayoutCommand.CanExecute(null));
        Assert.False(vm.NavigateToLayoutCommand.CanExecute(null));
    }

    [Fact]
    public void WithADesign_TheDesignTabOpens_ButAFieldStillNeedsThatTab()
    {
        var vm = NewVm();

        vm.TemplatePath = @"C:\designs\invoice.png";   // a path is enough; the image may still be loading

        Assert.True(vm.HasTemplate);
        Assert.True(vm.StartLayoutCommand.CanExecute(null));
        Assert.True(vm.NavigateToLayoutCommand.CanExecute(null));
        Assert.False(vm.AddSlotCommand.CanExecute(null));   // still on the Prepare tab
    }

    [Fact]
    public void OnTheDesignTabWithADesign_AFieldCanBeAdded()
    {
        var vm = NewVm();
        vm.TemplatePath = @"C:\designs\invoice.png";

        vm.WorkflowMode = WorkflowMode.Design;

        Assert.True(vm.CanEditDesign);
        Assert.True(vm.AddSlotCommand.CanExecute(null));
    }

    [Fact]
    public void RemovingTheDesign_DisablesThemAgain()
    {
        var vm = NewVm();
        vm.TemplatePath = @"C:\designs\invoice.png";
        vm.WorkflowMode = WorkflowMode.Design;

        vm.TemplatePath = null;

        Assert.False(vm.CanEditDesign);
        Assert.False(vm.AddSlotCommand.CanExecute(null));
        Assert.False(vm.StartLayoutCommand.CanExecute(null));
    }
}
