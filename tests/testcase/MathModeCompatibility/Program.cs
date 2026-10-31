using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MathModeCompatibility;

internal static class Program
{
    public static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }

    public static int Main(string[] args)
    {
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            if (args is ["--self-test"]) { Audit.SelfTest(); Console.WriteLine("PASS auditor self-test"); return 0; }
            Require(args.Length is 3 or 4, "Usage: inspect DLL auto|software|off [normal-off-reference.dll] OR probe DLL software|x87|off|ArgumentException");
            string path = Path.GetFullPath(args[1]);
            Require(File.Exists(path), "Missing assembly: " + path);
            if (args[0] == "inspect") Audit.Run(path, args[2], args.Length == 4 ? args[3] : null);
            else if (args[0] == "probe" && args.Length == 3) RuntimeProbe.Run(path, args[2]);
            else throw new ArgumentException("Unknown command or argument count");
            Console.WriteLine("PASS " + string.Join(' ', args));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}

internal static class Audit
{
    const string Legacy = "SlimDX.Compat.Legacy";
    static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) =>
        types.SelectMany(t => new[] { t }.Concat(Types(t.NestedTypes)));
    static bool LegacyReference(string text) => text.Contains(Legacy, StringComparison.Ordinal);
    static bool Shadow(string text) => text.StartsWith("__LegacyExtended_", StringComparison.Ordinal) ||
        text.Contains("$legacy", StringComparison.OrdinalIgnoreCase) || text.Contains("$extension", StringComparison.OrdinalIgnoreCase);
    static IEnumerable<MethodReference> Calls(MethodDefinition method) => method.HasBody ?
        method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>() : [];
    static MethodDefinition Method(TypeDefinition type, string name, int count) =>
        type.Methods.Single(m => m.Name == name && m.Parameters.Count == count);
    static void Call(MethodDefinition method, string type, string name) => Program.Require(
        Calls(method).Any(c => c.DeclaringType.FullName == type && c.Name == name), $"{method.FullName} must call {type}.{name}");

    public static void SelfTest()
    {
        using var module = ModuleDefinition.CreateModule("Synthetic", ModuleKind.Dll);
        var type = new TypeDefinition("Fixture", "Plain", TypeAttributes.Public, module.TypeSystem.Object);
        module.Types.Add(type);
        CheckOffReferences(module, new[] { type });
        void Reject(Action add, Action remove)
        {
            add();
            bool rejected = false;
            try { CheckOffReferences(module, new[] { type }); }
            catch (InvalidDataException) { rejected = true; }
            remove();
            Program.Require(rejected, "Auditor accepted deliberately contaminated IL");
        }
        var field = new FieldDefinition("state", FieldAttributes.Static, new TypeReference("SlimDX.Compat", "LegacyExtended", module, module));
        Reject(() => type.Fields.Add(field), () => type.Fields.Remove(field));
        var shadow = new MethodDefinition("__LegacyExtended_get_Vx", MethodAttributes.Public, module.TypeSystem.Void);
        Reject(() => type.Methods.Add(shadow), () => type.Methods.Remove(shadow));
        var method = new MethodDefinition("Run", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.Void);
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "SlimDX.Compat.LegacyFloat"));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Reject(() => type.Methods.Add(method), () => type.Methods.Remove(method));
        CheckOffReferences(module, new[] { type });
    }

    public static void Run(string path, string mode, string? reference)
    {
        Program.Require(mode is "auto" or "software" or "off", "Invalid expected mode");
        using var module = ModuleDefinition.ReadModule(path);
        var modes = module.Assembly.CustomAttributes.Where(a => a.AttributeType.FullName == "System.Reflection.AssemblyMetadataAttribute" &&
            (string)a.ConstructorArguments[0].Value == "THSSSMathMode").ToArray();
        Program.Require(modes.Length == 1 && (string)modes[0].ConstructorArguments[1].Value == mode, "Compiled THSSSMathMode metadata mismatch");
        string directory = Path.GetDirectoryName(path)!;
        using var record = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "THSSS.math-mode.json")));
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Program.Require(record.RootElement.GetProperty("mode").GetString() == mode &&
            string.Equals(record.RootElement.GetProperty("sha256").GetString(), hash, StringComparison.OrdinalIgnoreCase), "Final DLL SHA256/build record mismatch");
        var types = Types(module.Types).ToArray();
        var baseType = types.Single(t => t.FullName == "Shooting.BaseObject");
        var random = types.Single(t => t.FullName == "Shooting.MyRandom");
        Program.Require(random.BaseType.FullName == "System.Random", "MyRandom must inherit Random");
        bool enabled = mode != "off";
        Program.Require(types.Any(t => t.FullName == Legacy + "Float") == enabled &&
            types.Any(t => t.FullName == Legacy + "Extended") == enabled, "Legacy runtime type presence mismatch");
        if (enabled)
        {
            CheckFiles(directory, true);
            foreach (string helper in new[] { "MotionStep", "Distance", "CircleHit", "EllipseHit" })
                Program.Require(baseType.Methods.SelectMany(Calls).Any(c => c.DeclaringType.FullName == Legacy + "Float" && c.Name == helper), "Missing enabled helper call: " + helper);
            Program.Require(types.SelectMany(t => t.Methods).Any(m => Shadow(m.Name)), "Enabled assembly has no extended shadow getters");
            // Fody writes its audit beside the intermediate assembly, not necessarily the output DLL.
            string manifestPath = Environment.GetEnvironmentVariable("THSSS_TEST_WEAVE_MANIFEST")
                ?? Path.Combine(directory, "GameplayMath.manifest.json");
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Program.Require(manifest.RootElement.GetProperty("Success").GetBoolean() &&
                manifest.RootElement.GetProperty("Methods").EnumerateArray().Any(m => m.GetProperty("Action").GetString() == "rewritten"), "Manifest lacks successful woven methods");
            return;
        }
        Program.Require(!module.AssemblyReferences.Any(a => a.Name.Contains("Fody", StringComparison.OrdinalIgnoreCase)), "Off assembly references Fody");
        CheckOffReferences(module, types);
        Call(Method(baseType, "Move", 0), "System.Math", "Cos");
        Call(Method(baseType, "Move", 0), "System.Math", "Sin");
        Call(Method(baseType, "Move", 0), "Shooting.BaseObject", "set_Position");
        var distance = baseType.Methods.Single(m => m.Name == "GetDistance" && m.Parameters[0].ParameterType.FullName == "System.Drawing.PointF");
        Call(distance, "System.Math", "Sqrt");
        Call(Method(baseType, "HitCheck", 2), "System.Math", "Cos");
        Call(Method(baseType, "HitCheck", 2), "System.Math", "Sin");
        Program.Require(!random.Methods.Any(m => m.Name == "Sample") && random.Fields.All(f => f.Name == "<ranValue>k__BackingField"), "Off RNG owns compatibility kernel state or Sample override");
        if (reference != null) CompareReference(baseType, random, reference);
        CheckFiles(directory, false);
    }

    static void CheckFiles(string directory, bool enabled)
    {
        string[] files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        bool native = files.Any(f => Path.GetFileName(f) is "libthsss_legacy_math.so" or "libthsss_legacy_math.dylib" or "thsss_legacy_math.dll");
        Program.Require(native == enabled, "Native math library presence mismatch");
        if (enabled) return;
        Program.Require(!File.Exists(Path.Combine(directory, "GameplayMath.manifest.json")), "Off output retains a weave manifest");
        Program.Require(!files.Any(f => Path.GetFileName(f).Contains("legacy_math", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(f).Contains("Fody", StringComparison.OrdinalIgnoreCase)), "Off output contains native/PDB/Fody artifact");
        using var deps = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "THSSS.deps.json")));
        Program.Require(!deps.RootElement.GetProperty("libraries").EnumerateObject().Any(p => p.Name.Contains("Fody", StringComparison.OrdinalIgnoreCase)), "Off deps contains Fody package");
    }

    static void CheckOffReferences(ModuleDefinition module, TypeDefinition[] types)
    {
        var signatures = module.GetTypeReferences().Select(t => t.FullName).Concat(module.GetMemberReferences().Select(m => m.FullName))
            .Concat(types.Select(t => t.FullName + " " + t.BaseType?.FullName))
            .Concat(types.SelectMany(t => t.Interfaces).Select(i => i.InterfaceType.FullName))
            .Concat(types.SelectMany(t => t.Fields).Select(f => f.FullName))
            .Concat(types.SelectMany(t => t.Properties).Select(p => p.FullName))
            .Concat(types.SelectMany(t => t.Events).Select(e => e.FullName));
        foreach (string signature in signatures) Program.Require(!LegacyReference(signature), "Off legacy metadata reference: " + signature);
        foreach (var method in types.SelectMany(t => t.Methods))
        {
            Program.Require(!Shadow(method.Name) && !LegacyReference(method.FullName), "Off legacy method: " + method.FullName);
            if (!method.HasBody) continue;
            var body = method.Body.Variables.Select(v => v.VariableType.FullName)
                .Concat(method.Body.ExceptionHandlers.Select(h => h.CatchType?.FullName ?? ""))
                .Concat(method.Body.Instructions.Select(i => i.Operand?.ToString() ?? ""));
            foreach (string operand in body) Program.Require(!LegacyReference(operand) && !operand.Contains("__LegacyExtended_", StringComparison.Ordinal), "Off legacy IL operand: " + method.FullName + ": " + operand);
        }
    }

    static void CompareReference(TypeDefinition baseType, TypeDefinition random, string path)
    {
        using var reference = ModuleDefinition.ReadModule(Path.GetFullPath(path));
        foreach (var type in new[] { baseType, random })
        foreach (var method in type.Methods.Where(m => m.HasBody && (type == random || m.Name is "Move" or "GetDistance" or "HitCheck")))
        {
            var expected = Types(reference.Types).Single(t => t.FullName == type.FullName).Methods.Single(m => m.FullName == method.FullName);
            Program.Require(Fingerprint(method) == Fingerprint(expected), "Ordinary off-reference IL mismatch: " + method.FullName);
        }
    }

    static string Fingerprint(MethodDefinition method) => string.Join('\n', method.Body.Instructions.Select(i => i.OpCode.Name + " " + (i.Operand switch
    {
        Instruction target => "@" + method.Body.Instructions.IndexOf(target),
        Instruction[] targets => string.Join(',', targets.Select(t => method.Body.Instructions.IndexOf(t))),
        _ => i.Operand?.ToString() ?? ""
    })));
}
