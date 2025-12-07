using Apex.UI.ViewModels;
using Apex.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Apex.UI.Modules
{
    public static class UIModule
    {
        public static IServiceCollection AddApexUI(this IServiceCollection services)
        {
            // ViewModels
            services.AddSingleton<MainViewModel>();
            services.AddTransient<PrintersViewModel>();
            services.AddTransient<SettingsViewModel>();
            services.AddTransient<PrintManagerViewModel>();
            services.AddTransient<NumberedBooksViewModel>();
            services.AddTransient<SystemPerformanceViewModel>();
            services.AddTransient<PrinterDiagnosticsViewModel>();
            services.AddTransient<LogViewerViewModel>();

            // Views
            services.AddSingleton<MainWindow>();
            services.AddTransient<LogViewerWindow>();
            services.AddTransient<ErrorDialog>();

            return services;
        }
    }
}
