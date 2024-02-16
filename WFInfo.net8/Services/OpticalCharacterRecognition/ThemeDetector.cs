using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;
using Serilog;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;

namespace WFInfo.Services.OpticalCharacterRecognition;

/// <summary>
/// Detects a warframe theme based on image data
/// </summary>
public sealed class ThemeDetector : IThemeDetector
{
    // Pixel measurements for reward screen @ 1920 x 1080 with 100% scale https://docs.google.com/drawings/d/1Qgs7FU2w1qzezMK-G1u9gMTsQZnDKYTEU36UPakNRJQ/edit
    private const int PixelRewardWidth = 968;
    private const int PixelRewardLineHeight = 48;

    private static readonly ILogger Logger = Log.Logger.ForContext<ThemeDetector>();

#pragma warning disable IDE0044 // Add readonly modifier
    private static short[,,] GetThemeCache = new short[256, 256, 256];
    private static short[,,] GetThresholdCache = new short[256, 256, 256];
#pragma warning disable IDE0044 // Add readonly modifier

    // TODO (rudzen) : Load from config
    // Colors for the top left "profile bar"
    private static readonly Color[] ThemePrimary =
    [
        Color.FromArgb(190, 169, 102), //VITRUVIAN
        Color.FromArgb(153, 31, 35),   //STALKER
        Color.FromArgb(238, 193, 105), //BARUUK
        Color.FromArgb(35, 201, 245),  //CORPUS
        Color.FromArgb(57, 105, 192),  //FORTUNA
        Color.FromArgb(255, 189, 102), //GRINEER
        Color.FromArgb(36, 184, 242),  //LOTUS
        Color.FromArgb(140, 38, 92),   //NIDUS
        Color.FromArgb(20, 41, 29),    //OROKIN
        Color.FromArgb(9, 78, 106),    //TENNO
        Color.FromArgb(2, 127, 217),   //HIGH_CONTRAST
        Color.FromArgb(255, 255, 255), //LEGACY
        Color.FromArgb(158, 159, 167), //EQUINOX
        Color.FromArgb(140, 119, 147), //DARK_LOTUS
        Color.FromArgb(253, 132, 2)    //ZEPHYR
    ];

    // TODO (rudzen) : Load from config
    //highlight colors from selected items
    private static readonly Color[] ThemeSecondary =
    [
        Color.FromArgb(245, 227, 173), //VITRUVIAN
        Color.FromArgb(255, 61, 51),   //STALKER
        Color.FromArgb(236, 211, 162), //BARUUK
        Color.FromArgb(111, 229, 253), //CORPUS
        Color.FromArgb(255, 115, 230), //FORTUNA
        Color.FromArgb(255, 224, 153), //GRINEER
        Color.FromArgb(255, 241, 191), //LOTUS
        Color.FromArgb(245, 73, 93),   //NIDUS
        Color.FromArgb(178, 125, 5),   //OROKIN
        Color.FromArgb(6, 106, 74),    //TENNO
        Color.FromArgb(255, 255, 0),   //HIGH_CONTRAST
        Color.FromArgb(232, 213, 93),  //LEGACY
        Color.FromArgb(232, 227, 227), //EQUINOX
        Color.FromArgb(189, 169, 237), //DARK_LOTUS
        Color.FromArgb(255, 53, 0)     //ZEPHYR
    ];

    private static readonly WFtheme[] KnownThemes;

    static ThemeDetector()
    {
        var themes = (WFtheme[])Enum.GetValues(typeof(WFtheme));
        KnownThemes = new List<WFtheme>(themes.Where(theme => (int)theme >= 0)).ToArray();
        Array.Sort(KnownThemes);
    }

    private readonly IWindowInfoService _window;
    private readonly ApplicationSettings _settings;

    public ThemeDetector(IWindowInfoService window, ApplicationSettings settings)
    {
        _window = window;
        _settings = settings;
    }

    public Color PrimaryThemeColor(WFtheme theme)
    {
        return ThemePrimary[(int)theme];
    }

    public Color SecondaryThemeColor(WFtheme theme)
    {
        return ThemeSecondary[(int)theme];
    }

    /// <summary>
    /// Processes the theme, parse image to detect the theme in the image. Parse null to detect the theme from the screen.
    /// closeestThresh is used for getting the most "Accurate" result, anything over 100 is sure to be correct.
    /// </summary>
    /// <param name="closestThresh"></param>
    /// <param name="image"></param>
    /// <returns></returns>
    public WFtheme GetThemeWeighted(
        out double closestThresh,
        Bitmap? image = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfZero(image.Height);

        var lineHeight = (int)(PixelRewardLineHeight / 2 * _window.ScreenScaling);
        var mostWidth = (int)(PixelRewardWidth * _window.ScreenScaling);

        Span<double> weights = stackalloc double[15];
        var minWidth = mostWidth / 4;

        for (var y = lineHeight; y < image.Height; y++)
        {
            var perc = (y - lineHeight) / (double)(image.Height - lineHeight);
            var totWidth = (int)(minWidth * perc + minWidth);
            for (var x = 0; x < totWidth; x++)
            {
                var match = (int)GetClosestTheme(image.GetPixel(x + (mostWidth - totWidth) / 2, y), out var thresh);

                weights[match] += 1 / Math.Pow(thresh + 1, 4);
            }
        }

        var simdMax = GetMaxWeight(weights);
        var simdActive = (WFtheme)weights.IndexOf(simdMax);

        Logger.Debug("CLOSEST THEME ({Culture}): {Active}", simdMax.ToString("F2", ApplicationConstants.Culture), simdActive);

        closestThresh = simdMax;
        if (_settings.ThemeSelection != WFtheme.AUTO)
        {
            Logger.Debug("Theme overwrite present, setting to: {ThemeSelection}", _settings.ThemeSelection.ToString());
            return _settings.ThemeSelection;
        }

        return simdActive;
    }

    public bool ThemeThresholdFilter(in Color test, WFtheme theme)
    {
        //treat unknown as custom, for safety
        if (theme is WFtheme.CUSTOM or WFtheme.UNKNOWN)
            return CustomThresholdFilter(test);

        var primary = PrimaryThemeColor(theme);
        var secondary = SecondaryThemeColor(theme);

        // TODO (rudzen) : wtf is this
        return theme switch
        {
            // TO CHECK
            WFtheme.VITRUVIAN => Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.25 && test.GetBrightness() >= 0.42,
            WFtheme.LOTUS => Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetSaturation() >= 0.65 && Math.Abs(test.GetBrightness() - primary.GetBrightness()) <= 0.1
                             || (Math.Abs(test.GetHue() - secondary.GetHue()) < 15 && test.GetBrightness() >= 0.65),
            // TO CHECK
            WFtheme.OROKIN => (Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetBrightness() <= 0.42 && test.GetSaturation() >= 0.1) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 5
                && test.GetBrightness() <= 0.5 && test.GetBrightness() >= 0.25
                && test.GetSaturation() >= 0.25),
            WFtheme.STALKER => ((Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.55) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetSaturation() >= 0.66))
                               && test.GetBrightness() >= 0.25,
            WFtheme.CORPUS  => Math.Abs(test.GetHue() - primary.GetHue()) < 3 && test.GetBrightness() >= 0.42 && test.GetSaturation() >= 0.35,
            WFtheme.EQUINOX => test.GetSaturation() <= 0.2 && test.GetBrightness() >= 0.55,
            WFtheme.DARK_LOTUS => (Math.Abs(test.GetHue() - secondary.GetHue()) < 20 && test.GetBrightness() >= 0.35 && test.GetBrightness() <= 0.55 && test.GetSaturation() <= 0.25
                                   && test.GetSaturation() >= 0.05) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetBrightness() >= 0.50 && test.GetSaturation() >= 0.20),
            WFtheme.FORTUNA => ((Math.Abs(test.GetHue() - primary.GetHue()) < 3 && test.GetBrightness() >= 0.35) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetBrightness() >= 0.15))
                               && test.GetSaturation() >= 0.20,
            WFtheme.HIGH_CONTRAST => (Math.Abs(test.GetHue() - primary.GetHue()) < 3 || Math.Abs(test.GetHue() - secondary.GetHue()) < 2) && test.GetSaturation() >= 0.75
                && test.GetBrightness() >= 0.35 // || Math.Abs(test.GetHue() - secondary.GetHue()) < 2;
            ,
            // TO CHECK
            WFtheme.LEGACY => (test.GetBrightness() >= 0.65) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 6 && test.GetBrightness() >= 0.5 && test.GetSaturation() >= 0.5),
            WFtheme.NIDUS => (Math.Abs(test.GetHue() - (primary.GetHue() + 6)) < 8 && test.GetSaturation() >= 0.30)
                             || (Math.Abs(test.GetHue() - secondary.GetHue()) < 15 && test.GetSaturation() >= 0.55),
            WFtheme.TENNO   => (Math.Abs(test.GetHue() - primary.GetHue()) < 3 || Math.Abs(test.GetHue() - secondary.GetHue()) < 2) && test.GetSaturation() >= 0.38 && test.GetBrightness() <= 0.55,
            WFtheme.BARUUK  => (Math.Abs(test.GetHue() - primary.GetHue()) < 2) && test.GetSaturation() > 0.25 && test.GetBrightness() > 0.5,
            WFtheme.GRINEER => (Math.Abs(test.GetHue() - primary.GetHue()) < 5 && test.GetBrightness() > 0.5) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 6 && test.GetBrightness() > 0.55),
            WFtheme.ZEPHYR => ((Math.Abs(test.GetHue() - primary.GetHue()) < 4 && test.GetSaturation() >= 0.55) || (Math.Abs(test.GetHue() - secondary.GetHue()) < 4 && test.GetSaturation() >= 0.66))
                              && test.GetBrightness() >= 0.25,
            var _ => Math.Abs(test.GetHue() - primary.GetHue()) < 2 || Math.Abs(test.GetHue() - secondary.GetHue()) < 2
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double GetMaxWeight(ReadOnlySpan<double> span)
    {
        double result;
        int index;

        if (Vector.IsHardwareAccelerated && span.Length >= Vector<double>.Count * 2)
        {
            var maxes = new Vector<double>(span);
            index = Vector<double>.Count;
            do
            {
                var right = new Vector<double>(span[index..]);
                maxes = Vector.Max(maxes, right);
                index += Vector<double>.Count;
            } while (index + Vector<double>.Count <= span.Length);

            result = maxes[0];
            for (var i = 1; i < Vector<double>.Count; i++)
            {
                if (maxes[i] > result)
                    result = maxes[i];
            }
        }
        else
        {
            result = span[0];
            index = 1;
        }

        for (var i = index; (uint)i < (uint)span.Length; i++)
        {
            if (span[i] > result)
                result = span[i];
        }

        return result;
    }

    private static WFtheme GetClosestTheme(Color clr, out int threshold)
    {
        threshold = 999;
        var minTheme = WFtheme.CORPUS;
        if (GetThemeCache[clr.R, clr.G, clr.B] > 0)
        {
            threshold = GetThresholdCache[clr.R, clr.G, clr.B];
            return (WFtheme)(GetThemeCache[clr.R, clr.G, clr.B] - 1);
        }

        foreach (var theme in KnownThemes)
        {
            //ignore special theme values
            if ((int)theme < 0)
                continue;

            var themeColor = ThemePrimary[(int)theme];
            var tempThresh = ColorDifference(clr, themeColor);
            if (tempThresh < threshold)
            {
                threshold = tempThresh;
                minTheme = theme;
            }
        }

        GetThemeCache[clr.R, clr.G, clr.B] = (byte)(minTheme + 1);
        GetThresholdCache[clr.R, clr.G, clr.B] = (byte)threshold;
        return minTheme;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ColorDifference(Color test, Color thresh)
    {
        return Math.Abs(test.R - thresh.R) + Math.Abs(test.G - thresh.G) + Math.Abs(test.B - thresh.B);
    }

    private bool CustomThresholdFilter(Color test)
    {
        if (_settings.CF_usePrimaryHSL)
        {
            if (_settings.CF_pHueMax >= test.GetHue() && test.GetHue() >= _settings.CF_pHueMin &&
                _settings.CF_pSatMax >= test.GetSaturation() && test.GetSaturation() >= _settings.CF_pSatMin &&
                _settings.CF_pBrightMax >= test.GetBrightness() && test.GetBrightness() >= _settings.CF_pBrightMin)
                return true;
        }

        if (_settings.CF_usePrimaryRGB)
        {
            if (_settings.CF_pRMax >= test.R && test.R >= _settings.CF_pRMin &&
                _settings.CF_pGMax >= test.G && test.G >= _settings.CF_pGMin &&
                _settings.CF_pBMax >= test.B && test.B >= _settings.CF_pBMin)
                return true;
        }

        if (_settings.CF_useSecondaryHSL)
        {
            if (_settings.CF_sHueMax >= test.GetHue() && test.GetHue() >= _settings.CF_sHueMin &&
                _settings.CF_sSatMax >= test.GetSaturation() && test.GetSaturation() >= _settings.CF_sSatMin &&
                _settings.CF_sBrightMax >= test.GetBrightness() && test.GetBrightness() >= _settings.CF_sBrightMin)
                return true;
        }

        if (_settings.CF_useSecondaryRGB)
        {
            if (_settings.CF_sRMax >= test.R && test.R >= _settings.CF_sRMin &&
                _settings.CF_sGMax >= test.G && test.G >= _settings.CF_sGMin &&
                _settings.CF_sBMax >= test.B && test.B >= _settings.CF_sBMin)
                return true;
        }


        return false;
    }
}
