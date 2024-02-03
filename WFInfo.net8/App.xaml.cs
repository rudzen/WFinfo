using System.Windows;
using Windows.Foundation.Metadata;
using Windows.Graphics.Capture;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WFInfo.Services.HDRDetection;
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
                    configurationBuilder.SetBasePath(context.HostingEnvironment.ContentRootPath);
                    configurationBuilder.AddJsonFile("appsettings.json", optional: false);
                })
                .ConfigureServices((context, services) =>
                {
                    services.AddKeyedSingleton<IScreenshotService, GdiScreenshotService>(ScreenshotTypes.Gdi);
                    services.AddKeyedSingleton<IScreenshotService, ImageScreenshotService>(ScreenshotTypes.ImageScreenshot);
                    
                    // Only add windows capture on supported platforms (W10+ 2004 / Build 20348 and above)
                    const string windowsCaptureNameSpace = "Windows.Graphics.Capture.GraphicsCaptureSession";
                    if (ApiInformation.IsTypePresent(windowsCaptureNameSpace) &&
                        ApiInformation.IsPropertyPresent(windowsCaptureNameSpace, nameof(GraphicsCaptureSession.IsBorderRequired)))
                    {
                        services.AddKeyedSingleton<IScreenshotService, WindowsCaptureScreenshotService>(ScreenshotTypes.WindowCapture);
                    }

                    services.AddSingleton<IWindowInfoService, Win32WindowInfoService>();
                    services.AddSingleton<IProcessFinder, WarframeProcessFinder>();
                    services.AddSingleton<IHDRDetectorService, SchemeHDRDetector>();

                    services.AddSingleton(ApplicationSettings.GlobalReadonlySettings)
                            .AddSingleton<ITesseractService, TesseractService>();

                    services.AddSingleton<ApplicationSettings>();
                    
                    services.AddSingleton<MainWindow>();
                    services.AddSingleton<Login>();
                    services.AddSingleton<SettingsWindow>();
                    services.AddSingleton<ThemeAdjuster>();
                    
                    services.AddSingleton<SettingsViewModel>();
                })
                .ConfigureLogging(logging => { logging.AddConsole(); })
                .Build();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        await _host.StartAsync();

        CustomEntrypoint.Run(AppDomain.CurrentDomain);
        
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
}