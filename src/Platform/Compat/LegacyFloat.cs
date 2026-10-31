using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SlimDX.Compat;

/// <summary>Portable x87 value; never substitute the host long-double or double ABI.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public struct LegacyExtended
{
    public ulong Significand;
    public ushort SignExp;
    public ushort Pad0, Pad1, Pad2;
}

/// <summary>Legacy arithmetic used by compiled gameplay, isolated from rendering and host FPU state.</summary>
public static partial class LegacyFloat
{
    private const string Kernel = "thsss_legacy_math";
    [ThreadStatic] private static int precision;

    static LegacyFloat()
    {
        string requested = Environment.GetEnvironmentVariable("THSSS_MATH_BACKEND");
        int backend = requested switch
        {
            null or "" or "auto" => 0,
            "software" => 1,
            "x87" => 2,
            _ => throw new ArgumentException("THSSS_MATH_BACKEND must be auto, software or x87")
        };
        int status = SetBackend(backend);
        if (status == -2) throw new PlatformNotSupportedException("The current native math library does not support hardware x87");
        Check(status);
    }

    /// <summary>Actual selected backend; automatic selection prefers x87 when supported.</summary>
    public static string Backend => GetBackend() switch
    {
        1 => "software",
        2 => "x87",
        _ => throw new InvalidOperationException("Native gameplay numeric library returned an invalid backend")
    };

    [DllImport(Kernel, EntryPoint = "legacy_math_set_backend", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int SetBackend(int backend);
    [DllImport(Kernel, EntryPoint = "legacy_math_get_backend", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int GetBackend();

    // CLR2 starts new managed threads at 53 bits; original D3D creation changes its calling thread to 24.
    public static int Precision
    {
        get => precision == 0 ? 53 : precision;
        set => precision = value is 24 or 53 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "Significand precision must be 24 or 53");
    }

    public static void InitializeGameplayThread()
    {
        Precision = 24;
        Console.WriteLine("[MATH] backend=" + Backend + " precision=24");
    }

    private static void Check(int status)
    {
        if (status != 0) throw new InvalidOperationException("Portable gameplay numeric ABI rejected an operation: status=" + status);
    }

    [DllImport(Kernel, EntryPoint = "legacy_ext_store_float", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int StoreSingle(in LegacyExtended value, out float result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_store_double", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int StoreDouble(in LegacyExtended value, out double result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_sin", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int SinRaw(in LegacyExtended value, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_cos", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int CosRaw(in LegacyExtended value, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_atan2", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int AtanRaw(in LegacyExtended y, in LegacyExtended x, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_sqrt", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int SqrtRaw(in LegacyExtended value, int bits, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_add", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int AddRaw(in LegacyExtended a, in LegacyExtended b, int bits, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_sub", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int SubRaw(in LegacyExtended a, in LegacyExtended b, int bits, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_mul", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int MulRaw(in LegacyExtended a, in LegacyExtended b, int bits, out LegacyExtended result);
    [DllImport(Kernel, EntryPoint = "legacy_ext_div", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int DivRaw(in LegacyExtended a, in LegacyExtended b, int bits, out LegacyExtended result);

    // Exact bit expansion needs no arithmetic or native transition, even for DAZ/FTZ inputs.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LegacyExtended FromSingle(float value)
    {
        uint bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
        ushort sign = (ushort)((bits >> 16) & 0x8000);
        uint exponent = (bits >> 23) & 0xff;
        ulong fraction = bits & 0x7fffff;
        if (exponent == 0)
            return fraction == 0 ? new LegacyExtended { SignExp = sign } : ExpandSubnormal(fraction, sign, 16234);
        if (exponent == 0xff)
            return new LegacyExtended { SignExp = (ushort)(sign | 0x7fff),
                Significand = fraction == 0 ? 0x8000000000000000UL : 0xc000000000000000UL | (fraction << 40) };
        return new LegacyExtended { SignExp = (ushort)(sign | (exponent + 16256)), Significand = (0x800000UL | fraction) << 40 };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static LegacyExtended FromDouble(double value)
    {
        ulong bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
        ushort sign = (ushort)((bits >> 48) & 0x8000);
        uint exponent = (uint)((bits >> 52) & 0x7ff);
        ulong fraction = bits & 0xfffffffffffff;
        if (exponent == 0)
            return fraction == 0 ? new LegacyExtended { SignExp = sign } : ExpandSubnormal(fraction, sign, 15309);
        if (exponent == 0x7ff)
            return new LegacyExtended { SignExp = (ushort)(sign | 0x7fff),
                Significand = fraction == 0 ? 0x8000000000000000UL : 0xc000000000000000UL | (fraction << 11) };
        return new LegacyExtended { SignExp = (ushort)(sign | (exponent + 15360)), Significand = (0x10000000000000UL | fraction) << 11 };
    }

    private static LegacyExtended ExpandSubnormal(ulong fraction, ushort sign, int exponentBias)
    {
        int highest = 63 - BitOperations.LeadingZeroCount(fraction);
        return new LegacyExtended { SignExp = (ushort)(sign | (highest + exponentBias)), Significand = fraction << (63 - highest) };
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ToSingle(LegacyExtended value)
    {
        if (TryExactStore(value, 23, 127, out ulong bits))
            return BitConverter.Int32BitsToSingle(unchecked((int)bits));
        Check(StoreSingle(value, out var result)); return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double ToDouble(LegacyExtended value)
    {
        if (TryExactStore(value, 52, 1023, out ulong bits))
            return BitConverter.Int64BitsToDouble(unchecked((long)bits));
        Check(StoreDouble(value, out var result)); return result;
    }

    // Only encode values already exactly representable. Every rounding or unusual
    // ext80 case stays with the native codec, including all NaNs and infinities.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryExactStore(LegacyExtended value, int fractionBits, int bias, out ulong bits)
    {
        int exponent = value.SignExp & 0x7fff;
        bits = (ulong)(value.SignExp >> 15) << (fractionBits == 23 ? 31 : 63);
        if (value.Significand == 0 && exponent == 0) return true;
        if (exponent is 0 or 0x7fff || (value.Significand & 0x8000000000000000UL) == 0) return false;
        int power = exponent - 16383;
        int shift;
        bool normal = power >= 1 - bias && power <= bias;
        if (normal) shift = 63 - fractionBits;
        else if (power >= 1 - bias - fractionBits && power < 1 - bias)
            shift = 63 - (power - (1 - bias - fractionBits));
        else return false;
        if ((value.Significand & ((1UL << shift) - 1)) != 0) return false;
        ulong significand = value.Significand >> shift;
        bits |= normal ? ((ulong)(power + bias) << fractionBits) | (significand & ((1UL << fractionBits) - 1)) : significand;
        return true;
    }
    public static LegacyExtended NarrowSingle(LegacyExtended value) => FromSingle(ToSingle(value));
    public static LegacyExtended NarrowDouble(LegacyExtended value) => FromDouble(ToDouble(value));

    // Native Math argument slots store binary64; the transcendental return stays extended until used/stored.
    public static LegacyExtended Sin(LegacyExtended value) { value = NarrowDouble(value); Check(SinRaw(value, out var result)); return result; }
    public static LegacyExtended Cos(LegacyExtended value) { value = NarrowDouble(value); Check(CosRaw(value, out var result)); return result; }
    public static LegacyExtended Atan2(LegacyExtended y, LegacyExtended x)
    {
        y = NarrowDouble(y); x = NarrowDouble(x);
        // CLR2's Math wrapper handles two infinities with x/y before its FPATAN path.
        if (Infinity(y) && Infinity(x)) return Div(x, y);
        Check(AtanRaw(y, x, out var result));
        return result;
    }
    public static LegacyExtended Atan(LegacyExtended value) => Atan2(value, FromInt32(1));
    public static LegacyExtended Sqrt(LegacyExtended value) { Check(SqrtRaw(value, Precision, out var result)); return result; }
    public static LegacyExtended Add(LegacyExtended a, LegacyExtended b) { Check(AddRaw(a, b, Precision, out var result)); return result; }
    public static LegacyExtended Sub(LegacyExtended a, LegacyExtended b) { Check(SubRaw(a, b, Precision, out var result)); return result; }
    public static LegacyExtended Mul(LegacyExtended a, LegacyExtended b) { Check(MulRaw(a, b, Precision, out var result)); return result; }
    public static LegacyExtended Div(LegacyExtended a, LegacyExtended b) { Check(DivRaw(a, b, Precision, out var result)); return result; }
    public static LegacyExtended Neg(LegacyExtended value) { value.SignExp ^= 0x8000; return value; }
    public static LegacyExtended AbsSingle(LegacyExtended value) { value = NarrowSingle(value); value.SignExp &= 0x7fff; return value; }
    public static LegacyExtended AbsDouble(LegacyExtended value) { value = NarrowDouble(value); value.SignExp &= 0x7fff; return value; }

    private static bool NaN(LegacyExtended value) => (value.SignExp & 0x7fff) == 0x7fff && (value.Significand & 0x7fffffffffffffffUL) != 0;
    private static bool Infinity(LegacyExtended value) => (value.SignExp & 0x7fff) == 0x7fff && value.Significand == 0x8000000000000000UL;

    // Canonical software values sort by signed exponent/significand. -2 explicitly denotes unordered.
    private static int Compare(LegacyExtended a, LegacyExtended b)
    {
        if (NaN(a) || NaN(b)) return -2;
        if (a.Significand == 0 && b.Significand == 0) return 0;
        bool negativeA = (a.SignExp & 0x8000) != 0, negativeB = (b.SignExp & 0x8000) != 0;
        if (negativeA != negativeB) return negativeA ? -1 : 1;
        int order = (a.SignExp & 0x7fff).CompareTo(b.SignExp & 0x7fff);
        if (order == 0) order = a.Significand.CompareTo(b.Significand);
        return negativeA ? -order : order;
    }

    public static bool Equal(LegacyExtended a, LegacyExtended b) => Compare(a, b) == 0;
    public static bool NotEqual(LegacyExtended a, LegacyExtended b) => Compare(a, b) != 0;
    public static bool Greater(LegacyExtended a, LegacyExtended b) => Compare(a, b) == 1;
    public static bool Less(LegacyExtended a, LegacyExtended b) => Compare(a, b) == -1;
    public static bool GreaterEqual(LegacyExtended a, LegacyExtended b) => Compare(a, b) is 0 or 1;
    public static bool LessEqual(LegacyExtended a, LegacyExtended b) => Compare(a, b) is 0 or -1;
    public static bool GreaterUn(LegacyExtended a, LegacyExtended b) => Compare(a, b) is 1 or -2;
    public static bool LessUn(LegacyExtended a, LegacyExtended b) => Compare(a, b) is -1 or -2;
    public static bool GreaterEqualUn(LegacyExtended a, LegacyExtended b) => Compare(a, b) != -1;
    public static bool LessEqualUn(LegacyExtended a, LegacyExtended b) => Compare(a, b) != 1;
}
