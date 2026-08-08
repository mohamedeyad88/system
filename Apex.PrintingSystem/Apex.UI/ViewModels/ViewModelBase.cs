using CommunityToolkit.Mvvm.ComponentModel;
using System.Threading.Tasks;

namespace Apex.UI.ViewModels
{
    public class ViewModelBase : ObservableObject
    {
        /// <summary>
        /// Override this method in derived ViewModels to perform async initialization.
        /// This should be called after the ViewModel is constructed and the UI is ready.
        /// </summary>
        public virtual Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>Localized string from the active language dictionary (falls back to the key).
        /// Public + static so non-VM UI helpers (e.g. item wrappers) can localize too.
        /// Thread-safe: TryFindResource is UI-thread-affine, so calls from background
        /// threads are marshalled onto the Dispatcher.</summary>
        public static string L(string key)
        {
            var app = System.Windows.Application.Current;
            if (app == null) return key;

            var dispatcher = app.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => app.TryFindResource(key) as string ?? key);

            return app.TryFindResource(key) as string ?? key;
        }

        /// <summary>Localized format string, e.g. <c>Lf("Msg_Loaded", pageCount)</c>.
        /// A malformed placeholder in a translated string must never crash the app,
        /// so a <see cref="System.FormatException"/> falls back to the raw string.</summary>
        public static string Lf(string key, params object?[] args)
        {
            var format = L(key);
            try { return string.Format(format, args); }
            catch (System.FormatException) { return format; }
        }
    }
}
