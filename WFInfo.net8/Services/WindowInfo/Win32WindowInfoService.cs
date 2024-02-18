using System.Drawing;
using System.Windows.Forms;
using Mediator;
using Serilog;
using WFInfo.Domain;
using WFInfo.Services.WarframeProcess;
using WFInfo.Settings;

namespace WFInfo.Services.WindowInfo;

public class Win32WindowInfoService(
    IProcessFinder process,
    ApplicationSettings settings,
    IPublisher publisher)
    : IWindowInfoService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<Win32WindowInfoService>();

    public double DpiScaling { get; private set; }

    public double ScreenScaling
    {
        get
        {
            // Image is less than 16:9 aspect
            if (Window.Width * 9 > Window.Height * 16)
                return Window.Height / 1080.0;

            // Image is higher than 16:9 aspect
            return Window.Width / 1920.0;
        }
    }

    public Rectangle Window { get; private set; }

    public Point Center => new(Window.X + Window.Width / 2, Window.Y + Window.Height / 2);

    public Screen? Screen { get; private set; } = Screen.PrimaryScreen;

    public async Task UpdateWindow()
    {
        if (!process.IsRunning() && !settings.Debug)
        {
            Logger.Debug("Failed to find warframe process for window info");
            await publisher.Publish(new UpdateStatus("Failed to find warframe process for window info", StatusSeverity.Error));
            return;
        }

        Screen = Screen.FromHandle(process.HandleRef.Handle);
        var screenType = Screen.Primary ? "primary" : "secondary";

        if (process.GameIsStreamed)
            Logger.Debug("GFN -- Warframe display: {DeviceName}, {ScreenType}", Screen.DeviceName, screenType);
        else
            Logger.Debug("Warframe display: {DeviceName}, {ScreenType}", Screen.DeviceName, screenType);

        RefreshDPIScaling();
        await GetWindowRect();
    }

    public void UseImage(Bitmap? bitmap)
    {
        var width = bitmap?.Width ?? Screen.Bounds.Width;
        var height = bitmap?.Height ?? Screen.Bounds.Height;

        Window = new Rectangle(0, 0, width, height);
        DpiScaling = 1;

        if (bitmap is not null)
            Logger.Debug("DETECTED LOADED IMAGE BOUNDS: {Window}", Window);
        else
            Logger.Debug("Couldn't Detect Warframe Process. Using Primary Screen Bounds. window={Window},named={Name}", Window, Screen.DeviceName);
    }

    private async Task GetWindowRect()
    {
        if (!Win32.GetWindowRect(process.HandleRef, out var osRect))
        {
            if (settings.Debug)
            {
                // If debug is on AND warframe is not detected,
                // silently ignore missing process and use main monitor center.
                GetFullscreenRect();
                return;
            }
            else
            {
                Logger.Debug("Failed to get window bounds");
                await publisher.Publish(new UpdateStatus("Failed to get window bounds", StatusSeverity.Error));
                return;
            }
        }

        if (osRect.Left < -20000 || osRect.Top < -20000)
        {
            // If the window is in the VOID delete current process and re-set window to nothing
            Window = Rectangle.Empty;
        }
        else if (Window != default || Window.Left != osRect.Left || Window.Right != osRect.Right ||
                 Window.Top != osRect.Top || Window.Bottom != osRect.Bottom)
        {
            // Checks if old window size is the right size if not change it
            // get Rectangle out of rect
            Window = new Rectangle(osRect.Left, osRect.Top, osRect.Right - osRect.Left, osRect.Bottom - osRect.Top);

            // Rectangle is (x, y, width, height) RECT is (x, y, x+width, y+height)
            const int GWL_style = -16;
            const uint WS_BORDER = 0x00800000;
            const uint WS_POPUP = 0x80000000;

            var styles = Win32.GetWindowLongPtr(process.HandleRef, GWL_style);
            if ((styles & WS_POPUP) != 0)
            {
                // Borderless, don't do anything
                Logger.Debug("Borderless detected (0x{Styles:X8}, {Window})", styles, Window);
            }
            else if ((styles & WS_BORDER) != 0)
            {
                // Windowed, adjust for thick border
                // TODO (rudze) : Get real border sizes at some point
                Window = new Rectangle(Window.Left + 8, Window.Top + 30, Window.Width - 16, Window.Height - 38);
                Logger.Debug("Windowed detected (0x{Styles:X8}, adjusting window to: {Window})", styles, Window);
            }
            else
            {
                // Assume Fullscreen, don't do anything
                Logger.Debug("Fullscreen detected (0x{Styles:X8}, {Window})", styles, Window);
                //Show the Fullscreen prompt
                if (settings.IsOverlaySelected)
                {
                    Logger.Debug("Showing the Fullscreen Reminder");
                    await publisher.Publish(new FullscreenReminderShow(new Point(Window.X, Window.Y), new Point(Window.Width, Window.Height)));
                }
            }
        }
    }

    private void GetFullscreenRect()
    {
        var width = Screen.Bounds.Width;
        var height = Screen.Bounds.Height;

        Window = new Rectangle(0, 0, width, height);
        DpiScaling = 1;

        Logger.Debug("Couldn't Detect Warframe Process. Using Primary Screen Bounds: {Window}, named: {Name}", Window, Screen.DeviceName);
    }

    private void RefreshDPIScaling()
    {
        try
        {
            var mon = Win32.MonitorFromPoint(new Point(Screen.Bounds.Left + 1, Screen.Bounds.Top + 1), 2);
            Win32.GetDpiForMonitor(mon, Win32.DpiType.Effective, out var dpiXEffective, out _);

            Logger.Debug("Effective dpi, X: {DpiXEffective} Which is %: {DpiXEffectiveDiv / 96.0}", dpiXEffective, dpiXEffective / 96.0);

            // assuming that y and x axis dpi scaling will be uniform. So only need to check one value
            DpiScaling = dpiXEffective / 96.0;
        }
        catch (Exception e)
        {
            Logger.Error(e, "Was unable to set a new dpi scaling, defaulting to 100% zoom");
            DpiScaling = 1;
        }
    }
}
