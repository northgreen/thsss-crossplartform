using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;

CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
if (args.Length == 2 && args[0] == "generate")
{
    Cases.Generate(args[1]);
    return;
}
if (args.Length != 4) throw new ArgumentException("Expected gameDll output inputs precision(24|53), or generate inputs");
string gamePath = Path.GetFullPath(args[0]);
int precision = int.Parse(args[3], CultureInfo.InvariantCulture);
if (precision is not (24 or 53)) throw new ArgumentOutOfRangeException(nameof(precision));
CollisionCase[] cases = Cases.Read(args[2]);
var probe = new GameProbe(gamePath, precision);
using (var writer = new StreamWriter(new FileStream(args[1], FileMode.CreateNew), new UTF8Encoding(false)))
{
    writer.WriteLine($"collision-v1 precision={precision} count={cases.Length} nan=raw-bits");
    foreach (CollisionCase c in cases) writer.WriteLine(probe.Run(c));
}
Console.WriteLine($"cases={cases.Length} precision={precision} backend={probe.Backend} gameSHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(gamePath)))} outputSHA256={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[1])))}");

internal sealed class GameProbe
{
    public string Backend { get; }
    private readonly Type baseType;
    private readonly PropertyInfo position;
    private readonly PropertyInfo time;
    private readonly MethodInfo distancePoint;
    private readonly MethodInfo distanceObject;
    private readonly MethodInfo hitOne;
    private readonly MethodInfo hitTwo;
    private readonly Dictionary<string, FieldInfo> fields;
    private readonly Type[] receiverTypes;

    public GameProbe(string gamePath, int precision)
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
        Backend = (string)math.GetProperty("Backend")!.GetValue(null)!;
        string? requested = Environment.GetEnvironmentVariable("THSSS_MATH_BACKEND");
        if (requested is "software" or "x87" && requested != Backend)
            throw new InvalidOperationException($"Requested {requested} backend but loaded {Backend}");
        baseType = game.GetType("Shooting.BaseObject", true)!;
        position = baseType.GetProperty("OriginalPosition")!;
        time = baseType.GetProperty("Time")!;
        distancePoint = RequireMethod("GetDistance", typeof(PointF));
        distanceObject = RequireMethod("GetDistance", baseType);
        hitOne = RequireMethod("HitCheck", baseType);
        hitTwo = RequireMethod("HitCheck", baseType, typeof(float));
        if (distancePoint.ReturnType != typeof(double) || distanceObject.ReturnType != typeof(double) ||
            hitOne.ReturnType != typeof(bool) || hitTwo.ReturnType != typeof(bool) || !hitOne.IsVirtual || !hitTwo.IsVirtual)
            throw new MissingMethodException("Public collision signatures changed");
        string[] names = { "scaleLength", "scaleWidth", "region", "angle", "direction", "angleWithDirection" };
        fields = names.ToDictionary(name => name, name => baseType.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(baseType.FullName, name));
        receiverTypes = new[] { baseType, CreateOverride(hitTwo, true, "OverrideTwo"), CreateOverride(hitOne, false, "OverrideOne") };
    }

    public string Run(CollisionCase c)
    {
        if ((uint)c.Dispatch >= receiverTypes.Length) throw new InvalidDataException("Invalid receiver dispatch mode");
        object own = Activator.CreateInstance(receiverTypes[c.Dispatch])!;
        object target = Activator.CreateInstance(baseType)!;
        position.SetValue(own, new PointF(c.X, c.Y));
        position.SetValue(target, new PointF(c.TargetX, c.TargetY));
        time.SetValue(own, c.Time);
        Set(own, "scaleLength", c.Length); Set(own, "scaleWidth", c.Width); Set(own, "region", c.Region);
        Set(own, "angle", c.Angle); Set(own, "direction", c.Direction); Set(own, "angleWithDirection", c.AngleWithDirection);
        Set(target, "scaleLength", c.TargetScale); Set(target, "scaleWidth", c.TargetScale); Set(target, "region", c.TargetRegion);
        object? targetArgument = c.NullTarget ? null : target;
        string dp = Invoke(distancePoint, own, new PointF(c.TargetX, c.TargetY));
        string dob = Invoke(distanceObject, own, targetArgument);
        string h1 = Invoke(hitOne, own, targetArgument);
        string h2 = Invoke(hitTwo, own, targetArgument, c.Radius);
        int count = c.Dispatch == 0 ? 0 : (int)own.GetType().GetField("Calls")!.GetValue(own)!;
        CheckControlCases(c, h1, h2, dob, count);
        return $"{c.Name}\tdp={dp}\tdo={dob}\th1={h1}\th2={h2}\tcalls={count}";
    }

    private static void CheckControlCases(CollisionCase c, string h1, string h2, string distance, int count)
    {
        if (c.NullTarget && distance != "error:System.NullReferenceException")
            throw new InvalidOperationException($"{c.Name}: missing typed distance null error");
        if (c.Dispatch == 0 && c.Time < 9 && (h1 != "false" || h2 != "false"))
            throw new InvalidOperationException($"{c.Name}: early guard no longer precedes target access");
        if (c.Dispatch == 0 && c.Time >= 9 && c.NullTarget &&
            (h1 != "error:System.NullReferenceException" || h2 != "error:System.NullReferenceException"))
            throw new InvalidOperationException($"{c.Name}: expected typed hit null error");
        if (c.Dispatch == 1 && (h1 != "true" || h2 != "true" || count != 2))
            throw new InvalidOperationException($"{c.Name}: one-argument overload no longer dispatches virtually");
        if (c.Dispatch == 2 && (h1 != "false" || count != 1))
            throw new InvalidOperationException($"{c.Name}: one-argument override was bypassed");
    }

    private static string Invoke(MethodInfo method, object receiver, params object?[] arguments)
    {
        try
        {
            object result = method.Invoke(receiver, arguments)!;
            return result is double d ? $"{unchecked((ulong)BitConverter.DoubleToInt64Bits(d)):X16}" : (bool)result ? "true" : "false";
        }
        catch (TargetInvocationException error) when (error.InnerException is NullReferenceException)
        {
            return "error:" + error.InnerException.GetType().FullName;
        }
    }

    private MethodInfo RequireMethod(string name, params Type[] parameters) => baseType.GetMethod(name, parameters)
        ?? throw new MissingMethodException(baseType.FullName, name);
    private void Set(object receiver, string name, object value) => fields[name].SetValue(receiver, value);

    private Type CreateOverride(MethodInfo method, bool result, string name)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(name), AssemblyBuilderAccess.Run);
        var builder = assembly.DefineDynamicModule(name).DefineType(name, TypeAttributes.Public, baseType);
        var calls = builder.DefineField("Calls", typeof(int), FieldAttributes.Public);
        builder.DefineDefaultConstructor(MethodAttributes.Public);
        var implementation = builder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.Virtual |
            MethodAttributes.HideBySig, typeof(bool), method.GetParameters().Select(p => p.ParameterType).ToArray());
        ILGenerator il = implementation.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldfld, calls);
        il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stfld, calls);
        il.Emit(result ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret);
        builder.DefineMethodOverride(implementation, method);
        return builder.CreateType()!;
    }
}
