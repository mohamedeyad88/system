using Apex.Core.Interfaces;
using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;
using Apex.Services.PaperCutting;
using Apex.UI.ViewModels;
using PdfSharpCore.Pdf;

namespace Apex.UI.Tests;

/// <summary>
/// A scripted <see cref="IFileDialogService"/> that returns a queued path instead of
/// showing native dialogs, so dialog-driven ViewModel commands become unit-testable.
/// </summary>
internal sealed class FakeFileDialogService : IFileDialogService
{
    public string? NextOpenPath { get; set; }
    public string? NextSavePath { get; set; }
    public string? OpenFile(string title, string filter, string? initialDirectory = null) => NextOpenPath;
    public string? SaveFile(string title, string filter, string? suggestedFileName = null) => NextSavePath;
}

/// <summary>
/// Headless tests for <see cref="ImpositionViewModel"/>: plan, preview navigation,
/// preset selection, and the dialog-driven commands (load/export/resize) via a fake
/// dialog service. Services are real since they are pure/deterministic.
/// </summary>
public class ImpositionViewModelTests
{
    private static FakeFileDialogService _dialogs = new();

    private static ImpositionViewModel NewVm(FakeFileDialogService? dialogs = null)
    {
        _dialogs = dialogs ?? new FakeFileDialogService();
        return new(
            new ImpositionService(new PaperCuttingOptimizerService()),
            new PdfOperationsService(),
            new PdfImpositionEngine(),
            new ImpositionTemplateStore(System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "apex_uitests_" + System.Guid.NewGuid().ToString("N"))),
            _dialogs);
    }

    /// <summary>Writes a blank-page PDF to a temp file and returns its path.</summary>
    private static string WriteTempPdf(int pages)
    {
        using var doc = new PdfDocument();
        for (int i = 0; i < pages; i++) { var p = doc.AddPage(); p.Width = 595; p.Height = 842; }
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"apex_vm_{System.Guid.NewGuid():N}.pdf");
        using var ms = new System.IO.MemoryStream();
        doc.Save(ms, false);
        System.IO.File.WriteAllBytes(path, ms.ToArray());
        return path;
    }

    [Fact]
    public void Plan_NUp_ProducesResultAndPreview()
    {
        var vm = NewVm();
        vm.SourcePageCount = 4;
        vm.PageWidth = 210; vm.PageHeight = 297;
        vm.SelectedSheetPreset = "SRA3";   // 320×450
        vm.SelectedTypeIndex = 0;          // N-Up
        vm.SelectedNUpIndex = 0;           // 2-Up
        vm.Bleed = 0; vm.SheetMargin = 0; vm.Gutter = 0;

        vm.PlanCommand.Execute(null);

        Assert.True(vm.HasResult);
        Assert.False(vm.HasError);
        Assert.Equal(2, vm.PagesPerSide);
        Assert.True(vm.SheetsRequired >= 1);
        Assert.NotEmpty(vm.PreviewRects);            // preview rendered
    }

    [Fact]
    public void Plan_PageLargerThanSheet_SetsError()
    {
        var vm = NewVm();
        vm.SourcePageCount = 2;
        vm.PageWidth = 900; vm.PageHeight = 900;
        vm.SelectedSheetPreset = "A4";     // 210×297
        vm.SelectedTypeIndex = 0;

        vm.PlanCommand.Execute(null);

        Assert.False(vm.HasResult);
        Assert.True(vm.HasError);
        Assert.NotEmpty(vm.ErrorMessage);
    }

    [Fact]
    public void SheetPreset_Updates_WidthHeight()
    {
        var vm = NewVm();
        vm.SelectedSheetPreset = "A3";     // 297×420
        Assert.Equal(297, vm.SheetWidth);
        Assert.Equal(420, vm.SheetHeight);
        Assert.False(vm.IsCustomSheet);

        vm.SelectedSheetPreset = "مخصص";
        Assert.True(vm.IsCustomSheet);
    }

    [Fact]
    public void SaddleStitch_PreviewNavigation_WalksSheetsAndSides()
    {
        var vm = NewVm();
        vm.SourcePageCount = 8;
        vm.PageWidth = 210; vm.PageHeight = 297;
        vm.SelectedSheetPreset = "مخصص";
        vm.SheetWidth = 450; vm.SheetHeight = 320;
        vm.SelectedTypeIndex = 1;          // Saddle Stitch
        vm.Bleed = 0; vm.SheetMargin = 0; vm.Gutter = 0;

        vm.PlanCommand.Execute(null);

        Assert.True(vm.HasResult);
        Assert.True(vm.IsDuplex);
        Assert.Equal(2, vm.TotalSheets);
        Assert.True(vm.CanShowBack);
        Assert.Equal(0, vm.CurrentSheetIndex);

        vm.NextSheetCommand.Execute(null);
        Assert.Equal(1, vm.CurrentSheetIndex);

        vm.ToggleSideCommand.Execute(null);
        Assert.True(vm.ShowBack);
        Assert.NotEmpty(vm.PreviewRects);  // back side rendered

        vm.PrevSheetCommand.Execute(null);
        Assert.Equal(0, vm.CurrentSheetIndex);
    }

    [Fact]
    public void StepRepeat_FillsSheetWithMultiplePieces()
    {
        var vm = NewVm();
        vm.SourcePageCount = 1;
        vm.PageWidth = 90; vm.PageHeight = 50;
        vm.SelectedSheetPreset = "SRA3";
        vm.SelectedTypeIndex = 4;          // Step & Repeat
        vm.RequiredCopies = 100;

        vm.PlanCommand.Execute(null);

        Assert.True(vm.HasResult);
        Assert.True(vm.PagesPerSide > 1);
    }

    [Fact]
    public void LoadSource_ReadsPageCount()
    {
        var vm = NewVm();
        string src = WriteTempPdf(6);
        try
        {
            vm.LoadSource(src);
            Assert.True(vm.HasSource);
            Assert.Equal(6, vm.SourcePageCount);
            Assert.False(vm.HasError);
        }
        finally { System.IO.File.Delete(src); }
    }

    [Fact]
    public async System.Threading.Tasks.Task Export_WritesImposedPdf_ToDialogPath()
    {
        var dialogs = new FakeFileDialogService();
        var vm = NewVm(dialogs);
        string src = WriteTempPdf(4);
        string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"apex_out_{System.Guid.NewGuid():N}.pdf");
        try
        {
            vm.LoadSource(src);
            vm.SelectedTypeIndex = 0; vm.SelectedNUpIndex = 0;
            vm.SelectedSheetPreset = "SRA3";
            vm.Bleed = 0; vm.SheetMargin = 0; vm.Gutter = 0;
            vm.PlanCommand.Execute(null);
            Assert.True(vm.HasResult);

            dialogs.NextSavePath = outPath;
            await vm.ExportCommand.ExecuteAsync(null);

            Assert.True(System.IO.File.Exists(outPath));
            Assert.False(vm.HasError);
        }
        finally
        {
            System.IO.File.Delete(src);
            if (System.IO.File.Exists(outPath)) System.IO.File.Delete(outPath);
        }
    }

    [Fact]
    public void ResizePdf_WritesResizedFile()
    {
        var dialogs = new FakeFileDialogService();
        var vm = NewVm(dialogs);
        string src = WriteTempPdf(3);
        string outPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"apex_rs_{System.Guid.NewGuid():N}.pdf");
        try
        {
            vm.LoadSource(src);
            vm.ResizePreset = "A3";
            dialogs.NextSavePath = outPath;
            vm.ResizePdfCommand.Execute(null);

            Assert.True(System.IO.File.Exists(outPath));
            Assert.False(vm.HasError);
        }
        finally
        {
            System.IO.File.Delete(src);
            if (System.IO.File.Exists(outPath)) System.IO.File.Delete(outPath);
        }
    }

    [Fact]
    public void Export_WithoutPlan_SetsError()
    {
        var vm = NewVm();
        vm.ExportCommand.Execute(null);   // no plan run yet
        Assert.True(vm.HasError);
    }
}
