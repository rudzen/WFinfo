using System.Runtime.CompilerServices;

namespace WFInfo.Services.OpticalCharacterRecognition;

public enum WFtheme
{
    VITRUVIAN,
    STALKER,
    BARUUK,
    CORPUS,
    FORTUNA,
    GRINEER,
    LOTUS,
    NIDUS,
    OROKIN,
    TENNO,
    HIGH_CONTRAST,
    LEGACY,
    EQUINOX,
    DARK_LOTUS,
    ZEPHYR,
    UNKNOWN = -1,
    AUTO = -2,
    CUSTOM = -3
}

public static class WFthemeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AsInt(this WFtheme t) => (int)t;

    public static string ToFriendlyString(this WFtheme theme)
    {
        return theme switch
        {
            WFtheme.VITRUVIAN => "Vitruvian",
            WFtheme.STALKER => "Stalker",
            WFtheme.BARUUK => "Baruuk",
            WFtheme.CORPUS => "Corpus",
            WFtheme.FORTUNA => "Fortuna",
            WFtheme.GRINEER => "Grineer",
            WFtheme.LOTUS => "Lotus",
            WFtheme.NIDUS => "Nidus",
            WFtheme.OROKIN => "Orokin",
            WFtheme.TENNO => "Tenno",
            WFtheme.HIGH_CONTRAST => "High Contrast",
            WFtheme.LEGACY => "Legacy",
            WFtheme.EQUINOX => "Equinox",
            WFtheme.DARK_LOTUS => "Dark Lotus",
            WFtheme.ZEPHYR => "Zephyr",
            WFtheme.UNKNOWN => "Unknown",
            WFtheme.AUTO => "Auto",
            WFtheme.CUSTOM => "Custom",
            _ => throw new ArgumentOutOfRangeException(nameof(theme), theme, null)
        };
    }
}
