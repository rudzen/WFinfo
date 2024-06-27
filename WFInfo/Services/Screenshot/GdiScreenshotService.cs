using System.Drawing;
using DotNext.Collections.Generic;
using WFInfo.Services.WindowInfo;

namespace WFInfo.Services.Screenshot;

public sealed class GdiScreenshotService(IWindowInfoService window) : IScreenshotService
{
    public Task<IReadOnlyList<Bitmap>> CaptureScreenshot()
    {
        window.UpdateWindow();

        var window1 = window.Window;
        var width = window1.Width;
        var height = window1.Height;

        if (window1 == null || window1.Width == 0 || window1.Height == 0)
        {
            window1 = window.Screen.Bounds;

            width *= (int)window.DpiScaling;
            height *= (int)window.DpiScaling;
        }

        var image = new Bitmap(width, height);
        var fullscreenSize = new Size(image.Width, image.Height);

        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(window1.Left, window1.Top, 0, 0, fullscreenSize, CopyPixelOperation.SourceCopy);
        }

        var result = List.Singleton(image);
        return Task.FromResult(result);
    }
}
