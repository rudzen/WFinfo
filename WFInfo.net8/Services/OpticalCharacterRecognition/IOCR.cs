using System.Drawing;
using WFInfo.Types;

namespace WFInfo.Services.OpticalCharacterRecognition;

public interface IOCR
{
    double UiScaling { get; }

    int NumberOfRewardsDisplayed { get; }

    AtomicBoolean ProcessingActive { get; }
    Task ProcessRewardScreen(Bitmap? file = null);
    WFtheme GetThemeWeighted(out double closestThresh, Bitmap? image = null);

    /// <summary>
    /// Processes the image the user cropped in the selection
    /// </summary>
    /// <param name="snapItImage"></param>
    /// <param name="fullShot"></param>
    /// <param name="snapItOrigin"></param>
    Task ProcessSnapIt(
        Bitmap snapItImage,
        Bitmap fullShot,
        Point snapItOrigin);

    /// <summary>
    /// Process the profile screen to find owned items
    /// </summary>
    /// <param name="fullShot">Image to scan</param>
    Task ProcessProfileScreen(Bitmap fullShot);

    unsafe Bitmap ScaleUpAndFilter(Bitmap image, WFtheme active, out int[] rowHits, out int[] colHits);
    Task<Bitmap> CaptureScreenshot();
    Task SnapScreenshot();
}
