using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Windows;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.ObjectPool;
using Serilog;
using Serilog.Events;
using WFInfo.Services;
using WFInfo.Services.HDRDetection;
using WFInfo.Services.HDRDetection.Schemes;
using WFInfo.Services.OpticalCharacterRecognition;
using WFInfo.Services.Screenshot;
using WFInfo.Services.WarframeProcess;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;

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
                    services = CreateWebClients(context, services);

                    // client.Timeout = TimeSpan.FromSeconds(30);
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

                    services = AddMessageBus(services);

                    services.TryAddSingleton<IHasherService, HasherService>();
                    services.TryAddSingleton<IWindowInfoService, Win32WindowInfoService>();
                    services.TryAddSingleton<IThemeDetector, ThemeDetector>();
                    services.TryAddSingleton<ISnapZoneDivider, SnapZoneDivider>();
                    services.TryAddSingleton<IRewardSelector, RewardSelector>();
                    services.TryAddSingleton<IProcessFinder, WarframeProcessFinder>();
                    services.TryAddSingleton<IEncryptedDataService, EncryptedDataService>();
                    services.TryAddSingleton<ISoundPlayer, SoundPlayer>();
                    services.TryAddSingleton<IHDRDetectorService, SchemeHDRDetector>();
                    services.TryAddSingleton<IHDRDetectionScheme, GameSettingsHDRDetectionScheme>();
                    services.TryAddSingleton<ILogCapture, LogCapture>();
                    services.TryAddSingleton<ILowLevelListener, LowLevelListener>();
                    services.TryAddSingleton<ILevenshteinDistanceService, LevenshteinDistanceService>();

                    services.TryAddSingleton<Main>();
                    services.TryAddSingleton<Data>();
                    services.TryAddSingleton<ITesseractService, TesseractService>();
                    services.TryAddSingleton<ApplicationSettings>();

                    // windows
                    services.TryAddSingleton<MainWindow>();
                    services.TryAddSingleton<Login>();
                    services.TryAddSingleton<SettingsWindow>();
                    services.TryAddSingleton<ThemeAdjuster>();
                    services.TryAddSingleton<PlusOne>();
                    services.TryAddSingleton<RelicsWindow>();
                    services.TryAddSingleton<RelicsViewModel>();
                    services.TryAddSingleton<EquipmentWindow>();
                    services.TryAddSingleton<RewardWindow>();
                    services.TryAddSingleton<AutoCount>();
                    services.TryAddSingleton<SearchIt>();
                    services.TryAddSingleton<GFNWarning>();

                    services.TryAddSingleton<SettingsViewModel>();

                    services.AddDataProtection();

                    services.TryAddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
                    services.TryAddSingleton<ObjectPool<StringBuilder>>(serviceProvider =>
                    {
                        var provider = serviceProvider.GetRequiredService<ObjectPoolProvider>();
                        var policy = new StringBuilderPooledObjectPolicy();
                        return provider.Create(policy);
                    });
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
                })
                .Build();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        await _host.StartAsync();

        await CustomEntrypoint.Run(AppDomain.CurrentDomain, _host.Services);
        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        // well because so far, the original author(s) decided to intertwine things
        mainWindow.Show();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
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

    private static IServiceCollection CreateWebClients(HostBuilderContext context, IServiceCollection services)
    {
        var proxyString2 = context.Configuration.GetValue<string>("http_proxy");
        var proxyString = Environment.GetEnvironmentVariable("http_proxy");
        var clientHandler = new HttpClientHandler
        {
            Proxy = proxyString is not null ? new WebProxy(new Uri(proxyString)) : null,
            UseCookies = false
        };

        services.AddHttpClient();
        services.AddHttpClient(nameof(Data), (provider, client) =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("User-Agent", "WFInfo");
                    ApplyEncoding(client.DefaultRequestHeaders);
                })
                .ConfigurePrimaryHttpMessageHandler(() => clientHandler);

        services.AddHttpClient(nameof(TesseractService), (provider, client) =>
                {
                    client.Timeout = TimeSpan.FromSeconds(30);
                    client.DefaultRequestHeaders.Add("User-Agent", "WFInfo");
                    ApplyEncoding(client.DefaultRequestHeaders);
                })
                .ConfigurePrimaryHttpMessageHandler(() => clientHandler);

        return services;

        static void ApplyEncoding(HttpRequestHeaders httpRequestHeaders)
        {
            httpRequestHeaders.AcceptEncoding.Clear();
            httpRequestHeaders.AcceptEncoding.Add(new StringWithQualityHeaderValue("br"));
        }
    }

    private static IServiceCollection AddMessageBus(IServiceCollection services)
    {
        return services.AddMediator(options =>
        {
            options.Namespace = "WFInfo";
            options.ServiceLifetime = ServiceLifetime.Singleton;
        });
    }
}
