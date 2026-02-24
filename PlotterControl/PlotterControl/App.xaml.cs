using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using PlotterControl.Services;
using PlotterControl.ViewModels;
using PlotterControl.Utils;

namespace PlotterControl;

public partial class App : Application
{
    public ServiceProvider ServiceProvider { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Global exception handlers
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        try
        {
            var services = new ServiceCollection();

            // Register core services
            services.AddSingleton<SerialConnection>();
            services.AddSingleton<IPlotterService, PlotterService>();
            services.AddSingleton<ConfigManager>();
            services.AddSingleton<TextRenderer>();
            services.AddSingleton<GCodeGenerator>();
            services.AddSingleton<TemplateManager>();
            services.AddSingleton<ImageProcessor>();
            services.AddSingleton<SystemFontRenderer>();
            services.AddSingleton<HumanizingFilter>();

            // Register MainWindow
            services.AddSingleton<MainWindow>();

            // Register ViewModels
            services.AddTransient<MainViewModel>();
            services.AddTransient<ControlPanelVM>();
            services.AddTransient<CalibrationVM>();
            services.AddTransient<EditorVM>();
            services.AddTransient<SettingsVM>();

            ServiceProvider = services.BuildServiceProvider();

            // Apply saved theme before showing any UI
            var configManager = ServiceProvider.GetRequiredService<ConfigManager>();
            SettingsVM.ApplyTheme(configManager.CurrentConfig.DarkTheme);

            var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Logger.Error($"Fatal startup error: {ex.Message}", ex);
            MessageBox.Show($"Failed to start application:\n\n{ex.Message}\n\n{ex.InnerException?.Message}",
                "SimplePlotter - Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Logger.Error($"Unhandled UI exception: {e.Exception.Message}", e.Exception);
        MessageBox.Show($"An error occurred:\n\n{e.Exception.Message}",
            "SimplePlotter Error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            Logger.Error($"Unhandled exception: {ex.Message}", ex);
        }
    }
}
