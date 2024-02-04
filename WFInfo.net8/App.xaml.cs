using System.IO;
using System.Windows;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using WFInfo.Resources;
using WFInfo.Services.HDRDetection;
using WFInfo.Services.HDRDetection.Schemes;
using WFInfo.Services.Screenshot;
using WFInfo.Services.WarframeProcess;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace WFInfo;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = new HostBuilder()
                .ConfigureAppConfiguration((context, configurationBuilder) =>
                {
                    configurationBuilder.SetBasePath(context.HostingEnvironment.ContentRootPath)
                                        .AddEnvironmentVariables()
                                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddKeyedSingleton<IScreenshotService, GdiScreenshotService>(ScreenshotTypes.Gdi);
                    services.AddKeyedSingleton<IScreenshotService, ImageScreenshotService>(ScreenshotTypes
                        .ImageScreenshot);

                    // Only add windows capture on supported platforms (W10+ 2004 / Build 20348 and above)
                    const string windowsCaptureNameSpace = "Windows.Graphics.Capture.GraphicsCaptureSession";
                    if (ApiInformation.IsTypePresent(windowsCaptureNameSpace) &&
                        ApiInformation.IsPropertyPresent(windowsCaptureNameSpace,
                            nameof(GraphicsCaptureSession.IsBorderRequired)))
                    {
                        services.AddKeyedSingleton<IScreenshotService, WindowsCaptureScreenshotService>(
                            ScreenshotTypes.WindowCapture
                        );
                    }

                    services.AddSingleton<IWindowInfoService, Win32WindowInfoService>();
                    services.AddSingleton<IProcessFinder, WarframeProcessFinder>();
                    services.AddSingleton<IEncryptedDataService, EncryptedDataService>();
                    services.AddSingleton<IHDRDetectorService, SchemeHDRDetector>();
                    services.AddSingleton<IHDRDetectionScheme, GameSettingsHDRDetectionScheme>();

                    services.AddSingleton<Data>();

                    services.AddSingleton<ITesseractService, TesseractService>();

                    services.AddSingleton<ApplicationSettings>();

                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<Login>();
                    services.AddSingleton<SettingsWindow>();
                    services.AddSingleton<ThemeAdjuster>();
                    services.AddSingleton<PlusOne>();
                    services.AddSingleton<RelicsWindow>();
                    services.AddSingleton<EquipmentWindow>();

                    services.AddSingleton<SettingsViewModel>();

                    services.AddDataProtection();
                })
                .UseSerilog((context, provider, arg3) =>
                {
                    const string defaultTemplate =
                        "[{Timestamp:HH:mm:ss} T:{ThreadId} {Level:u3}] {Message:lj} {SourceContext}{NewLine}{Exception}";
                    LogEventLevel level;

                    arg3.ReadFrom.Configuration(context.Configuration)
#if DEBUG
                        .WriteTo.File(
                            restrictedToMinimumLevel: LogEventLevel.Verbose,
                            outputTemplate: defaultTemplate,
                            path: Path.Combine(Environment.CurrentDirectory, "wf_info_debug.log.txt"))
                        // .WriteTo.Debug(outputTemplate: defaultTemplate)
#endif
                        .Enrich.WithThreadId()
                        .Enrich.FromLogContext();

                    // .CreateLogger();
                })
                .Build();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        await _host.StartAsync();

        await CustomEntrypoint.Run(AppDomain.CurrentDomain, _host.Services);
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();

        // well because so far, the original author(s) decided to intertwine things
        WFInfo.MainWindow.INSTANCE = mainWindow;
        mainWindow.Show();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        if (WFInfo.MainWindow.INSTANCE != null)
        {
            WFInfo.Main.INSTANCE.DisposeTesseract();
            WFInfo.MainWindow.listener.Dispose();
            WFInfo.MainWindow.INSTANCE.Exit(null, null);
        }

        using (_host)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
    }
    
    private static Serilog.ILogger CreateLogger(IConfiguration configuration)
    {
        const string defaultTemplate =
            "[{Timestamp:HH:mm:ss} T:{ThreadId} {Level:u3}] {Message:lj} {SourceContext}{NewLine}{Exception}";
        LogEventLevel level;

#if DEBUG
        level = LogEventLevel.Verbose;
#else
        level = LogEventLevel.Debug;
#endif

        // Apply the config to the logger
        Log.Logger = new LoggerConfiguration()
                     .ReadFrom.Configuration(configuration)
                     .Enrich.WithThreadId()
                     .Enrich.FromLogContext()
#if DEBUG
                     .WriteTo.File(
                         restrictedToMinimumLevel: LogEventLevel.Verbose,
                         outputTemplate: defaultTemplate,
                         path: Path.Combine(Environment.CurrentDirectory, "wf_info_debug.log.txt"))
                     .WriteTo.Debug(outputTemplate: defaultTemplate)
#endif
                     .CreateLogger();
        AppDomain.CurrentDomain.ProcessExit += static (_, _) => Log.CloseAndFlush();
        return Log.Logger;
    }
}