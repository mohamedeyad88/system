using System.IO;
using System.Runtime.CompilerServices;

namespace Apex.UI.Tests
{
    /// <summary>
    /// Where the source tree is, for tests that read XAML and resource files.
    ///
    /// Walking up from the test binary's folder only works when the tests are built
    /// inside the repo: build them to another output folder (which is how you run
    /// them while the app itself is open and holding its own binaries) and the walk
    /// never finds Apex.UI, so every such test fails for a reason that has nothing to
    /// do with what it checks. The compiler knows where this file lives; use that.
    /// </summary>
    internal static class RepoPaths
    {
        /// <summary>The Apex.PrintingSystem folder.</summary>
        public static string SolutionDir([CallerFilePath] string thisFile = "") =>
            Path.GetDirectoryName(Path.GetDirectoryName(thisFile))!;

        /// <summary>The Apex.UI project folder.</summary>
        public static string UiDir() => Path.Combine(SolutionDir(), "Apex.UI");
    }
}
