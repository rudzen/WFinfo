using System.Numerics;
using System.Runtime.CompilerServices;

namespace WFInfo.Domain;

[Flags]
public enum DataTypes
{
    None = 0,
    Equipment = 1 << 0,
    Relic = 1 << 1,
    Name = 1 << 2,
    MarketItems = 1 << 3,
    MarketData = 1 << 4,
    All = Equipment | Relic | Name | MarketItems | MarketData
}

public static class DataTypeExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasFlag(this DataTypes value, DataTypes flag) => (value & flag) == flag;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Contains(this DataTypes value, DataTypes flag) => (value & flag) != 0;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DataTypes PopLsb(ref DataTypes input)
    {
        var dt = input.Lsb();
        ResetLsb(ref input);
        return dt;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DataTypes Lsb(this DataTypes dt)
    {
        var tzc = BitOperations.TrailingZeroCount(dt.AsInt());
        return (DataTypes)(1 << tzc);
    }

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static void ResetLsb(ref DataTypes dt) => dt &= dt - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int PopCount(this DataTypes dt) => BitOperations.PopCount((uint)dt);

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DataTypes Clear(this DataTypes dt, DataTypes flag) => dt & ~flag;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DataTypes Set(this DataTypes dt, DataTypes flag) => dt | flag;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DataTypes Set(this DataTypes dt, DataTypes flag, bool value)
        => value ? dt | flag : dt & ~flag;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DataTypes Toggle(this DataTypes dt, DataTypes flag) => dt ^ flag;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static int AsInt(this DataTypes dt) => (int)dt;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static DataTypes AsDataType(this int value) => (DataTypes)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool MoreThanOne(this DataTypes v) => (v & (v - 1)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Index(this DataTypes cet)
    {
        // If no bit is set or more than one bit is set, return null
        if (cet == DataTypes.None || cet.MoreThanOne())
            return -1;

        // Convert enum value to ulong
        var mask = (uint)cet;

        // Use BitOperations to get the log2 value, which gives the bit index
        return BitOperations.Log2(mask);
    }

}
