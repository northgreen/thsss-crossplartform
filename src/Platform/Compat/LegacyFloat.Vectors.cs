namespace SlimDX.Compat;

public static partial class LegacyFloat
{
    // Gameplay calls are redirected here; the rendering-facing SlimDX types are left unchanged.
    public static LegacyExtended Vector2Length(ref Vector2 value)
    {
        var x = FromSingle(value.X);
        var y = FromSingle(value.Y);
        return NarrowSingle(Sqrt(Add(Mul(x, x), Mul(y, y))));
    }

    public static void Vector2Normalize(ref Vector2 value)
    {
        var length = Vector2Length(ref value);
        if (Equal(length, FromInt32(0))) return;
        // Original SlimDX rounds the reciprocal before multiplying; division per component differs.
        var reciprocal = NarrowSingle(Div(FromInt32(1), length));
        value.X = ToSingle(Mul(FromSingle(value.X), reciprocal));
        value.Y = ToSingle(Mul(FromSingle(value.Y), reciprocal));
    }

    public static Vector2 Vector2Add(Vector2 a, Vector2 b) => new(
        ToSingle(Add(FromSingle(a.X), FromSingle(b.X))),
        ToSingle(Add(FromSingle(a.Y), FromSingle(b.Y))));

    public static Vector2 Vector2Subtract(Vector2 a, Vector2 b) => new(
        ToSingle(Sub(FromSingle(a.X), FromSingle(b.X))),
        ToSingle(Sub(FromSingle(a.Y), FromSingle(b.Y))));

    public static Vector2 Vector2Multiply(Vector2 value, float scale) => new(
        ToSingle(Mul(FromSingle(value.X), FromSingle(scale))),
        ToSingle(Mul(FromSingle(value.Y), FromSingle(scale))));

    public static Vector2 Vector2Scale(float scale, Vector2 value) => Vector2Multiply(value, scale);

    public static Vector2 Vector2Lerp(Vector2 start, Vector2 end, float amount)
    {
        var t = FromSingle(amount);
        return new Vector2(
            ToSingle(Add(Mul(Sub(FromSingle(end.X), FromSingle(start.X)), t), FromSingle(start.X))),
            ToSingle(Add(Mul(Sub(FromSingle(end.Y), FromSingle(start.Y)), t), FromSingle(start.Y))));
    }
}
