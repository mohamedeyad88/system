using System;

namespace Apex.Core.Localization
{
    /// <summary>
    /// WPF-agnostic localization entry point for the service layer.
    /// The UI sets <see cref="Resolver"/> at startup to look strings up in the
    /// active language <c>ResourceDictionary</c>. In tests / headless runs the
    /// default resolver returns the key unchanged, so services never crash.
    /// </summary>
    public static class AppLocalizer
    {
        /// <summary>Key → localized string. Defaults to identity (returns the key).</summary>
        public static Func<string, string> Resolver { get; set; } = key => key;

        /// <summary>Localized string for <paramref name="key"/> (falls back to the key).</summary>
        public static string L(string key) => Resolver(key) ?? key;

        /// <summary>Localized format string, e.g. <c>Lf("ImpS_SumGrid", cols, rows)</c>.</summary>
        public static string Lf(string key, params object?[] args) => string.Format(L(key), args);
    }
}
