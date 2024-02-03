﻿using System.Drawing;
using System.Windows.Forms;
using Serilog;
using WFInfo.Services.WarframeProcess;
using WFInfo.Settings;

namespace WFInfo.Services.WindowInfo;

public class Win32WindowInfoService(IProcessFinder process, IReadOnlyApplicationSettings settings)
    : IWindowInfoService
{
    private static readonly ILogger Logger = Log.Logger.ForContext<Win32WindowInfoService>();
    
    public double DpiScaling { get; private set; }

    public double ScreenScaling
    {
        get
        {
            if (Window.Width         * 9 > Window.Height * 16) // image is less than 16:9 aspect
                return Window.Height / 1080.0;
            else
                return Window.Width / 1920.0; //image is higher than 16:9 aspect
        }
    }

    public Rectangle Window { get; private set; }
    
    public Point Center => new Point(Window.X + Window.Width / 2, Window.Y + Window.Height / 2);

    public Screen? Screen { get; private set; } = Screen.PrimaryScreen;

    public void UpdateWindow()
    {
        if (!process.IsRunning && !settings.Debug)
        {
            Logger.Debug("Failed to find warframe process for window info");
            Main.StatusUpdate("Failed to find warframe process for window info", 1);
            return;
        }

        Screen = Screen.FromHandle(process.HandleRef.Handle);
        string screenType = Screen.Primary ? "primary" : "secondary";

        if (process.GameIsStreamed)
            Logger.Debug("GFN -- Warframe display: {DeviceName}, {ScreenType}", Screen.DeviceName, screenType);
        else
            Logger.Debug("Warframe display: {DeviceName}, {ScreenType}", Screen.DeviceName, screenType);

        RefreshDPIScaling();
        GetWindowRect();
    }

    public void UseImage(Bitmap? bitmap)
    {
        int width = bitmap?.Width   ?? Screen.Bounds.Width;
        int height = bitmap?.Height ?? Screen.Bounds.Height;

        Window = new Rectangle(0, 0, width, height);
        DpiScaling = 1;

        if (bitmap is not null)
            Logger.Debug("DETECTED LOADED IMAGE BOUNDS: {Window}", Window);
        else
            Logger.Debug("Couldn't Detect Warframe Process. Using Primary Screen Bounds. window={Window},named={Name}",Window, Screen.DeviceName);
    }

    private void GetWindowRect()
    {
        if (!Win32.GetWindowRect(process.HandleRef, out Win32.R osRect))
        {
            if (settings.Debug)
            {
                //if debug is on AND warframe is not detected, sillently ignore missing process and use main monitor center.
                GetFullscreenRect();
                return;
            }
            else
            {
                Logger.Debug("Failed to get window bounds");
                Main.StatusUpdate("Failed to get window bounds", 1);
                return;
            }
        }

        if (osRect.Left < -20000 || osRect.Top < -20000)
        {
            // if the window is in the VOID delete current process and re-set window to nothing
            Window = Rectangle.Empty;
        }
        else if (Window != default || Window.Left   != osRect.Left || Window.Right != osRect.Right ||
                 Window.Top != osRect.Top || Window.Bottom != osRect.Bottom)
        {
            // checks if old window size is the right size if not change it
            // get Rectangle out of rect
            Window = new Rectangle(osRect.Left, osRect.Top, osRect.Right - osRect.Left, osRect.Bottom - osRect.Top);
            
            // Rectangle is (x, y, width, height) RECT is (x, y, x+width, y+height) 
            const int GWL_style = -16;
            const uint WS_BORDER = 0x00800000;
            const uint WS_POPUP = 0x80000000;

            uint styles = Win32.GetWindowLongPtr(process.HandleRef, GWL_style);
            if ((styles & WS_POPUP) != 0)
            {
                // Borderless, don't do anything
                Logger.Debug("Borderless detected (0x{Styles:X8}, {Window})", styles, Window);
            }
            else if ((styles & WS_BORDER) != 0)
            {
                // Windowed, adjust for thicc border
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
                    Main.RunOnUIThread(Main.SpawnFullscreenReminder);
                }
            }
        }
    }

    private void GetFullscreenRect()
    {
        int width = Screen.Bounds.Width;
        int height = Screen.Bounds.Height;

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