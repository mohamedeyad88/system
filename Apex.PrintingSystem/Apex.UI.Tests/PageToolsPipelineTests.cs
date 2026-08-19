using Apex.Services.Imposition;
using Apex.UI.ViewModels;

namespace Apex.UI.Tests;

/// <summary>
/// Page Tools now runs operations as a PIPELINE: each tool acts on the result of the
/// previous one, not on the original document every time. These tests pin that down
/// through page counts, plus the undo/redo history that goes with it.
///
/// Page count is read from PdfSharpCore (no rasteriser), so the assertions hold even
/// on a host without the native PDF renderer.
/// </summary>
public class PageToolsPipelineTests
{
    private static PageToolsViewModel NewVm() =>
        new(new PdfPageToolsService(), new FakeFileDialogService(), new Apex.UI.Services.WorkflowHandoff());

    /// <summary>
    /// Two "insert 2 blanks" in a row must give 4 → 6 → 8. If the second op re-ran on
    /// the original (the old behaviour) it would land on 6, proving nothing chained.
    /// </summary>
    [Fact]
    public async Task OperationsChainOnTheWorkingDocument()
    {
        var vm = NewVm();

        await vm.SampleCommand.ExecuteAsync(null);   // 4-page sample becomes the document
        Assert.Equal(4, vm.PreviewPageCount);

        vm.InsertAfter = 0;
        vm.InsertCount = 2;

        await vm.InsertCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.PreviewPageCount);

        await vm.InsertCommand.ExecuteAsync(null);
        Assert.Equal(8, vm.PreviewPageCount);        // chained, not 6
    }

    [Fact]
    public async Task UndoAndRedoStepThroughThePipeline()
    {
        var vm = NewVm();
        await vm.SampleCommand.ExecuteAsync(null);
        vm.InsertAfter = 0;
        vm.InsertCount = 2;
        await vm.InsertCommand.ExecuteAsync(null);   // 6
        await vm.InsertCommand.ExecuteAsync(null);   // 8

        Assert.True(vm.CanUndo);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.PreviewPageCount);

        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.PreviewPageCount);
        Assert.True(vm.CanRedo);

        await vm.RedoCommand.ExecuteAsync(null);
        Assert.Equal(6, vm.PreviewPageCount);
    }

    /// <summary>
    /// A new operation after an undo discards the redo branch — standard editor
    /// semantics, and it stops a stale future being re-applied to a different document.
    /// </summary>
    [Fact]
    public async Task ANewOperationClearsTheRedoBranch()
    {
        var vm = NewVm();
        await vm.SampleCommand.ExecuteAsync(null);
        vm.InsertAfter = 0;
        vm.InsertCount = 2;
        await vm.InsertCommand.ExecuteAsync(null);   // 6
        await vm.UndoCommand.ExecuteAsync(null);     // back to 4, redo available
        Assert.True(vm.CanRedo);

        await vm.InsertCommand.ExecuteAsync(null);   // new op on the 4-page doc → 6
        Assert.False(vm.CanRedo);
        Assert.Equal(6, vm.PreviewPageCount);
    }

    /// <summary>Reset drops the whole pipeline and returns to the original document.</summary>
    [Fact]
    public async Task ResetReturnsToTheOriginal()
    {
        var vm = NewVm();
        await vm.SampleCommand.ExecuteAsync(null);   // 4 pages
        vm.InsertAfter = 0;
        vm.InsertCount = 2;
        await vm.InsertCommand.ExecuteAsync(null);   // 6
        await vm.InsertCommand.ExecuteAsync(null);   // 8

        await vm.ResetPipelineCommand.ExecuteAsync(null);
        Assert.Equal(4, vm.PreviewPageCount);
        Assert.False(vm.HasResult);
    }
}
