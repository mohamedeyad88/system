using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.UI.Tests
{
    /// <summary>
    /// Every colour a screen uses must come from the palette in Styles/Brand.xaml.
    ///
    /// Colours typed straight into a view drift from the brand one screen at a time,
    /// and they are what a theme cannot reach: swap the palette and they stay behind.
    /// That is how Printer Properties ended up writing its heading and every value in
    /// off-white on an off-white page — a leftover from the old dark theme that nobody
    /// could see was there. This fails the build the moment one comes back.
    /// </summary>
    public class ColourTokenTests
    {
        private static string ViewsDir() => Path.Combine(RepoPaths.UiDir(), "Views");

        // A literal colour as an attribute value: #RGB … #AARRGGBB, or a named colour.
        // Transparent is allowed — it is the absence of a colour, not a choice of one.
        private static readonly Regex Literal = new(
            @"=""(#[0-9A-Fa-f]{3,8}|White|Black|Red|Green|Blue|Gray|Grey|Silver|LightGray|DarkGray|Orange|Yellow)""",
            RegexOptions.Compiled);

        [Fact]
        public void NoViewTypesAColourByHand()
        {
            var found = new List<string>();
            var files = Directory.GetFiles(ViewsDir(), "*.xaml")
                // The shell carries the sidebar, which is on screen all the time.
                .Append(Path.Combine(Path.GetDirectoryName(ViewsDir())!, "MainWindow.xaml"));
            foreach (var file in files)
            {
                // Comments may quote the old values when they explain a change.
                string xaml = Regex.Replace(File.ReadAllText(file), @"<!--[\s\S]*?-->", "");
                foreach (Match m in Literal.Matches(xaml))
                    found.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}");
            }

            Assert.True(found.Count == 0,
                "Colours typed into views (use a token from Styles/Brand.xaml): " +
                string.Join(", ", found));
        }
    }
}
