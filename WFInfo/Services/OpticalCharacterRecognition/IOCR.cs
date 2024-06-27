using System.Drawing;
using WFInfo.Types;

namespace WFInfo.Services.OpticalCharacterRecognition;

public interface IOCR
{
    double UiScaling { get; }

    int NumberOfRewardsDisplayed { get; }

    AtomicBoolean ProcessingActive { get; }
    Task ProcessRewardScreen(Bitmap? file = null);

    string RewardScreenClipboard(
        in double platinum,
        string correctName,
        string plat,
        string? primeSetPlat,
        string ducats,
        bool vaulted,
        int partNumber);

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

    Bitmap ScaleUpAndFilter(Bitmap image, WFtheme active, out int[] rowHits, out int[] colHits);
    Task<Bitmap> CaptureScreenshot();
}
