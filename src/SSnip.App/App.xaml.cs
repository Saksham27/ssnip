using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using SSnip.App.Services;
using SSnip.App.ViewModels;
using SSnip.App.Views;
using SSnip.App.Views.Pages;
using SSnip.Core;
using SSnip.Core.Settings;
using SSnip.Infrastructure;
using Wpf.Ui.Appearance;

namespace SSnip.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        try
        {
            _host = BuildHost(e.Args, AppPaths.Default);
            await _host.StartAsync();

            var services = _host.Services;
            var settings = await services.GetRequiredService<ISettingsStore>().LoadAsync();
            ApplyTheme(settings.Theme);

            var tools = services.GetRequiredService<ToolsViewModel>();
            await tools.RefreshAsync();

            if (!settings.FirstRunCompleted)
            {
                var wizard = services.GetRequiredService<FirstRunWindow>();
                if (wizard.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            // The stored concurrency applies before the first job starts.
            services.GetRequiredService<QueueViewModel>().Initialize();
            await services.StartQueueAsync();

            var window = services.GetRequiredService<MainWindow>();
            MainWindow = window;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            window.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SSnip failed to start");
            System.Windows.MessageBox.Show($"SSnip could not start.\n\n{ex.Message}", "SSnip", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));

            // The queue only supports async disposal.
            if (_host is IAsyncDisposable asyncHost)
            {
                await asyncHost.DisposeAsync();
            }
            else
            {
                _host.Dispose();
            }
        }

        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private static IHost BuildHost(string[] args, AppPaths paths)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, DisableDefaults = true });

        builder.Services.AddSSnipInfrastructure(paths);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                System.IO.Path.Combine(paths.Logs, "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        builder.Services.AddSerilog();

        builder.Services.AddSingleton<IDialogService, DialogService>();
        builder.Services.AddSingleton<INotificationService, NotificationService>();
        builder.Services.AddSingleton<IPowerService, PowerService>();
        builder.Services.AddSingleton<PageProvider>();

        builder.Services.AddSingleton<ToolsViewModel>();
        builder.Services.AddSingleton<QueueViewModel>();
        builder.Services.AddSingleton<HomeViewModel>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddTransient<FirstRunViewModel>();

        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddTransient<FirstRunWindow>();

        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<SearchPage>();
        builder.Services.AddSingleton<ClipEditorPage>();
        builder.Services.AddSingleton<QueuePage>();
        builder.Services.AddSingleton<LibraryPage>();
        builder.Services.AddSingleton<ChannelsPage>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder.Build();
    }

    private static void ApplyTheme(AppTheme theme)
    {
        switch (theme)
        {
            case AppTheme.Light:
                ApplicationThemeManager.Apply(ApplicationTheme.Light);
                break;
            case AppTheme.System:
                ApplicationThemeManager.ApplySystemTheme();
                break;
            default:
                ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                break;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // Phase 6 adds the full crash handler; until then log it and keep the app running.
        _host?.Services.GetService<ILogger<App>>()?.LogError(e.Exception, "Unhandled UI exception");
        System.Windows.MessageBox.Show(e.Exception.Message, "SSnip error", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
