using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Apex.UI.ViewModels;
using Xunit;

namespace Apex.UI.Tests
{
    /// <summary>
    /// Every {Binding SomethingCommand} in a screen must name a command the view
    /// model actually has. A binding that names nothing fails silently at runtime:
    /// the button is enabled, it depresses when clicked, and nothing happens. That
    /// is how "🚀 بدء التصدير" sat there dead — it asked for StartExportAsyncCommand
    /// while the generator, which drops the Async suffix, had produced
    /// StartExportCommand.
    /// </summary>
    public class CommandBindingTests
    {
        private static string RepoFile(params string[] parts)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Apex.UI")))
                dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(new[] { dir!.FullName }.Concat(parts).ToArray());
        }

        private static IEnumerable<string> CommandNamesIn(string xamlPath)
        {
            string xaml = File.ReadAllText(xamlPath);
            foreach (Match m in Regex.Matches(xaml, @"\{Binding\s+(?:Path=)?([A-Za-z0-9_.]*?Command)\b"))
            {
                string name = m.Groups[1].Value;
                // Only the ones addressed directly on this view's own view model.
                if (name.Contains('.')) continue;
                yield return name;
            }
        }

        public static IEnumerable<object[]> Screens() => new List<object[]>
        {
            new object[] { Path.Combine("Apex.UI", "Views", "SmartVariablesView.xaml"), typeof(SmartVariablesViewModel) },
            new object[] { Path.Combine("Apex.UI", "Views", "TemplateDesignerView.xaml"), typeof(TemplateDesignerViewModel) },
        };

        [Theory]
        [MemberData(nameof(Screens))]
        public void EveryCommandBindingNamesARealCommand(string relativeXaml, Type viewModel)
        {
            string path = RepoFile(relativeXaml.Split(Path.DirectorySeparatorChar));
            Assert.True(File.Exists(path), $"missing {path}");

            var have = viewModel.GetProperties().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            var missing = CommandNamesIn(path).Distinct().Where(n => !have.Contains(n)).ToList();

            Assert.True(missing.Count == 0,
                $"{Path.GetFileName(path)} binds to commands {viewModel.Name} does not have: " +
                string.Join(", ", missing));
        }
    }
}
