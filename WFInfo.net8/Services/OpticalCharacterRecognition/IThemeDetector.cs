using System.Drawing;

namespace WFInfo.Services.OpticalCharacterRecognition;

public interface IThemeDetector
{
    Color PrimaryThemeColor(WFtheme theme);
    Color SecondaryThemeColor(WFtheme theme);

    /// <summary>
    /// Processes the theme, parse image to detect the theme in the image. Parse null to detect the theme from the screen.
    /// closeestThresh is used for getting the most "Accurate" result, anything over 100 is sure to be correct.
    /// </summary>
    /// <param name="closestThresh"></param>
    /// <param name="image"></param>
    /// <returns></returns>
    WFtheme GetThemeWeighted(
        out double closestThresh,
        Bitmap? image = null);

    bool ThemeThresholdFilter(in Color test, WFtheme theme);
}
