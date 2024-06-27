using System.Runtime.CompilerServices;

namespace WFInfo.Extensions;

public static class BoolExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe byte AsByte(this bool b) => *(byte*)&b;
}