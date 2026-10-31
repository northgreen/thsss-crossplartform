using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace MathModeCompatibility;

internal static class RuntimeProbe
{
    public static void Run(string path, string expected)
    {
        Program.Require(expected is "software" or "x87" or "off" or "ArgumentException", "Invalid expected backend/error");
        string directory = Path.GetDirectoryName(path)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        var game = AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
        NativeLibrary.SetDllImportResolver(game, (name, _, _) =>
        {
            if (name != "thsss_legacy_math") return 0;
            Program.Require(expected != "off", "Off unexpectedly requested native math");
            string file = OperatingSystem.IsWindows() ? "thsss_legacy_math.dll" :
                OperatingSystem.IsMacOS() ? "libthsss_legacy_math.dylib" : "libthsss_legacy_math.so";
            return NativeLibrary.Load(Path.Combine(directory, file));
        });
        if (expected == "off")
        {
            Program.Require(game.GetType("SlimDX.Compat.LegacyFloat") == null && game.GetType("SlimDX.Compat.LegacyExtended") == null, "Off runtime still contains legacy types");
            RandomParity(game); GeometryParity(game); return;
        }
        var math = game.GetType("SlimDX.Compat.LegacyFloat", true)!;
        try
        {
            string backend = (string)math.GetProperty("Backend")!.GetValue(null)!;
            Program.Require(expected != "ArgumentException", "Invalid backend silently accepted/fell back to " + backend);
            Program.Require(backend == expected, "Unexpected backend: " + backend);
            math.GetMethod("InitializeGameplayThread")!.Invoke(null, null);
            Program.Require((int)math.GetProperty("Precision")!.GetValue(null)! == 24, "Gameplay precision must initialize to 24");
        }
        catch (Exception error) when (expected == "ArgumentException")
        {
            bool initializationError = false;
            while (error is TargetInvocationException or TypeInitializationException)
            {
                initializationError |= error is TypeInitializationException;
                error = error.InnerException ?? throw new InvalidDataException("Missing initialization cause");
            }
            Program.Require(initializationError && error is ArgumentException, "Expected TypeInitializationException wrapping ArgumentException, got " + error);
        }
    }

    static void RandomParity(Assembly game)
    {
        var type = game.GetType("Shooting.MyRandom", true)!;
        foreach (int seed in new[] { 0, 1, -1, int.MinValue, int.MaxValue })
        {
            var actual = (Random)Activator.CreateInstance(type, seed)!;
            var reference = new OrdinaryRandom(seed);
            void State() => Equal(type.GetProperty("ranValue")!.GetValue(actual)!, reference.ranValue, "ranValue");
            for (int i = 0; i < 10; i++)
            {
                Equal(actual.Next(), reference.Next(), "Next");
                State();
                Equal(actual.Next(19), reference.Next(19), "Next(max)");
                State();
                Equal(actual.Next(int.MinValue, int.MaxValue), reference.Next(int.MinValue, int.MaxValue), "Next(range)");
                State();
                Equal(actual.NextDouble(), reference.NextDouble(), "Random.NextDouble");
                State();
                Equal(type.GetMethod("NextDouble")!.Invoke(actual, null)!, reference.NextDouble(), "MyRandom.NextDouble");
                State();
                Equal(type.GetMethod("NextPMDouble")!.Invoke(actual, null)!, reference.NextPMDouble(), "NextPMDouble");
                Equal(type.GetProperty("ranValue")!.GetValue(actual)!, reference.ranValue, "ranValue after PM");
                byte[] a = new byte[17], b = new byte[17];
                actual.NextBytes(a); reference.NextBytes(b);
                Program.Require(a.SequenceEqual(b), "NextBytes mismatch");
                Equal(type.GetProperty("ranValue")!.GetValue(actual)!, reference.ranValue, "ranValue after bytes");
            }
            Program.Require(Cause(() => actual.Next(-1)) == Cause(() => reference.Next(-1)), "Next negative exception mismatch");
            Program.Require(Cause(() => actual.Next(2, 1)) == Cause(() => reference.Next(2, 1)), "Next invalid range exception mismatch");
            Program.Require(Cause(() => actual.NextBytes((byte[])null!)) == Cause(() => reference.NextBytes((byte[])null!)), "NextBytes null exception mismatch");
        }
    }

    static void GeometryParity(Assembly game)
    {
        var type = game.GetType("Shooting.BaseObject", true)!;
        var stageType = game.GetType("Shooting.StageDataPackage", true)!;
        var stage = RuntimeHelpers.GetUninitializedObject(stageType);
        var bounds = new Rectangle(10000003, -29, 640, 480);
        stageType.GetField("BoundRect")!.SetValue(stage, bounds);
        object instance = Activator.CreateInstance(type)!;
        void Set(string name, object value) => type.GetProperty(name)!.SetValue(instance, value);
        void Field(string name, object value) => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
        Set("StageData", stage);
        var distance = type.GetMethod("GetDistance", new[] { typeof(PointF) })!;
        for (int i = 0; i < 10; i++)
        {
            var origin = new PointF(123.4567f + i * 0.17f, -987.6543f - i * 1.13f);
            var target = new PointF(-0.001f * i, 100.123f);
            Set("OriginalPosition", origin);
            double expectedDistance = Math.Sqrt((origin.X - target.X) * (origin.X - target.X) + (origin.Y - target.Y) * (origin.Y - target.Y));
            Equal(distance.Invoke(instance, new object[] { target })!, expectedDistance, "GetDistance.PointF");
            float velocity = 7.123456f + i, sx = 0.9876543f, sy = -1.234567f;
            double direction = 0.123456789012345 + i;
            Field("velocity", velocity); Field("direction", direction); Field("scaleX", sx); Field("scaleY", sy);
            PointF position = new(origin.X + (float)bounds.Left, origin.Y + (float)bounds.Top);
            PointF moved = new((float)((double)position.X + (double)velocity * Math.Cos(direction) * (double)sx),
                (float)((double)position.Y + (double)velocity * Math.Sin(direction) * (double)sy));
            PointF expectedOrigin = new(moved.X - (float)bounds.Left, moved.Y - (float)bounds.Top);
            type.GetMethod("Move", Type.EmptyTypes)!.Invoke(instance, null);
            Equal(type.GetProperty("OriginalPosition")!.GetValue(instance)!, expectedOrigin, "Move BoundRect stores");
        }
        Set("StageData", null!);
        Program.Require(Cause(() => type.GetMethod("Move", Type.EmptyTypes)!.Invoke(instance, null)) == typeof(NullReferenceException), "Off null stage must preserve original error");
    }

    static Type? Cause(Action action)
    {
        try { action(); return null; }
        catch (Exception error)
        {
            while (error is TargetInvocationException or TypeInitializationException && error.InnerException != null) error = error.InnerException;
            return error.GetType();
        }
    }

    static void Equal(object actual, object expected, string label)
    {
        bool equal = (actual, expected) switch
        {
            (double a, double b) => BitConverter.DoubleToInt64Bits(a) == BitConverter.DoubleToInt64Bits(b),
            (PointF a, PointF b) => BitConverter.SingleToInt32Bits(a.X) == BitConverter.SingleToInt32Bits(b.X) && BitConverter.SingleToInt32Bits(a.Y) == BitConverter.SingleToInt32Bits(b.Y),
            _ => actual.Equals(expected)
        };
        Program.Require(equal, $"{label}: actual={actual}, expected={expected}");
    }

    // Independent ordinary-.NET prototype of reference/decompiled/Shooting/MyRandom.cs.
    sealed class OrdinaryRandom(int seed) : Random(seed)
    {
        public int ranValue { get; private set; }
        public override int Next() => ranValue = base.Next();
        public override int Next(int maxValue) => ranValue = base.Next(maxValue);
        public override int Next(int minValue, int maxValue) => ranValue = base.Next(minValue, maxValue);
        public override double NextDouble()
        {
            double value = base.NextDouble(); ranValue = (int)(value * 100.0); return value;
        }
        public override void NextBytes(byte[] buffer) { ranValue = 0; base.NextBytes(buffer); }
        public double NextPMDouble() => 2.0 * NextDouble() - 1.0;
    }
}
