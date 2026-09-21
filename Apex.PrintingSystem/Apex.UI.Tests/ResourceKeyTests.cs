using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Apex.UI.Tests
{
    /// <summary>
    /// A {DynamicResource} that names nothing renders as empty space — a button with
    /// no label, a heading that is not there — and nothing in the build says so. And
    /// a string added to one language file only leaves the other language showing a
    /// blank where the words should be.
    /// </summary>
    public class ResourceKeyTests
    {
        private static DirectoryInfo UiRoot() => new(RepoPaths.UiDir());

        private static HashSet<string> KeysIn(string file) =>
            Regex.Matches(File.ReadAllText(file), @"x:Key=""([^""]+)""")
                 .Select(m => m.Groups[1].Value)
                 .ToHashSet(StringComparer.Ordinal);

        private static HashSet<string> StringKeysIn(string file) =>
            Regex.Matches(File.ReadAllText(file), @"<sys:String\s+x:Key=""([^""]+)""")
                 .Select(m => m.Groups[1].Value)
                 .ToHashSet(StringComparer.Ordinal);

        [Fact]
        public void ArabicAndEnglishCarryTheSameStrings()
        {
            var res = Path.Combine(UiRoot().FullName, "Resources");
            var ar = StringKeysIn(Path.Combine(res, "Language.ar.xaml"));
            var en = StringKeysIn(Path.Combine(res, "Language.en.xaml"));

            var missingEn = ar.Except(en).OrderBy(k => k).ToList();
            var missingAr = en.Except(ar).OrderBy(k => k).ToList();

            Assert.True(missingEn.Count == 0, "English is missing: " + string.Join(", ", missingEn));
            Assert.True(missingAr.Count == 0, "Arabic is missing: " + string.Join(", ", missingAr));
        }

        [Fact]
        public void EveryDynamicResourceInAViewExists()
        {
            var ui = UiRoot();
            var defined = new HashSet<string>(StringComparer.Ordinal);
            foreach (var f in Directory.GetFiles(Path.Combine(ui.FullName, "Resources"), "*.xaml")
                              .Concat(Directory.GetFiles(Path.Combine(ui.FullName, "Styles"), "*.xaml")))
                defined.UnionWith(KeysIn(f));

            // Views also define their own local resources.
            var problems = new List<string>();
            foreach (var view in Directory.GetFiles(Path.Combine(ui.FullName, "Views"), "*.xaml"))
            {
                var local = KeysIn(view);
                var referenced = Regex.Matches(File.ReadAllText(view), @"\{DynamicResource\s+([^}\s]+)\s*\}")
                                      .Select(m => m.Groups[1].Value)
                                      .Distinct();
                foreach (var key in referenced)
                    if (!defined.Contains(key) && !local.Contains(key))
                        problems.Add($"{Path.GetFileName(view)} → {key}");
            }

            Assert.True(problems.Count == 0,
                "DynamicResource names nothing: " + string.Join(", ", problems));
        }
    }
}
