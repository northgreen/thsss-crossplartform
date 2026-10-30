using System;
using System.Globalization;
using System.Numerics;

namespace SlimDX.Compat;

public static partial class LegacyFloat
{
    /// <summary>CLR2's seven-significant-digit script region, including its away-from-zero decimal ties.</summary>
    public static string FormatScriptRegion(float value)
    {
        uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        uint magnitude = bits & 0x7fffffff;
        var format = CultureInfo.CurrentCulture.NumberFormat;
        if (magnitude == 0) return "0";
        // Special symbols remain parseable by the caller's culture; no finite rounding is involved.
        if (magnitude >= 0x7f800000) return value.ToString(CultureInfo.CurrentCulture);
        int binaryExponent = (int)(magnitude >> 23);
        BigInteger numerator = (magnitude & 0x7fffff) | (binaryExponent == 0 ? 0u : 0x800000u);
        BigInteger denominator = BigInteger.One;
        int power = (binaryExponent == 0 ? 1 : binaryExponent) - 150;
        if (power >= 0) numerator <<= power;
        else denominator <<= -power;
        int exponent = DecimalExponent(numerator, denominator);
        string digits = SignificantDigits(numerator, denominator, ref exponent);
        string body = ScriptDigits(digits, exponent, format);
        return (bits & 0x80000000) != 0 ? format.NegativeSign + body : body;
    }

    private static int DecimalExponent(BigInteger numerator, BigInteger denominator)
    {
        int exponent = numerator.ToString(CultureInfo.InvariantCulture).Length - denominator.ToString(CultureInfo.InvariantCulture).Length;
        bool below = exponent >= 0 ? numerator < denominator * BigInteger.Pow(10, exponent)
            : numerator * BigInteger.Pow(10, -exponent) < denominator;
        return below ? exponent - 1 : exponent;
    }

    private static string SignificantDigits(BigInteger numerator, BigInteger denominator, ref int exponent)
    {
        int decimalScale = 6 - exponent;
        if (decimalScale >= 0) numerator *= BigInteger.Pow(10, decimalScale);
        else denominator *= BigInteger.Pow(10, -decimalScale);
        var coefficient = BigInteger.DivRem(numerator, denominator, out var remainder);
        // Modern G7 uses ties-to-even. The original default Single formatter rounded a decimal tie away.
        if (remainder * 2 >= denominator) coefficient++;
        if (coefficient >= 10000000) { coefficient /= 10; exponent++; }
        return coefficient.ToString(CultureInfo.InvariantCulture).TrimEnd('0');
    }

    private static string ScriptDigits(string digits, int exponent, NumberFormatInfo format)
    {
        if (exponent < -4 || exponent >= 7)
            return digits[..1] + (digits.Length > 1 ? format.NumberDecimalSeparator + digits[1..] : "") + "E" +
                (exponent < 0 ? format.NegativeSign : format.PositiveSign) + Math.Abs(exponent).ToString("D2", CultureInfo.InvariantCulture);
        int point = exponent + 1;
        return point <= 0 ? "0" + format.NumberDecimalSeparator + new string('0', -point) + digits
            : point >= digits.Length ? digits + new string('0', point - digits.Length)
            : digits[..point] + format.NumberDecimalSeparator + digits[point..];
    }
}
