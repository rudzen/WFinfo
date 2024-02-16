using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Serilog;
using WFInfo.Services.WindowInfo;
using WFInfo.Settings;

namespace WFInfo.Services.OpticalCharacterRecognition;

/// <summary>
/// Detects a warframe theme based on image data
/// </summary>
public sealed class ThemeDetector(IWindowInfoService window, ApplicationSettings settings) : IThemeDetector
{
    // Pixel measurements for reward screen @ 1920 x 1080 with 100% scale https://docs.google.com/drawings/d/1Qgs7FU2w1qzezMK-G1u9gMTsQZnDKYTEU36UPakNRJQ/edit
    private const int PixelRewardWidth = 968;
    private const int PixelRewardLineHeight = 48;

    private static readonly ILogger Logger = Log.Logger.ForContext<ThemeDetector>();

    private record struct ThemeWithThreshold(WFtheme Theme, int Threshold);

    private readonly Dictionary<Color, ThemeWithThreshold> _themeCache = new(256);

    // TODO (rudzen) : Add the following themes : Conquera, Deadlock, Lunar Renewal

    // TODO (rudzen) : Load from config
    // Colors for the top left "profile bar"
    private static readonly Memory<Color> ThemePrimary = new(
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
    ]);

    // TODO (rudzen) : Load from config
    // Highlight colors from selected items
    private static readonly Memory<Color> ThemeSecondary = new(
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
    ]);

    private static readonly WFtheme[] KnownThemes;

    static ThemeDetector()
    {
        var themes = (WFtheme[])Enum.GetValues(typeof(WFtheme));
        KnownThemes = new List<WFtheme>(themes.Where(theme => theme >= WFtheme.VITRUVIAN)).ToArray();
        Array.Sort(KnownThemes);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref Color PrimaryThemeColor(WFtheme theme)
    {
        ref var r = ref MemoryMarshal.GetReference(ThemePrimary.Span);
        return ref Unsafe.Add(ref r, theme.AsInt());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref Color SecondaryThemeColor(WFtheme theme)
    {
        ref var r = ref MemoryMarshal.GetReference(ThemeSecondary.Span);
        return ref Unsafe.Add(ref r, theme.AsInt());
    }

    /// <summary>
    /// Processes the theme, parse image to detect the theme in the image. Parse null to detect the theme from the screen.
    /// closeestThresh is used for getting the most "Accurate" result, anything over 100 is sure to be correct.
    /// </summary>
    /// <param name="closestThresh"></param>
    /// <param name="image"></param>
    /// <returns></returns>
    public unsafe WFtheme GetThemeWeighted(
        out double closestThresh,
        Bitmap image)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentOutOfRangeException.ThrowIfZero(image.Height);

        var start = Stopwatch.GetTimestamp();
        const int halfRewardLineHeight = PixelRewardLineHeight / 2;
        var lineHeight = (int)(halfRewardLineHeight * window.ScreenScaling);
        var mostWidth = (int)(PixelRewardWidth * window.ScreenScaling);

        Span<double> weights = stackalloc double[15];
        var minWidth = mostWidth / 4;

        var bitmapData = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, image.PixelFormat);
        var bytesPerPixel = Image.GetPixelFormatSize(image.PixelFormat) / 8;
        var heightInPixels = bitmapData.Height;
        var widthInBytes = bitmapData.Width * bytesPerPixel;

        var pixels = new Span<byte>(bitmapData.Scan0.ToPointer(), widthInBytes * heightInPixels);

        for (var y = lineHeight; y < heightInPixels; y++)
        {
            var perc = (y - lineHeight) / (double)(image.Height - lineHeight);
            var totWidth = (int)(minWidth * perc + minWidth);
            var currentLine = y * bitmapData.Stride;
            for (var x = 0; x < totWidth; x++)
            {
                var adjustedX = x + (mostWidth - totWidth) / 2;
                var buffer = pixels.Slice(currentLine + adjustedX * bytesPerPixel, bytesPerPixel);
                var color = Color.FromArgb(buffer[2], buffer[1], buffer[0]);
                ref var match = ref GetClosestTheme(in color);
                weights[match.Theme.AsInt()] += 1 / Math.Pow(match.Threshold + 1, 4);
            }
        }

        image.UnlockBits(bitmapData);

        var max = GetMaxWeight(weights);
        var active = (WFtheme)weights.IndexOf(max);

        closestThresh = max;

        var end = Stopwatch.GetElapsedTime(start);

        Logger.Debug("Theme detection complete. found={Active},weight={Weight},time={Time}",
            active, max.ToString("F2", ApplicationConstants.Culture), end);

        if (settings.ThemeSelection != WFtheme.AUTO)
        {
            Logger.Debug("Theme overwrite present. detected={Active},forced={Forced}",
                settings.ThemeSelection.ToFriendlyString(), active.ToFriendlyString());
            return settings.ThemeSelection;
        }

        return active;
    }

    public bool ThemeThresholdFilter(in Color test, WFtheme theme)
    {
        //treat unknown as custom, for safety
        if (theme is WFtheme.CUSTOM or WFtheme.UNKNOWN)
            return CustomThresholdFilter(in test);

        ref var primary = ref PrimaryThemeColor(theme);
        ref var secondary = ref SecondaryThemeColor(theme);

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

    private ref ThemeWithThreshold GetClosestTheme(in Color clr)
    {
        ref var cached = ref CollectionsMarshal.GetValueRefOrAddDefault(_themeCache, clr, out var exists);
        if (exists)
            return ref cached!;

        var threshold = 999;

        var knownThemes = KnownThemes.AsSpan();

        ref var minTheme = ref MemoryMarshal.GetReference(knownThemes);

        for (var i = 0; i < knownThemes.Length; i++)
        {
            ref var knownTheme = ref Unsafe.Add(ref minTheme, i);

            ref var themeColor = ref PrimaryThemeColor(knownTheme);
            var tempThresh = ColorDifference(in clr, in themeColor);
            if (tempThresh < threshold)
            {
                threshold = tempThresh;
                minTheme = knownTheme;
            }
        }

        cached = new ThemeWithThreshold(minTheme, threshold);
        return ref cached;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ColorDifference(in Color test, in Color thresh)
    {
        return Math.Abs(test.R - thresh.R) + Math.Abs(test.G - thresh.G) + Math.Abs(test.B - thresh.B);
    }

    private bool CustomThresholdFilter(in Color test)
    {
        if (settings.CF_usePrimaryHSL)
        {
            if (settings.CF_pHueMax >= test.GetHue() && test.GetHue() >= settings.CF_pHueMin &&
                settings.CF_pSatMax >= test.GetSaturation() && test.GetSaturation() >= settings.CF_pSatMin &&
                settings.CF_pBrightMax >= test.GetBrightness() && test.GetBrightness() >= settings.CF_pBrightMin)
                return true;
        }

        if (settings.CF_usePrimaryRGB)
        {
            if (settings.CF_pRMax >= test.R && test.R >= settings.CF_pRMin &&
                settings.CF_pGMax >= test.G && test.G >= settings.CF_pGMin &&
                settings.CF_pBMax >= test.B && test.B >= settings.CF_pBMin)
                return true;
        }

        if (settings.CF_useSecondaryHSL)
        {
            if (settings.CF_sHueMax >= test.GetHue() && test.GetHue() >= settings.CF_sHueMin &&
                settings.CF_sSatMax >= test.GetSaturation() && test.GetSaturation() >= settings.CF_sSatMin &&
                settings.CF_sBrightMax >= test.GetBrightness() && test.GetBrightness() >= settings.CF_sBrightMin)
                return true;
        }

        if (settings.CF_useSecondaryRGB)
        {
            if (settings.CF_sRMax >= test.R && test.R >= settings.CF_sRMin &&
                settings.CF_sGMax >= test.G && test.G >= settings.CF_sGMin &&
                settings.CF_sBMax >= test.B && test.B >= settings.CF_sBMin)
                return true;
        }


        return false;
    }
}
