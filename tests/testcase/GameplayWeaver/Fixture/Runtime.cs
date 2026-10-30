using System.Runtime.InteropServices;

// Mechanism-test doubles only: these do NOT emulate x87 or prove gameplay parity.
namespace SlimDX.Compat
{
    [StructLayout(LayoutKind.Sequential, Size = 16)]
    public struct LegacyExtended { public double Value; public ulong Padding; }
    public static class LegacyFloat
    {
        public static LegacyExtended FromSingle(float value) => new() { Value = value };
        public static LegacyExtended FromDouble(double value) => new() { Value = value };
        public static float ToSingle(LegacyExtended value) => (float)value.Value;
        public static double ToDouble(LegacyExtended value) => value.Value;
        public static LegacyExtended NarrowSingle(LegacyExtended value) => FromSingle(ToSingle(value));
        public static LegacyExtended NarrowDouble(LegacyExtended value) => value;
        public static LegacyExtended FromInt32(int value) => FromDouble(value);
        public static LegacyExtended FromUInt32(uint value) => FromDouble(value);
        public static LegacyExtended FromInt64(long value) => FromDouble(value);
        public static LegacyExtended FromUInt64(ulong value) => FromDouble(value);
        public static int ToInt32(LegacyExtended value) => unchecked((int)value.Value);
        public static uint ToUInt32(LegacyExtended value) => unchecked((uint)value.Value);
        public static long ToInt64(LegacyExtended value) => unchecked((long)value.Value);
        public static ulong ToUInt64(LegacyExtended value) => unchecked((ulong)value.Value);
        public static byte ToByte(LegacyExtended value) => unchecked((byte)(int)value.Value);
        public static sbyte ToSByte(LegacyExtended value) => unchecked((sbyte)(int)value.Value);
        public static short ToInt16(LegacyExtended value) => unchecked((short)(int)value.Value);
        public static ushort ToUInt16(LegacyExtended value) => unchecked((ushort)(int)value.Value);
        public static int ToInt32Checked(LegacyExtended value) => checked((int)value.Value);
        public static uint ToUInt32Checked(LegacyExtended value) => checked((uint)value.Value);
        public static long ToInt64Checked(LegacyExtended value) => checked((long)value.Value);
        public static ulong ToUInt64Checked(LegacyExtended value) => checked((ulong)value.Value);
        public static byte ToByteChecked(LegacyExtended value) => checked((byte)value.Value);
        public static sbyte ToSByteChecked(LegacyExtended value) => checked((sbyte)value.Value);
        public static short ToInt16Checked(LegacyExtended value) => checked((short)value.Value);
        public static ushort ToUInt16Checked(LegacyExtended value) => checked((ushort)value.Value);
        public static LegacyExtended Add(LegacyExtended a, LegacyExtended b) => FromDouble(a.Value + b.Value);
        public static LegacyExtended Sub(LegacyExtended a, LegacyExtended b) => FromDouble(a.Value - b.Value);
        public static LegacyExtended Mul(LegacyExtended a, LegacyExtended b) => FromDouble(a.Value * b.Value);
        public static LegacyExtended Div(LegacyExtended a, LegacyExtended b) => FromDouble(a.Value / b.Value);
        public static LegacyExtended Rem(LegacyExtended a, LegacyExtended b) => FromDouble(a.Value % b.Value);
        public static LegacyExtended Neg(LegacyExtended a) => FromDouble(-a.Value);
        public static LegacyExtended Sin(LegacyExtended a) => FromDouble(Math.Sin(a.Value));
        public static LegacyExtended Cos(LegacyExtended a) => FromDouble(Math.Cos(a.Value));
        public static LegacyExtended Atan(LegacyExtended a) => FromDouble(Math.Atan(a.Value));
        public static LegacyExtended Atan2(LegacyExtended a, LegacyExtended b) => FromDouble(Math.Atan2(a.Value, b.Value));
        public static LegacyExtended Sqrt(LegacyExtended a) => FromDouble(Math.Sqrt(a.Value));
        public static LegacyExtended AbsSingle(LegacyExtended a) => FromSingle(Math.Abs(ToSingle(a)));
        public static LegacyExtended AbsDouble(LegacyExtended a) => FromDouble(Math.Abs(a.Value));
        static bool Unordered(LegacyExtended a, LegacyExtended b) => double.IsNaN(a.Value) || double.IsNaN(b.Value);
        public static bool Equal(LegacyExtended a, LegacyExtended b) => a.Value == b.Value;
        public static bool NotEqual(LegacyExtended a, LegacyExtended b) => a.Value != b.Value;
        public static bool Greater(LegacyExtended a, LegacyExtended b) => a.Value > b.Value;
        public static bool Less(LegacyExtended a, LegacyExtended b) => a.Value < b.Value;
        public static bool GreaterEqual(LegacyExtended a, LegacyExtended b) => a.Value >= b.Value;
        public static bool LessEqual(LegacyExtended a, LegacyExtended b) => a.Value <= b.Value;
        public static bool GreaterUn(LegacyExtended a, LegacyExtended b) => Unordered(a, b) || Greater(a, b);
        public static bool LessUn(LegacyExtended a, LegacyExtended b) => Unordered(a, b) || Less(a, b);
        public static bool GreaterEqualUn(LegacyExtended a, LegacyExtended b) => Unordered(a, b) || GreaterEqual(a, b);
        public static bool LessEqualUn(LegacyExtended a, LegacyExtended b) => Unordered(a, b) || LessEqual(a, b);
        public static LegacyExtended Vector2Length(ref Vector2 value) => FromDouble(Math.Sqrt(value.X * value.X + value.Y * value.Y));
        public static void Vector2Normalize(ref Vector2 value) { var length = (float)ToDouble(Vector2Length(ref value)); value.X /= length; value.Y /= length; }
        public static Vector2 Vector2Add(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector2 Vector2Subtract(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector2 Vector2Multiply(Vector2 a, float b) => new(a.X * b, a.Y * b);
        public static Vector2 Vector2Scale(float a, Vector2 b) => Vector2Multiply(b, a);
        public static Vector2 Vector2Lerp(Vector2 a, Vector2 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }
}
namespace SlimDX
{
    public struct Vector2(float x, float y)
    {
        public float X = x, Y = y;
        public static Vector2 Zero => default;
        public float Length() => MathF.Sqrt(X * X + Y * Y);
        public void Normalize() { var length = Length(); X /= length; Y /= length; }
        public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
        public static Vector2 operator *(Vector2 a, float b) => new(a.X * b, a.Y * b);
        public static Vector2 operator *(float a, Vector2 b) => b * a;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }
}
