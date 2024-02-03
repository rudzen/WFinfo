using Microsoft.Extensions.DependencyInjection;
using WFInfo.Services.HDRDetection;
using WFInfo.Services.Screenshot;
using WFInfo.Services.WarframeProcess;
using WFInfo.Services.WindowInfo;

namespace WFInfo.Services;

// TODO: Convert classes that use these services into services
public static class ServiceExtensions
{
    public static IServiceCollection AddGDIScreenshots(this IServiceCollection services)
    {
        return services.AddSingleton<IScreenshotService, GdiScreenshotService>();
    }

    public static IServiceCollection AddWindowsCaptureScreenshots(this IServiceCollection services)
    {
        return services.AddSingleton<IScreenshotService, WindowsCaptureScreenshotService>();
    }

    /// <summary>
    /// Registers <see cref="ImageScreenshotService"/> service for providing image data from files.
    /// With <paramref name="primaryProvider"/> <see langword="false"/> this adds a standalone instance that is not tied to <see cref="IScreenshotService"/>.
    /// </summary>
    /// <param name="services"></param>
    /// <param name="primaryProvider">Whether to use this as the primary image source</param>
    public static IServiceCollection AddImageScreenshots(this IServiceCollection services, bool primaryProvider = false)
    {
        return primaryProvider
            ? services.AddSingleton<IScreenshotService, ImageScreenshotService>()
            : services.AddSingleton<ImageScreenshotService>();
    }

    public static IServiceCollection AddWin32WindowInfo(this IServiceCollection services)
    {
        return services.AddSingleton<IWindowInfoService, Win32WindowInfoService>();
    }

    public static IServiceCollection AddProcessFinder(this IServiceCollection services)
    {
        return services.AddSingleton<IProcessFinder, WarframeProcessFinder>();
    }

    public static IServiceCollection AddHDRDetection(this IServiceCollection services)
    {
        return services.AddSingleton<IHDRDetectorService, SchemeHDRDetector>();
    }

    // TODO: Convert old services
}