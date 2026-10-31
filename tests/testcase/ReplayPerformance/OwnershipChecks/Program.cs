using System.Text.Json;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;
using THSSS.Diagnostics;
using static Instrumentation;

if (args.Length == 1 && args[0] == "--self-test") { SelfTest.Run(); return; }
if (args.Length == 3 && args[0] == "--smoke-copy") { SelfTest.SmokeCopy(args[1], args[2]); return; }
if (args.Length != 2) throw new ArgumentException("Usage: OwnershipTool INPUT.dll OUTPUT.dll | --self-test");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (input == output || File.Exists(output)) throw new ArgumentException("Output must be a new diagnostic copy, not input or an existing file.");
string inputSha256 = Fingerprint(input);
using var assembly = AssemblyDefinition.ReadAssembly(input, new ReaderParameters { ReadSymbols = false });
var module = assembly.MainModule;
if (module.AssemblyReferences.Any(r => r.Name == "OwnershipProbe")) throw new InvalidDataException("Input already instrumented.");
var types = AllTypes(module.Types).ToArray();
var baseObject = types.Single(t => t.FullName == "Shooting.BaseObject");
var stageData = types.Single(t => t.FullName == "Shooting.StageDataPackage");
string[] required = { "<OriginalPosition>k__BackingField", "<StageData>k__BackingField", "<Time>k__BackingField", "velocity", "direction", "angle", "angleWithDirection", "region", "scaleLength", "scaleWidth", "scaleX", "scaleY" };
var fields = required.Select(name => baseObject.Fields.Single(f => f.Name == name)).Append(stageData.Fields.Single(f => f.Name == "BoundRect")).ToDictionary(f => f.FullName);
var guardWrite = module.ImportReference(typeof(OwnershipProbe).GetMethod(nameof(OwnershipProbe.GuardWrite))!);
var guardCall = module.ImportReference(typeof(OwnershipProbe).GetMethod(nameof(OwnershipProbe.GuardCall))!);
var enterFrame = module.ImportReference(typeof(OwnershipProbe).GetMethod(nameof(OwnershipProbe.EnterFrame))!);
var sites = new List<object>();
var addressGaps = new List<string>();
int writeCount = 0, callCount = 0, frameHooks = 0;
foreach (var method in types.SelectMany(t => t.Methods).Where(m => m.HasBody))
{
    ExpandBranches(method);
    foreach (var instruction in method.Body.Instructions.ToArray())
    {
        if (instruction.Operand is not FieldReference field || !fields.ContainsKey(field.FullName)) continue;
        string site = method.FullName + $" IL_{instruction.Offset:x4}";
        string operation = instruction.OpCode.Name;
        if (instruction.OpCode == OpCodes.Ldflda)
        {
            bool readOnly = instruction.Next?.Operand is MethodReference next && next.Name.StartsWith("get_", StringComparison.Ordinal)
                && next.DeclaringType.FullName is "System.Drawing.Rectangle" or "System.Drawing.PointF"
                && instruction.Next.OpCode == OpCodes.Call;
            bool equals = IsRectangleEquals(instruction);
            readOnly |= equals;
            operation += equals ? ":readonly-rectangle-equals-exact-pattern" : readOnly ? ":readonly-immediate-getter" : ":UNCLASSIFIED";
            if (!readOnly)
            {
                var context = new List<string>();
                for (Instruction? cursor = instruction; cursor != null && context.Count < 14; cursor = cursor.Next) context.Add(cursor.ToString());
                addressGaps.Add(site + " " + field.FullName + " :: " + string.Join(" | ", context));
            }
        }
        sites.Add(new { operation, field = field.FullName, site });
        if (instruction.OpCode != OpCodes.Stfld) continue;
        InjectWrite(method, instruction, field, site);
        writeCount++;
    }
    if (!method.IsStatic && DerivesFrom(method.DeclaringType, "Shooting.BaseObject") && method.Name is "Move" or "HitCheck")
    {
        var prefix = new List<Instruction> { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldstr, method.FullName), Instruction.Create(OpCodes.Call, guardCall) };
        foreach (var parameter in method.Parameters.Where(p => p.ParameterType.FullName == "Shooting.BaseObject"))
        {
            prefix.Add(Instruction.Create(OpCodes.Ldarg, parameter));
            prefix.Add(Instruction.Create(OpCodes.Ldstr, method.FullName + " target:" + parameter.Name));
            prefix.Add(Instruction.Create(OpCodes.Call, guardCall));
        }
        InsertBefore(method, method.Body.Instructions[0], prefix);
        callCount++;
    }
}
var game = types.Single(t => t.FullName == "Shooting.Game_Main");
var stateField = game.Fields.Single(f => f.Name == "PresentState");
var main = game.Methods.Single(m => m.Name == "MainProcess");
InsertBefore(main, main.Body.Instructions[0], FramePrefix());
frameHooks++;
foreach (var method in game.Methods.Where(m => m.HasBody && !m.IsStatic))
{
    foreach (var instruction in method.Body.Instructions.ToArray())
    {
        if (instruction.OpCode == OpCodes.Stfld && instruction.Operand is FieldReference field && field.FullName == stateField.FullName)
        {
            var il = method.Body.GetILProcessor(); var previous = instruction;
            foreach (var item in FramePrefix()) { il.InsertAfter(previous, item); previous = item; }
            frameHooks++;
        }
        if (method != main || instruction.Operand is not MethodReference called || called.Name is not ("UpdateData" or "Render")) continue;
        InsertBefore(main, instruction, FramePrefix());
        frameHooks++;
    }
}
string manifest = output + ".ownership-manifest.json";
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(manifest, JsonSerializer.Serialize(new { input, inputSha256, output, fields = fields.Keys.Order().ToArray(), writeCount, callCount, frameHooks,
    addressGaps, sites, scope = "Whole module field instructions; BaseObject and derived Move/HitCheck entries including BaseObject targets; current PresentState at MainProcess entry, state reassignment and update/render dispatch." }, new JsonSerializerOptions { WriteIndented = true }));
if (addressGaps.Count > 0 || writeCount == 0 || callCount == 0 || frameHooks < 3)
    throw new InvalidDataException("Incomplete ownership instrumentation; inspect " + manifest);
if (Fingerprint(input) != inputSha256) throw new IOException("Input assembly changed during instrumentation.");
using (var destination = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    assembly.Write(destination);
string helper = typeof(OwnershipProbe).Assembly.Location;
File.Copy(helper, Path.Combine(Path.GetDirectoryName(output)!, "OwnershipProbe.dll"), true);
Console.WriteLine($"OWNERSHIP_COPY_CREATED writes={writeCount} methods={callCount} frameHooks={frameHooks} gaps={addressGaps.Count} manifest={manifest}");

static string Fingerprint(string path)
{
    using var stream = File.OpenRead(path);
    return Convert.ToHexString(SHA256.HashData(stream));
}

Instruction[] FramePrefix() => new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, stateField), Instruction.Create(OpCodes.Call, enterFrame) };

void InjectWrite(MethodDefinition method, Instruction instruction, FieldReference field, string site)
{
    if (instruction.Previous?.OpCode.OpCodeType == OpCodeType.Prefix)
        throw new InvalidDataException("Unsupported prefixed field write: " + site);
    var value = new VariableDefinition(field.FieldType);
    var owner = new VariableDefinition(field.DeclaringType);
    method.Body.Variables.Add(value); method.Body.Variables.Add(owner); method.Body.InitLocals = true;
    var prefix = new List<Instruction> { Instruction.Create(OpCodes.Stloc, value), Instruction.Create(OpCodes.Stloc, owner), Instruction.Create(OpCodes.Ldloc, owner) };
    prefix.Add(field.Name == "<StageData>k__BackingField" ? Instruction.Create(OpCodes.Ldloc, value) : Instruction.Create(OpCodes.Ldnull));
    prefix.AddRange(new[] { Instruction.Create(OpCodes.Ldstr, field.FullName), Instruction.Create(OpCodes.Ldstr, site), Instruction.Create(OpCodes.Call, guardWrite), Instruction.Create(OpCodes.Ldloc, owner), Instruction.Create(OpCodes.Ldloc, value) });
    InsertBefore(method, instruction, prefix);
}

static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> roots)
{
    foreach (var type in roots) { yield return type; foreach (var nested in AllTypes(type.NestedTypes)) yield return nested; }
}

static bool DerivesFrom(TypeDefinition type, string name)
{
    for (TypeDefinition? current = type; current != null; current = current.BaseType?.Scope is ModuleDefinition ? current.BaseType.Resolve() : null)
        if (current.FullName == name) return true;
    return false;
}

static bool IsRectangleEquals(Instruction address)
{
    var items = new List<Instruction>();
    for (var cursor = address.Next; cursor != null && items.Count < 8; cursor = cursor.Next) items.Add(cursor);
    if (items.Count != 8) return false;
    return items[0].OpCode == OpCodes.Ldc_I4_0 && items[1].OpCode == OpCodes.Ldc_I4_0
        && items[2].OpCode == OpCodes.Ldc_I4 && (int)items[2].Operand == 640
        && items[3].OpCode == OpCodes.Ldc_I4 && (int)items[3].Operand == 480
        && items[4].OpCode == OpCodes.Newobj && items[4].Operand is MethodReference ctor
        && ctor.FullName == "System.Void System.Drawing.Rectangle::.ctor(System.Int32,System.Int32,System.Int32,System.Int32)"
        && items[5].OpCode == OpCodes.Box && ((TypeReference)items[5].Operand).FullName == "System.Drawing.Rectangle"
        && items[6].OpCode == OpCodes.Constrained && ((TypeReference)items[6].Operand).FullName == "System.Drawing.Rectangle"
        && items[7].OpCode == OpCodes.Callvirt && items[7].Operand is MethodReference equals
        && equals.FullName == "System.Boolean System.Object::Equals(System.Object)";
}

internal static partial class Instrumentation
{
    internal static void ExpandBranches(MethodDefinition method)
    {
        foreach (var instruction in method.Body.Instructions.Where(i => i.OpCode.OperandType == OperandType.ShortInlineBrTarget))
            instruction.OpCode = (OpCode)typeof(OpCodes).GetField(instruction.OpCode.Code.ToString()[..^2])!.GetValue(null)!;
    }

    internal static void InsertBefore(MethodDefinition method, Instruction original, IEnumerable<Instruction> prefix)
    {
        var items = prefix.ToArray();
        if (items.Length == 0) throw new ArgumentException("Empty IL prefix");
        foreach (var instruction in method.Body.Instructions)
        {
            if (ReferenceEquals(instruction.Operand, original)) instruction.Operand = items[0];
            if (instruction.Operand is Instruction[] targets)
                for (int i = 0; i < targets.Length; i++) if (ReferenceEquals(targets[i], original)) targets[i] = items[0];
        }
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            if (handler.TryStart == original) handler.TryStart = items[0];
            if (handler.TryEnd == original) handler.TryEnd = items[0];
            if (handler.HandlerStart == original) handler.HandlerStart = items[0];
            if (handler.HandlerEnd == original) handler.HandlerEnd = items[0];
            if (handler.FilterStart == original) handler.FilterStart = items[0];
        }
        var il = method.Body.GetILProcessor();
        foreach (var item in items) il.InsertBefore(original, item);
        method.Body.MaxStackSize += 8;
    }
}
