using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Apex.Services.SmartVariables;
using Apex.Services.SmartVariables.Models;
using Xunit;

namespace Apex.UI.Tests
{
    /// <summary>
    /// A project whose template carries an image field, with the images coming from
    /// a folder rather than a column. Driving the real screens showed the export
    /// blocked by "الصورة مطلوبة لهذا السجل ولم يتم العثور عليها" for every record
    /// while the image manager reported every image matched; these pin down which
    /// half was wrong.
    /// </summary>
    public class ImageMatchValidationTests : IDisposable
    {
        private readonly string _folder;

        public ImageMatchValidationTests()
        {
            _folder = Path.Combine(Path.GetTempPath(), "apex-img-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);
            foreach (var id in new[] { "1001", "1002", "1003" })
            {
                using var bmp = new System.Drawing.Bitmap(120, 120);
                bmp.Save(Path.Combine(_folder, id + ".png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        public void Dispose()
        {
            try { Directory.Delete(_folder, recursive: true); } catch { }
        }

        private SmartDataSource ThreeRows()
        {
            var src = new SmartDataSource { Columns = { "الاسم", "الرقم" } };
            int i = 0;
            foreach (var (name, id) in new[] { ("محمد", "1001"), ("سارة", "1002"), ("خالد", "1003") })
            {
                src.Rows.Add(new SmartDataRow
                {
                    RowIndex = i++,
                    Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["الاسم"] = name,
                        ["الرقم"] = id,
                    }
                });
            }
            return src;
        }

        private List<ImageAsset> Library() =>
            Directory.GetFiles(_folder, "*.png")
                     .Select(p => new ImageAsset
                     {
                         FileName = Path.GetFileName(p),
                         FullPath = p,
                         Extension = ".png",
                         Status = AssetStatus.Available,
                     })
                     .ToList();

        [Fact]
        public void MatchingByFileNameFindsEveryRowsImage()
        {
            var src = ThreeRows();
            var matcher = new ImageMatchingService();

            matcher.ResolveImages(src, Library(), new ImageMatchingOptions
            {
                Mode = ImageMatchMode.ByFileName,
                KeyColumn = "الرقم",
                ImageFolder = _folder,
            });

            Assert.All(src.Rows, r => Assert.Equal(ImageStatus.Found, r.ImageStatus));
        }

        [Fact]
        public void ValidationDoesNotInventAMissingImageAfterAMatch()
        {
            var src = ThreeRows();
            var matcher = new ImageMatchingService();
            matcher.ResolveImages(src, Library(), new ImageMatchingOptions
            {
                Mode = ImageMatchMode.ByFileName,
                KeyColumn = "الرقم",
                ImageFolder = _folder,
            });

            var mappings = new List<VariableMapping>
            {
                new() { FieldId = "f1", FieldLabel = "الاسم", FieldType = SmartFieldType.TextVariable, ColumnName = "الاسم" },
                // The image field takes its picture from the folder, so it is
                // deliberately bound to no column.
                new() { FieldId = "f2", FieldLabel = "صورة 5", FieldType = SmartFieldType.ImageVariable },
            };

            new DataValidationService().ValidateAll(src, mappings);

            Assert.All(src.Rows, r => Assert.Empty(r.Errors));
        }

        /// <summary>
        /// The image field is bound to no column and no image folder was ever
        /// chosen: that is an unfinished project, not three broken records, and it
        /// must not be reported as an error that blocks the export.
        /// </summary>
        [Fact]
        public void AnUnmatchedOptionalImageIsNotAHardError()
        {
            var src = ThreeRows();
            foreach (var r in src.Rows) r.ImageStatus = ImageStatus.Missing;

            var mappings = new List<VariableMapping>
            {
                new() { FieldId = "f1", FieldLabel = "الاسم", FieldType = SmartFieldType.TextVariable, ColumnName = "الاسم" },
                new() { FieldId = "f2", FieldLabel = "صورة 5", FieldType = SmartFieldType.ImageVariable, IsRequired = false },
            };

            new DataValidationService().ValidateAll(src, mappings);

            Assert.All(src.Rows, r => Assert.Empty(r.Errors));
            Assert.All(src.Rows, r => Assert.NotEqual(RowStatus.Error, r.Status));
            Assert.All(src.Rows, r => Assert.NotEmpty(r.Warnings));
        }

        [Fact]
        public void ARequiredImageThatIsMissingIsStillAnError()
        {
            var src = ThreeRows();
            foreach (var r in src.Rows) r.ImageStatus = ImageStatus.Missing;

            var mappings = new List<VariableMapping>
            {
                new() { FieldId = "f2", FieldLabel = "صورة 5", FieldType = SmartFieldType.ImageVariable, IsRequired = true },
            };

            new DataValidationService().ValidateAll(src, mappings);

            Assert.All(src.Rows, r => Assert.NotEmpty(r.Errors));
            Assert.All(src.Rows, r => Assert.Equal(RowStatus.Error, r.Status));
        }

        /// <summary>
        /// A template with no image field at all: a stale "missing image" state left
        /// on the rows must say nothing.
        /// </summary>
        [Fact]
        public void NoImageFieldMeansNoImageComplaint()
        {
            var src = ThreeRows();
            foreach (var r in src.Rows) r.ImageStatus = ImageStatus.Missing;

            var mappings = new List<VariableMapping>
            {
                new() { FieldId = "f1", FieldLabel = "الاسم", FieldType = SmartFieldType.TextVariable, ColumnName = "الاسم" },
            };

            new DataValidationService().ValidateAll(src, mappings);

            Assert.All(src.Rows, r => Assert.Empty(r.Errors));
            Assert.All(src.Rows, r => Assert.Empty(r.Warnings));
        }
    }
}
