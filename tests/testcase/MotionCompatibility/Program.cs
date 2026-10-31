using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 2 && args[0] == "generate") { Cases.Generate(args[1]); return; }
if (args.Length != 4) throw new ArgumentException("Expected gameDll output inputs precision(24|53), or generate inputs");
int precision = int.Parse(args[3], CultureInfo.InvariantCulture);
if (precision is not (24 or 53)) throw new ArgumentOutOfRangeException(nameof(precision));
var cases = Cases.Read(args[2]);
var probe = new MotionProbe(Path.GetFullPath(args[0]), precision);
probe.Run(new("warmup"));
using var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew), new UTF8Encoding(false));
writer.WriteLine($"motion-v1 precision={precision} count={cases.Length} nan=raw-bits runtime=initialized");
foreach (var c in cases) writer.WriteLine(probe.Run(c));

internal sealed class MotionProbe
{
    private readonly Type baseType, stageType, overrideType;
    private readonly MethodInfo move;
    private readonly Dictionary<string, PropertyInfo> properties;
    private readonly Dictionary<string, FieldInfo> fields;
    private readonly FieldInfo bounds;

    public MotionProbe(string gamePath, int precision)
    {
        string directory = Path.GetDirectoryName(gamePath)!;
        AssemblyLoadContext.Default.Resolving += (context, name) =>
        {
            string candidate = Path.Combine(directory, name.Name + ".dll");
            return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
        };
        Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(gamePath);
        NativeLibrary.SetDllImportResolver(game, (name, _, _) =>
        {
            if (name != "thsss_legacy_math") return 0;
            string file = OperatingSystem.IsWindows() ? "thsss_legacy_math.dll" :
                OperatingSystem.IsMacOS() ? "libthsss_legacy_math.dylib" : "libthsss_legacy_math.so";
            return NativeLibrary.Load(Path.Combine(directory, file));
        });
        Type math = game.GetType("SlimDX.Compat.LegacyFloat", true)!;
        math.GetProperty("Precision")!.SetValue(null, precision);
        string backend = (string)math.GetProperty("Backend")!.GetValue(null)!;
        if (Environment.GetEnvironmentVariable("THSSS_MATH_BACKEND") != backend)
            throw new InvalidOperationException($"Unexpected backend {backend}");
        baseType = game.GetType("Shooting.BaseObject", true)!;
        stageType = game.GetType("Shooting.StageDataPackage", true)!;
        bounds = stageType.GetField("BoundRect") ?? throw new MissingFieldException("StageDataPackage.BoundRect");
        move = baseType.GetMethod("Move", Type.EmptyTypes) ?? throw new MissingMethodException("BaseObject.Move");
        if (!move.IsVirtual || move.ReturnType != typeof(void)) throw new MissingMethodException("Move must be public virtual void");
        properties = new[] { "StageData", "OriginalPosition", "Position", "DestPoint", "Velocity", "Direction", "Accelerate", "Time" }
            .ToDictionary(n => n, n => baseType.GetProperty(n) ?? throw new MissingMemberException(n));
        fields = new[] { "velocity", "direction", "scaleX", "scaleY" }.ToDictionary(n => n,
            n => baseType.GetField(n, BindingFlags.NonPublic | BindingFlags.Instance) ?? throw new MissingFieldException(n));
        overrideType = CreateOverride();
    }

    private object Create(MotionCase c)
    {
        object receiver = Activator.CreateInstance(c.Override ? overrideType : baseType)!;
        if (!c.NullStage)
        {
            object stage = RuntimeHelpers.GetUninitializedObject(stageType);
            bounds.SetValue(stage, new Rectangle(c.Left, c.Top, 640, 480));
            properties["StageData"].SetValue(receiver, stage);
        }
        properties["OriginalPosition"].SetValue(receiver, new PointF(Cases.Float(c.X), Cases.Float(c.Y)));
        properties["DestPoint"].SetValue(receiver, new PointF(Cases.Float(c.DestX), Cases.Float(c.DestY)));
        properties["Accelerate"].SetValue(receiver, Cases.Float(c.Accelerate));
        properties["Time"].SetValue(receiver, c.Time);
        fields["velocity"].SetValue(receiver, Cases.Float(c.Velocity));
        fields["direction"].SetValue(receiver, Cases.Double(c.Direction));
        fields["scaleX"].SetValue(receiver, Cases.Float(c.ScaleX));
        fields["scaleY"].SetValue(receiver, Cases.Float(c.ScaleY));
        return receiver;
    }

    public string Run(MotionCase c)
    {
        object getter = Create(c), setter = Create(c), receiver = Create(c);
        string get = Invoke(() => properties["Position"].GetValue(getter));
        string set = Invoke(() => { properties["Position"].SetValue(setter, new PointF(Cases.Float(c.X), Cases.Float(c.Y))); return null; });
        string setOrigin = Format(properties["OriginalPosition"].GetValue(setter));
        string moved = Invoke(() => { move.Invoke(receiver, null); return null; });
        int calls = c.Override ? (int)overrideType.GetField("Calls")!.GetValue(receiver)! : 0;
        if (c.Override && (calls != 1 || moved != "ok")) throw new InvalidOperationException("Virtual Move dispatch bypassed");
        if (c.NullStage && (get != "error:System.NullReferenceException" || set != get ||
            (!c.Override && moved != get))) throw new InvalidOperationException("Initialized null StageData error contract changed");
        return $"{c.Name}\tg={get}\ts={set}\tso={setOrigin}\tmove={moved}" +
            $"\to={Format(properties["OriginalPosition"].GetValue(receiver))}" +
            $"\tp={Invoke(() => properties["Position"].GetValue(receiver))}" +
            $"\tv={Format(properties["Velocity"].GetValue(receiver))}\td={Format(properties["Direction"].GetValue(receiver))}" +
            $"\ta={Format(properties["Accelerate"].GetValue(receiver))}\tt={properties["Time"].GetValue(receiver)}" +
            $"\tdest={Format(properties["DestPoint"].GetValue(receiver))}\tvirtual=1\tcalls={calls}";
    }

    private static string Invoke(Func<object?> action)
    {
        try { return Format(action()); }
        catch (TargetInvocationException error) when (error.InnerException is NullReferenceException)
        { return "error:" + error.InnerException.GetType().FullName; }
    }

    private static string Format(object? value) => value switch
    {
        null => "ok",
        PointF p => $"{BitConverter.SingleToInt32Bits(p.X):X8},{BitConverter.SingleToInt32Bits(p.Y):X8}",
        float f => $"{BitConverter.SingleToInt32Bits(f):X8}",
        double d => $"{BitConverter.DoubleToInt64Bits(d):X16}",
        _ => throw new InvalidDataException($"Unexpected output type {value.GetType()}")
    };

    private Type CreateOverride()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("MotionOverride"), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule("MotionOverride").DefineType("MotionOverride", TypeAttributes.Public, baseType);
        var calls = builder.DefineField("Calls", typeof(int), FieldAttributes.Public);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var implementation = builder.DefineMethod("Move", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig,
            typeof(void), Type.EmptyTypes);
        var il = implementation.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, calls);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, calls); il.Emit(OpCodes.Ret);
        builder.DefineMethodOverride(implementation, move);
        return builder.CreateType()!;
    }
}
