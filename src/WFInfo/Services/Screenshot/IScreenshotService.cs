using System.Drawing;

namespace WFInfo.Services.Screenshot;

public enum HdrSupport
{
    Auto,
    On,
    Off
}

/// <summary>
/// Provides game screenshots.
/// </summary>
public interface IScreenshotService
{
    /// <summary>
    /// Captures one or more screenshots of the game. All screenshots are in SDR.
    /// </summary>
    /// <returns>Captured screenshots</returns>
    Task<IReadOnlyList<Bitmap>> CaptureScreenshot();
}
