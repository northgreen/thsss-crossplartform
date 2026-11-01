using System.Collections;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text;

if (args.Length != 6) throw new ArgumentException("Expected DLL, native library, backend, precision, inputs, outputs");
string dll = Path.GetFullPath(args[0]);
string directory = Path.GetDirectoryName(dll)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
    File.Exists(Path.Combine(directory, name.Name + ".dll"))
        ? context.LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : null;
Assembly game = AssemblyLoadContext.Default.LoadFromAssemblyPath(dll);
NativeLibrary.SetDllImportResolver(game, (name, _, _) => name == "thsss_legacy_math"
    ? NativeLibrary.Load(Path.GetFullPath(args[1])) : IntPtr.Zero);
Type legacy = game.GetType("SlimDX.Compat.LegacyFloat", true)!;
int precision = int.Parse(args[3], CultureInfo.InvariantCulture);
if (precision != 24 && precision != 53) throw new ArgumentException("Expected PC24 or PC53");
legacy.GetProperty("Precision")!.SetValue(null, precision);
if ((string)legacy.GetProperty("Backend")!.GetValue(null)! != args[2] ||
    (int)legacy.GetProperty("Precision")!.GetValue(null)! != precision)
    throw new InvalidOperationException("Backend/precision selection differs");
Type csType = game.GetType("Shooting.BaseObject_CS", true)!;
Type baseType = csType.BaseType!;
MethodInfo ctrl = csType.GetMethod("Ctrl", Type.EmptyTypes)!;
if (!ctrl.IsVirtual || ctrl.DeclaringType != csType)
    throw new InvalidOperationException("Expected actual BaseObject_CS.Ctrl virtual method");
foreach (string property in new[] { "AccDirection", "AccelerateCS", "Ax", "Ay" })
    if (csType.GetProperty(property)!.GetMethod!.IsVirtual)
        throw new InvalidOperationException("Expected nonvirtual accessor: " + property);
Type derived = MakeProbe(csType);
FieldInfo[] fields = new[] { baseType, csType }.SelectMany(type => type.GetFields(
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
    .OrderBy(field => field.DeclaringType!.FullName + "." + field.Name, StringComparer.Ordinal).ToArray();
using var input = new BinaryReader(File.OpenRead(args[4]));
using var output = new StreamWriter(new FileStream(args[5], FileMode.CreateNew), new UTF8Encoding(false)) { NewLine = "\n" };
if (input.ReadInt32() != 0x31434341 || input.ReadInt32() != 6000)
    throw new InvalidDataException("Expected ACC1 corpus with 6000 cases");
int disabledWitnesses = 0, zeroInvalidWitnesses = 0, stoppedCases = 0;
for (int index = 0; index < 6000; index++) Run(index);
if (input.BaseStream.Position != input.BaseStream.Length) throw new InvalidDataException("Trailing input bytes");
if (disabledWitnesses == 0 || zeroInvalidWitnesses == 0 || stoppedCases != 32)
    throw new InvalidOperationException("Missing disabled/zero-invalid/prefix-only coverage");
output.WriteLine($"witnesses:disabled={disabledWitnesses};zero-invalid={zeroInvalidWitnesses};prefix-only={stoppedCases}");

object? Get(object instance, string name) => csType.GetProperty(name)!.GetValue(instance);
void Set(object instance, string name, object? value) => csType.GetProperty(name)!.SetValue(instance, value);
void Raw(object instance, string name, object value) => baseType.GetField(name,
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(instance, value);
string State(object instance) => string.Join('|', fields.Select(field =>
    field.DeclaringType!.Name + "." + field.Name + "=" + Bits(field.GetValue(instance)))) +
    "|observed:" + string.Join(';', new[] { "Position", "Vx", "Vy", "AngleDegree", "DirectionDegree" }
        .Select(name => name + "=" + Bits(Get(instance, name))));

void Run(int index)
{
    double accelerationDirection = BitConverter.Int64BitsToDouble(input.ReadInt64());
    float acceleration = BitConverter.Int32BitsToSingle(input.ReadInt32());
    double angle = BitConverter.Int64BitsToDouble(input.ReadInt64());
    double heading = BitConverter.Int64BitsToDouble(input.ReadInt64());
    float angularRate = BitConverter.Int32BitsToSingle(input.ReadInt32());
    float headingRate = BitConverter.Int32BitsToSingle(input.ReadInt32());
    byte flags = input.ReadByte();
    if ((flags & ~3) != 0) throw new InvalidDataException("Unknown corpus flags");
    bool enabled = (flags & 1) == 0, stopAtEvent = (flags & 2) != 0;
    // Never enter the original non-progressing normalization loops with unsafe base inputs.
    if (!stopAtEvent && (UnsafeAngle(angle) || UnsafeAngle(heading) || UnsafeRate(angularRate) || UnsafeRate(headingRate)))
        throw new InvalidDataException("Unsafe full-Ctrl normalization input");
    object instance = Activator.CreateInstance(derived)!;
    // Motion only reads BoundRect; a data-only stage avoids graphics/audio/asset initialization.
    Type stageType = game.GetType("Shooting.StageDataPackage", true)!;
    object stage = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(stageType);
    stageType.GetField("BoundRect")!.SetValue(stage, new Rectangle(17, -23, 640, 480));
    Set(instance, "StageData", stage);
    Set(instance, "AccDirection", accelerationDirection);
    Set(instance, "AccelerateCS", acceleration);
    Set(instance, "Ax", 42f); Set(instance, "Ay", -43f);
    Raw(instance, "angle", angle); Raw(instance, "direction", heading);
    Set(instance, "AngularVelocityDegree", angularRate);
    Set(instance, "DirectionVelocityDegree", headingRate);
    Set(instance, "Enabled", enabled);
    Set(instance, "OriginalPosition", new PointF(index % 19 - 9, index % 23 - 11));
    Set(instance, "LastPoints", new[] { new PointF(17, 19), new PointF(-23, 29), new PointF(31, -37) });
    Set(instance, "MaxVelocity", 100f); Set(instance, "MinVelocity", -100f);
    Set(instance, "Velocity", index % 3 == 0 ? 9f : -9f);
    Set(instance, "Accelerate", index % 3 == 0 ? 0.125f : -0.125f);
    Set(instance, "TransparentVelocity", index % 2 == 0 ? 5f : -5f);
    Set(instance, "ScaleVL", 0.25f); Set(instance, "ScaleVW", -0.25f);
    var events = new List<string>();
    string prefixAx = "", prefixAy = "", eventState = "";
    ProbeHooks.OnHook = (observed, name) =>
    {
        if (!ReferenceEquals(instance, observed)) throw new InvalidOperationException("Wrong callback object");
        if (name == "EventCtrl:enter")
        {
            if (events.Count != 0 || (int)Get(instance, "Time")! != 0)
                throw new InvalidOperationException("Acceleration prefix must precede EventCtrl and base Ctrl");
            prefixAx = Bits(Get(instance, "Ax")); prefixAy = Bits(Get(instance, "Ay"));
            eventState = State(instance);
            events.Add(name + ":" + eventState);
            if (stopAtEvent) throw new PrefixObservedException();
            // A later script write must not retroactively alter the already stored prefix.
            if (index % 7 == 0) Set(instance, "AccelerateCS", -0.25f);
            return;
        }
        int expectedCount = name switch { "EventCtrl:exit" => 1, "Move:enter" => 2, "Move:exit" => 3, _ => -1 };
        if (events.Count != expectedCount || (int)Get(instance, "Time")! != (name.StartsWith("Move:") ? 1 : 0))
            throw new InvalidOperationException("Original virtual-call ordering changed: " + name);
        if (Bits(Get(instance, "Ax")) != prefixAx || Bits(Get(instance, "Ay")) != prefixAy)
            throw new InvalidOperationException("Acceleration was rewritten after EventCtrl entry");
        events.Add(name + ":" + State(instance));
    };
    Action<string> callback = argument =>
    {
        if (argument != null || events.Count != 4 || (int)Get(instance, "Time")! != 1)
            throw new InvalidOperationException("CtrlCall must follow real Move and base updates");
        events.Add("CtrlCall:" + State(instance));
    };
    FieldInfo call = baseType.GetField("CtrlCall")!;
    call.SetValue(instance, Delegate.CreateDelegate(call.FieldType, callback.Target, callback.Method));
    string before = State(instance);
    bool stopped = false;
    try { ctrl.Invoke(instance, null); }
    catch (TargetInvocationException error) when (error.InnerException is PrefixObservedException)
    {
        if (!stopAtEvent) throw;
        stopped = true;
    }
    if (stopped != stopAtEvent || events.Count != (stopAtEvent ? 1 : enabled ? 5 : 2) ||
        (int)Get(instance, "Time")! != (stopAtEvent || !enabled ? 0 : 1))
        throw new InvalidOperationException("Unexpected event count, stop boundary or Enabled guard");
    if ((!enabled || stopped) && (prefixAx != Bits(42f) || prefixAy != Bits(-43f))) disabledWitnesses++;
    if (acceleration == 0 && (float.IsNaN((float)Get(instance, "Ax")!) || float.IsNaN((float)Get(instance, "Ay")!)))
        zeroInvalidWitnesses++;
    if (stopped) stoppedCases++;
    if ((!enabled || stopped) && Get(instance, "OriginalPosition") is PointF position &&
        position != new PointF(index % 19 - 9, index % 23 - 11))
        throw new InvalidOperationException("Disabled/prefix-only case moved");
    output.WriteLine($"{index}\tbefore:{before}\t{string.Join('\t', events)}\tafter:{State(instance)}\tstopped:{stopped}");
}

static bool UnsafeAngle(double value) => double.IsInfinity(value) || (double.IsFinite(value) && Math.Abs(value) > 7);
static bool UnsafeRate(float value) => float.IsInfinity(value) || (float.IsFinite(value) && Math.Abs(value) > 7200);

static Type MakeProbe(Type csType)
{
    var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("AccelerationVirtualProbe"), AssemblyBuilderAccess.Run).DefineDynamicModule("Main");
    var type = module.DefineType("AccelerationProbe", TypeAttributes.Public, csType);
    foreach (string name in new[] { "EventCtrl", "Move" })
    {
        MethodInfo original = csType.GetMethod(name, Type.EmptyTypes)!;
        if (!original.IsVirtual || original.IsFinal) throw new InvalidOperationException("Expected overridable " + name);
        var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig, typeof(void), Type.EmptyTypes);
        var il = method.GetILGenerator();
        EmitHook(il, name + ":enter");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, original); // Execute the original EventCtrl/Move, not a replacement formula.
        EmitHook(il, name + ":exit");
        il.Emit(OpCodes.Ret);
        type.DefineMethodOverride(method, original);
    }
    return type.CreateType()!;
}

static void EmitHook(ILGenerator il, string name)
{
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Ldstr, name);
    il.Emit(OpCodes.Call, typeof(ProbeHooks).GetMethod(nameof(ProbeHooks.Hook))!);
}

static string Bits(object? value)
{
    if (value == null) return "null";
    if (value is float f) return BitConverter.SingleToInt32Bits(f).ToString("X8");
    if (value is double d) return BitConverter.DoubleToInt64Bits(d).ToString("X16");
    if (value is string s) return "string:" + s;
    if (value is bool b) return b ? "true" : "false";
    if (value is Delegate callback) return "delegate:" + callback.GetType().FullName + ":" + callback.GetInvocationList().Length;
    if (value is IDictionary dictionary)
        return "{" + string.Join(';', dictionary.Keys.Cast<object>().OrderBy(k => Convert.ToInt32(k, CultureInfo.InvariantCulture)).Select(k => Bits(k) + "=" + Bits(dictionary[k]))) + "}";
    if (value is Array array) return "[" + string.Join(';', array.Cast<object?>().Select(Bits)) + "]";
    Type type = value.GetType();
    if (type.IsPrimitive || type.IsEnum) return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    if (type.IsValueType || type.FullName == "Shooting.StageDataPackage")
        return type.FullName + "{" + string.Join(';', type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => f.Name + "=" + Bits(f.GetValue(value)))) + "}";
    throw new InvalidOperationException("Uncaptured reference state: " + type.FullName);
}

public sealed class PrefixObservedException : Exception { }
public static class ProbeHooks
{
    public static Action<object, string> OnHook = (_, _) => throw new InvalidOperationException("No active case");
    public static void Hook(object instance, string name) => OnHook(instance, name);
}
