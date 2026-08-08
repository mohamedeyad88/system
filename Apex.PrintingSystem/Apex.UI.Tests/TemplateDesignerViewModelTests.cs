using Apex.Core.Utilities;
using Apex.Services.Templates;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Headless tests for <see cref="TemplateDesignerViewModel"/> pure logic that needs no
/// loaded template: grid snapping, multi-select, colour presets, and slot-type flags.
/// The ViewModel has a parameterless constructor with no disk side effects.
/// </summary>
public class TemplateDesignerViewModelTests
{
    private static CanvasSlotItem Slot(SlotDataType type = SlotDataType.Text) =>
        new() { Slot = new TemplateSlotDefinition { Name = "s", DataType = type, Width = 50, Height = 20 } };

    [Fact]
    public void SnapDip_Disabled_ReturnsInputUnchanged()
    {
        var vm = new TemplateDesignerViewModel { IsSnapEnabled = false };
        Assert.Equal(123.4, vm.SnapDip(123.4), 3);
    }

    [Fact]
    public void SnapDip_Enabled_SnapsToGridInMillimetres()
    {
        var vm = new TemplateDesignerViewModel { IsSnapEnabled = true, GridSizeMm = 5 };

        // 13mm → nearest 5mm grid = 15mm → back to canvas DIPs.
        double input = UnitConverter.MmToDips(13);
        double expected = UnitConverter.MmToDips(15);
        Assert.Equal(expected, vm.SnapDip(input), 2);
    }

    [Fact]
    public void IsImageSlot_TracksSlotDataType()
    {
        var vm = new TemplateDesignerViewModel { SlotDataType = SlotDataType.Image };
        Assert.True(vm.IsImageSlot);
        Assert.False(vm.IsTextLikeSlot);

        vm.SlotDataType = SlotDataType.Text;
        Assert.False(vm.IsImageSlot);
        Assert.True(vm.IsTextLikeSlot);
    }

    [Fact]
    public void MultiSelect_Toggle_AddsThenRemoves()
    {
        var vm = new TemplateDesignerViewModel();
        var a = Slot();
        var b = Slot();

        vm.ToggleMultiSelect(a);
        vm.ToggleMultiSelect(b);
        Assert.Equal(2, vm.SelectionCount);
        Assert.True(vm.HasMultiSelection);
        Assert.True(a.IsMultiSelected);

        vm.ToggleMultiSelect(a);              // toggle off
        Assert.Equal(1, vm.SelectionCount);
        Assert.False(a.IsMultiSelected);
    }

    [Fact]
    public void ClearMultiSelect_ResetsSelection()
    {
        var vm = new TemplateDesignerViewModel();
        var a = Slot();
        var b = Slot();
        vm.ToggleMultiSelect(a);
        vm.ToggleMultiSelect(b);
        Assert.True(vm.HasMultiSelection);    // >1 selected

        vm.ClearMultiSelect();
        Assert.Equal(0, vm.SelectionCount);
        Assert.False(vm.HasMultiSelection);
        Assert.False(a.IsMultiSelected);
        Assert.False(b.IsMultiSelected);
    }

    [Fact]
    public void ColorPresetCommands_SetSlotColors()
    {
        var vm = new TemplateDesignerViewModel();

        vm.SetTextColorPresetCommand.Execute("#FF0000");
        Assert.Equal("#FF0000", vm.SlotTextColor);

        vm.SetBgColorPresetCommand.Execute("#00FF00");
        Assert.Equal("#00FF00", vm.SlotBgColor);
    }

    [Fact]
    public void PresetColors_AreAvailable()
    {
        Assert.NotEmpty(TemplateDesignerViewModel.PresetColors);
        Assert.Contains("#000000", TemplateDesignerViewModel.PresetColors);
    }
}
