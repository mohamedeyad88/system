using Apex.Core.Models.Imposition;
using Apex.Services.Imposition;

namespace Apex.Services.Tests.Imposition;

/// <summary>
/// Tests for <see cref="ImpositionTemplateStore"/> — save/list/load/delete and
/// export/import round-trips, using an isolated temp directory.
/// </summary>
public class ImpositionTemplateStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly ImpositionTemplateStore _store;

    public ImpositionTemplateStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "apex_imp_tpl_" + System.Guid.NewGuid().ToString("N"));
        _store = new ImpositionTemplateStore(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static ImpositionTemplate Sample(string name) => new()
    {
        Name = name,
        Input = new ImpositionInput
        {
            Type = ImpositionType.SaddleStitch,
            PageWidth = 210, PageHeight = 297,
            SheetWidth = 320, SheetHeight = 450,
            SourcePageCount = 16, Bleed = 3
        },
        Marks = new PrintMarksOptions { CropMarks = true, ColorBars = true }
    };

    [Fact]
    public void Save_Then_GetAll_ReturnsTemplate()
    {
        _store.Save(Sample("كتيب A4"));
        var all = _store.GetAll();
        Assert.Single(all);
        Assert.Equal("كتيب A4", all[0].Name);
    }

    [Fact]
    public void Save_RequiresName()
    {
        Assert.Throws<System.ArgumentException>(() => _store.Save(Sample("")));
    }

    [Fact]
    public void Load_ReturnsSavedValues()
    {
        var saved = _store.Save(Sample("preset"));
        var loaded = _store.Load(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal(ImpositionType.SaddleStitch, loaded!.Input.Type);
        Assert.Equal(210, loaded.Input.PageWidth);
        Assert.True(loaded.Marks.ColorBars);
    }

    [Fact]
    public void Delete_RemovesTemplate()
    {
        var saved = _store.Save(Sample("temp"));
        Assert.True(_store.Delete(saved.Id));
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void ExportImport_RoundTrips_WithFreshId()
    {
        var original = _store.Save(Sample("shared"));
        string file = Path.Combine(_dir, "exported.apeximp");
        _store.ExportToFile(original, file);

        var imported = _store.ImportFromFile(file);

        Assert.Equal("shared", imported.Name);
        Assert.NotEqual(original.Id, imported.Id);          // fresh id on import
        Assert.Equal(2, _store.GetAll().Count);             // original + imported
        Assert.Equal(16, imported.Input.SourcePageCount);
    }

    [Fact]
    public void ImportFromFile_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => _store.ImportFromFile(Path.Combine(_dir, "nope.apeximp")));
    }
}
