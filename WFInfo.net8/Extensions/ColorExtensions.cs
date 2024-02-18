using System.Drawing;
using System.Runtime.CompilerServices;

namespace WFInfo.Extensions;

public static class ColorExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color FromBgra(this ReadOnlySpan<byte> bgra)
    {
        return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Color FromBgra(this Span<byte> bgra)
    {
        return Color.FromArgb(bgra[3], bgra[2], bgra[1], bgra[0]);
    }
}
