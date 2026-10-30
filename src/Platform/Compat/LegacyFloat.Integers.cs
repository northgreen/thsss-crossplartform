using System;
using System.Numerics;

namespace SlimDX.Compat;

public static partial class LegacyFloat
{
    public static LegacyExtended FromInt32(int value) => FromInt64(value);
    public static LegacyExtended FromUInt32(uint value) => FromUInt64(value);

    public static LegacyExtended FromInt64(long value)
    {
        if (value >= 0) return FromUInt64((ulong)value);
        var result = FromUInt64(unchecked((ulong)(-(value + 1)) + 1));
        result.SignExp |= 0x8000;
        return result;
    }

    public static LegacyExtended FromUInt64(ulong value)
    {
        if (value == 0) return default;
        int bits = 64 - BitOperations.LeadingZeroCount(value);
        return new LegacyExtended { Significand = value << (64 - bits), SignExp = (ushort)(16382 + bits) };
    }

    private static bool TryTruncate(LegacyExtended value, out BigInteger result)
    {
        int exponent = value.SignExp & 0x7fff;
        if (exponent == 0x7fff) { result = default; return false; }
        int shift = (exponent == 0 ? 1 : exponent) - 16446;
        result = value.Significand;
        result = shift < 0 ? result >> -shift : result << shift;
        if ((value.SignExp & 0x8000) != 0) result = -result;
        return true;
    }

    // CLR2 x86 unchecked conversions use integer-indefinite sentinels, not exceptions/saturation.
    public static int ToInt32(LegacyExtended value) => TryTruncate(value, out var result) && result >= int.MinValue && result <= int.MaxValue ? (int)result : int.MinValue;
    public static long ToInt64(LegacyExtended value) => TryTruncate(value, out var result) && result >= long.MinValue && result <= long.MaxValue ? (long)result : long.MinValue;
    public static uint ToUInt32(LegacyExtended value) => unchecked((uint)ToInt64(value));
    public static byte ToByte(LegacyExtended value) => unchecked((byte)ToInt32(value));
    public static sbyte ToSByte(LegacyExtended value) => unchecked((sbyte)ToInt32(value));
    public static short ToInt16(LegacyExtended value) => unchecked((short)ToInt32(value));
    public static ushort ToUInt16(LegacyExtended value) => unchecked((ushort)ToInt32(value));

    public static ulong ToUInt64(LegacyExtended value)
    {
        var highBit = FromUInt64(1UL << 63);
        if (GreaterEqualUn(value, highBit))
            return unchecked((ulong)ToInt64(Sub(value, highBit))) ^ (1UL << 63);
        return unchecked((ulong)ToInt64(value));
    }

    private static BigInteger CheckedInteger(LegacyExtended value, BigInteger minimum, BigInteger maximum)
    {
        if (!TryTruncate(value, out var result) || result < minimum || result > maximum)
            throw new OverflowException("Legacy floating value is outside the checked integer conversion range");
        return result;
    }

    public static int ToInt32Checked(LegacyExtended value) => (int)CheckedInteger(value, int.MinValue, int.MaxValue);
    public static uint ToUInt32Checked(LegacyExtended value) => (uint)CheckedInteger(value, uint.MinValue, uint.MaxValue);
    public static long ToInt64Checked(LegacyExtended value) => (long)CheckedInteger(value, long.MinValue, long.MaxValue);
    public static ulong ToUInt64Checked(LegacyExtended value)
    {
        CheckedInteger(value, ulong.MinValue, ulong.MaxValue);
        // The original unsigned helper's subtraction also observes the thread precision.
        return ToUInt64(value);
    }
    public static byte ToByteChecked(LegacyExtended value) => (byte)CheckedInteger(value, byte.MinValue, byte.MaxValue);
    public static sbyte ToSByteChecked(LegacyExtended value) => (sbyte)CheckedInteger(value, sbyte.MinValue, sbyte.MaxValue);
    public static short ToInt16Checked(LegacyExtended value) => (short)CheckedInteger(value, short.MinValue, short.MaxValue);
    public static ushort ToUInt16Checked(LegacyExtended value) => (ushort)CheckedInteger(value, ushort.MinValue, ushort.MaxValue);

    // FPREM uses a truncated quotient and the dividend's sign, independently of the PC setting.
    public static LegacyExtended Rem(LegacyExtended a, LegacyExtended b)
    {
        int exponentA = a.SignExp & 0x7fff, exponentB = b.SignExp & 0x7fff;
        if (NaN(a)) { a.Significand |= 0x4000000000000000UL; return a; }
        if (NaN(b)) { b.Significand |= 0x4000000000000000UL; return b; }
        if (exponentA == 0x7fff || b.Significand == 0)
            return new LegacyExtended { SignExp = 0xffff, Significand = 0xc000000000000000UL };
        if (exponentB == 0x7fff || a.Significand == 0) return a;
        int powerA = (exponentA == 0 ? 1 : exponentA) - 16446;
        int powerB = (exponentB == 0 ? 1 : exponentB) - 16446;
        int common = powerA < powerB ? powerA : powerB;
        var remainder = ((BigInteger)a.Significand << (powerA - common)) % ((BigInteger)b.Significand << (powerB - common));
        if (remainder.IsZero) return new LegacyExtended { SignExp = (ushort)(a.SignExp & 0x8000) };
        int bits = (int)remainder.GetBitLength();
        if (bits > 64) throw new InvalidOperationException("Software remainder exceeded the extended significand");
        int exponent = common + bits - 1 + 16383;
        ulong significand = (ulong)(remainder << (64 - bits));
        if (exponent <= 0) { significand >>= 1 - exponent; exponent = 0; }
        return new LegacyExtended { Significand = significand, SignExp = (ushort)((a.SignExp & 0x8000) | exponent) };
    }
}
