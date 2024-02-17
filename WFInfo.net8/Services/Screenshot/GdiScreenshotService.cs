using System.Drawing;
using WFInfo.Services.WindowInfo;

namespace WFInfo.Services.Screenshot;

public class GdiScreenshotService : IScreenshotService
{
    private static IWindowInfoService _window;

    public GdiScreenshotService(IWindowInfoService window)
    {
        _window = window;
    }

    public Task<List<Bitmap>> CaptureScreenshot()
    {
        _window.UpdateWindow();

        var window = _window.Window;
        var center = _window.Center;

        var width = window.Width;
        var height = window.Height;

        if (window == null || window.Width == 0 || window.Height == 0)
        {
            window = _window.Screen.Bounds;
            center = new Point(window.X + window.Width / 2, window.Y + window.Height / 2);

            width *= (int)_window.DpiScaling;
            height *= (int)_window.DpiScaling;
        }

        var image = new Bitmap(width, height);
        var FullscreenSize = new Size(image.Width, image.Height);

        using (var graphics = Graphics.FromImage(image))
        {
            graphics.CopyFromScreen(window.Left, window.Top, 0, 0, FullscreenSize, CopyPixelOperation.SourceCopy);
        }

        var result = new List<Bitmap> { image };
        return Task.FromResult(result);
    }
}