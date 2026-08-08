using Apex.NumberedBooksEngine.Core;
using Apex.NumberedBooksEngine.Models;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Apex.NumberedBooksEngine.Tests
{
    public class NumberSequencerTests
    {
        [Fact]
        public void LinearSequence_GeneratesCorrectOrder()
        {
            var sequencer = new NumberSequencer();
            var options = new BookJobOptions(
                TemplateStream: null!,
                TemplatePath: null,
                TemplateFormat: TemplateFormat.Image,
                Layout: LayoutSpec.A4,
                Slots: new List<SlotSpec> {
                    new SlotSpec("s1", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!),
                    new SlotSpec("s2", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!)
                },
                StartNumber: 1,
                TotalNumbers: 6,
                PagesPerBook: 10,
                CopiesPerPage: 1,
                Mode: NumberingMode.Linear,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: ""
            );

            var pages = sequencer.GenerateSequence(options).ToList();

            Assert.Equal(3, pages.Count);
            Assert.Equal(new long[] { 1, 2 }, pages[0]);
            Assert.Equal(new long[] { 3, 4 }, pages[1]);
            Assert.Equal(new long[] { 5, 6 }, pages[2]);
        }

        [Fact]
        public void ImposedSequence_GeneratesCorrectOrder_Balanced()
        {
            // Total 100, 4 slots per sheet
            // Sheets = 25
            // Gap = 25
            // Sheet 0: 1, 26, 51, 76
            // Sheet 1: 2, 27, 52, 77

            var sequencer = new NumberSequencer();
            var options = new BookJobOptions(
                TemplateStream: null!,
                TemplatePath: null,
                TemplateFormat: TemplateFormat.Image,
                Layout: LayoutSpec.A4,
                Slots: new List<SlotSpec>
                {
                    new SlotSpec("s1", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!),
                    new SlotSpec("s2", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!),
                    new SlotSpec("s3", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!),
                    new SlotSpec("s4", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!)
                },
                StartNumber: 1,
                TotalNumbers: 100,
                PagesPerBook: 10,
                CopiesPerPage: 1,
                Mode: NumberingMode.Imposed,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: ""
            );

            var pages = sequencer.GenerateSequence(options).ToList();

            Assert.Equal(25, pages.Count);
            Assert.Equal(new long[] { 1, 26, 51, 76 }, pages[0]);
            Assert.Equal(new long[] { 2, 27, 52, 77 }, pages[1]);
            Assert.Equal(new long[] { 25, 50, 75, 100 }, pages[24]);
        }

        [Fact]
        public void ImposedSequence_GeneratesCorrectOrder_Unbalanced()
        {
            // Total 10, 3 slots per sheet
            // Sheets = ceil(10/3) = 4
            // Gap = 4
            // Sheet 0: 1, 5, 9
            // Sheet 1: 2, 6, 10
            // Sheet 2: 3, 7, -1
            // Sheet 3: 4, 8, -1

            var sequencer = new NumberSequencer();
            var options = new BookJobOptions(
                TemplateStream: null!,
                TemplatePath: null,
                TemplateFormat: TemplateFormat.Image,
                Layout: LayoutSpec.A4,
                Slots: new List<SlotSpec>
                {
                    new SlotSpec("s1", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!),
                    new SlotSpec("s2", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!),
                    new SlotSpec("s3", 0, 0, 0, 0, "", 0, "#000000", TextAlign.Left, 0, null!)
                },
                StartNumber: 1,
                TotalNumbers: 10,
                PagesPerBook: 10,
                CopiesPerPage: 1,
                Mode: NumberingMode.Imposed,
                LowResourceMode: false,
                DegreeOfParallelism: 1,
                CheckpointEvery: 100,
                OutputMode: "SinglePdf",
                OutputPath: ""
            );

            var pages = sequencer.GenerateSequence(options).ToList();

            Assert.Equal(4, pages.Count);
            Assert.Equal(new long[] { 1, 5, 9 }, pages[0]);
            Assert.Equal(new long[] { 2, 6, 10 }, pages[1]);
            Assert.Equal(new long[] { 3, 7, -1 }, pages[2]);
            Assert.Equal(new long[] { 4, 8, -1 }, pages[3]);
        }
    }
}
