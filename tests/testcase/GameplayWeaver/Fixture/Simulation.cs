using SlimDX;

namespace Shooting;

public class BaseObject
{
    public float Velocity { get; set; } = 3;
    public double Direction { get; set; } = .4;
    public float Vx { get => Velocity * (float)Math.Cos(Direction); set { float vy = Vy; Velocity = (float)Math.Sqrt(value * value + vy * vy); Direction = Velocity == 0 ? 0 : Math.Atan2(vy, value); } }
    public float Vy { get => Velocity * (float)Math.Sin(Direction); set { float vx = Vx; Velocity = (float)Math.Sqrt(value * value + vx * vx); Direction = Velocity == 0 ? 0 : Math.Atan2(value, vx); } }
    public static float ArrayStorage(float[] array, ref float value, float add) { array[0] += add; value += array[0]; return value; }
    public static void Replace(ref float value) => value = 17f;
    public static float AddressLocal() { float value = 2; ref float alias = ref value; Replace(ref alias); return value + alias; }
    public static T Identity<T>(T value) => value;
    public static float Generic() => Identity(2f) + 4f;
    public static float GenericField() { var box = new FloatBox<float> { Value = 2 }; box.Value += 3; return box.Value; }
    public static ref T Reference<T>(ref T value) => ref value;
    public static float GenericReference() { float value = 7; return Reference(ref value) + 1; }
    public static float GenericContainers() { var values = new List<float> { 2, 4 }; float result = 0; foreach (var value in values) result += value; return result; }
    public static float Boxed() { object value = Identity<object>(2f); return (float)value + 1; }
    public static double Handler(bool fail)
    {
        double value = 1;
        try { if (fail) throw new InvalidOperationException(); value += 2; }
        catch (InvalidOperationException) { value += 3; }
        finally { value *= 2; }
        return value;
    }
    public static float Vectors()
    {
        var value = new Vector2(3, 4); var length = value.Length(); value.Normalize();
        var sum = value + new Vector2(1, 2); var diff = sum - value;
        return Vector2.Lerp(diff * 2f, 3f * diff, .5f).X + length;
    }
    public static bool Compare(double a, double b, int op) => op switch { 0 => a == b, 1 => a != b, 2 => a < b, 3 => a > b, 4 => a <= b, _ => a >= b };
    public static byte Byte(double value) => unchecked((byte)value);
    public static double Callback() { double value = .25; Func<double> callback = () => Math.Sin(value); return callback(); }
    public static double Show() { double value = .5; Func<double> callback = () => Math.Sin(value); return callback(); }
    public static double Draw() => Math.Pow(2, 3);
}
public class VisualObject : BaseObject
{
    public static double Ctrl(Random random) => random.NextDouble() + random.NextDouble();
    public static double Render() => Math.Pow(3, 2);
}
public class BaseStage { public static double Ctrl(double value) => Math.Cos(value); }
public class DerivedStage : BaseStage { public static double Move(double value) => Math.Sin(value); }
public static class EventGroup { public static double Twopointangle(double value) => Math.Atan(value); }
public static class WindowInputBridge { public static int Axis(float value) => (int)(Math.Clamp(value, -1, 1) * 1000); }
public class MyRandom : Random { public double NextPMDouble() => NextDouble() * 2 - 1; }
public class FloatBox<T> { public T Value = default!; }
