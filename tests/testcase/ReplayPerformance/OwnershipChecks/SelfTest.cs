using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using THSSS.Diagnostics;
using static Instrumentation;

internal static class SelfTest
{
    internal static void SmokeCopy(string assemblyPath, string dependencyDirectory)
    {
        System.Runtime.Loader.AssemblyLoadContext.Default.Resolving += (_, name) =>
        {
            string candidate = Path.Combine(dependencyDirectory, name.Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        var game = Assembly.LoadFile(Path.GetFullPath(assemblyPath));
        object stage = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(game.GetType("Shooting.StageDataPackage")!);
        object state = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(game.GetType("Shooting.BaseStage")!);
        var objectType = game.GetType("Shooting.BaseObject")!;
        object owner = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(objectType);
        var scriptType = game.GetType("Shooting.BaseObject_CS", throwOnError: true)!;
        object scriptOwner = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(scriptType);
        state.GetType().GetProperty("StageData")!.SetValue(state, stage);
        objectType.GetProperty("StageData")!.SetValue(owner, stage);
        objectType.GetProperty("StageData")!.SetValue(scriptOwner, stage);
        OwnershipProbe.Arm(stage); OwnershipProbe.EnterFrame(state);
        var time = objectType.GetProperty("Time")!;
        time.SetValue(owner, 1);
        var rates = new[] { "AngularVelocityDegree", "DirectionVelocityDegree" }
            .Select(name => objectType.GetProperty(name) ?? throw new InvalidDataException("Missing rate property: " + name)).ToArray();
        foreach (var rate in rates) rate.SetValue(owner, 3.5f);
        var acceleration = new[] { "AccDirection", "AccelerateCS", "Ax", "Ay" }
            .Select(name => scriptType.GetProperty(name) ?? throw new InvalidDataException("Missing acceleration property: " + name)).ToArray();
        foreach (var property in acceleration) property.SetValue(scriptOwner, Value(property, 3.5f));
        objectType.GetMethod("HitCheck", new[] { objectType, typeof(float) })!.Invoke(owner, new object?[] { null, 0f });
        string report = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!, "smoke-positive.json");
        OwnershipProbe.SaveReport(report);
        Worker(() => time.SetValue(owner, 999), true);
        if ((int)time.GetValue(owner)! != 1) throw new InvalidOperationException("Forbidden write happened before guard");
        foreach (var rate in rates)
        {
            Worker(() => rate.SetValue(owner, 999f), true);
            if ((float)rate.GetValue(owner)! != 3.5f)
                throw new InvalidOperationException("Forbidden rate write happened before guard: " + rate.Name);
        }
        foreach (var property in acceleration)
        {
            Worker(() => property.SetValue(scriptOwner, Value(property, 999f)), true);
            if (!Equals(property.GetValue(scriptOwner), Value(property, 3.5f)))
                throw new InvalidOperationException("Forbidden acceleration write happened before guard: " + property.Name);
        }
        OwnershipProbe.EnterFrame(null);
        foreach (var rate in rates) Worker(() => rate.SetValue(owner, 4.5f), false);
        AllowedAccelerationWrites(scriptOwner, 4.5f);
        object loading = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(game.GetType("Shooting.GameState_MusicLoading", throwOnError: true)!);
        loading.GetType().GetProperty("StageData")!.SetValue(loading, stage);
        OwnershipProbe.EnterFrame(loading);
        AllowedAccelerationWrites(scriptOwner, 5.5f);
        OwnershipProbe.EnterFrame(state);
        object unpublished = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(objectType);
        foreach (var rate in rates) Worker(() => rate.SetValue(unpublished, 5.5f), false);
        object unpublishedScript = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(scriptType);
        AllowedAccelerationWrites(unpublishedScript, 6.5f);
        ExpectFailure(() => OwnershipProbe.SaveReport(Path.Combine(Path.GetDirectoryName(report)!, "smoke-negative.json")), "actual setter violation report");
        Console.WriteLine("OWNERSHIP_ACTUAL_WOVEN_SETTER_PASS Time+AngularVelocityDegree+DirectionVelocityDegree+AccDirection+AccelerateCS+Ax+Ay blocked before mutation; suspended+loading+unpublished writes verified; " + report);

        static object Value(PropertyInfo property, float value) => property.PropertyType == typeof(double) ? (object)(double)value : value;
        void AllowedAccelerationWrites(object target, float value)
        {
            foreach (var property in acceleration)
            {
                Worker(() => property.SetValue(target, Value(property, value)), false);
                if (!Equals(property.GetValue(target), Value(property, value)))
                    throw new InvalidOperationException("Excluded acceleration write did not happen: " + property.Name);
            }
        }
    }

    internal static void Run()
    {
        string root = Path.Combine(AppContext.BaseDirectory, "self-test");
        Directory.CreateDirectory(root);
        ExpectFailure(() => OwnershipProbe.SaveReport(Path.Combine(root, "empty.json")), "empty coverage");
        object stage = new Shooting.StageDataPackage();
        var owner = new Shooting.BaseObject { StageData = stage };
        OwnershipProbe.Arm(stage);
        OwnershipProbe.EnterFrame(new Shooting.BaseStage { StageData = stage });
        OwnershipProbe.GuardWrite(owner, null, "test", "main");
        OwnershipProbe.GuardCall(owner, "Move");
        OwnershipProbe.SaveReport(Path.Combine(root, "positive.json"));
        OwnershipProbe.EnterFrame(new Shooting.MusicLoading { StageData = stage });
        Worker(() => OwnershipProbe.GuardWrite(owner, null, "test", "suspended"), false);
        OwnershipProbe.EnterFrame(new Shooting.BaseStage { StageData = stage });
        Worker(() => OwnershipProbe.GuardWrite(new Shooting.BaseObject { StageData = new object() }, null, "test", "unpublished"), false);
        Worker(() => OwnershipProbe.GuardWrite(owner, null, "test", "cross-thread"), true);
        Worker(() => OwnershipProbe.GuardWrite(new Shooting.BaseObject(), stage, "StageData", "assignment"), true);
        ExpectFailure(() => OwnershipProbe.SaveReport(Path.Combine(root, "negative.json")), "violation report");
        ControlFlow(root);
        FieldSelection();
        Console.WriteLine("OWNERSHIP_SELF_TEST_PASS main+worker+loading+unpublished+assignment+empty+branch+switch+EH+required-rate-and-acceleration-storage");
    }

    private static void Worker(Action action, bool shouldFail)
    {
        Exception? error = null;
        var worker = new Thread(() => { try { action(); } catch (Exception exception) { error = exception is TargetInvocationException ? exception.InnerException : exception; } });
        worker.Start(); worker.Join();
        if (shouldFail ? error is not InvalidOperationException || !error.Message.StartsWith("Active-stage ownership violation:", StringComparison.Ordinal) : error != null)
            throw new InvalidOperationException("Unexpected worker gate result", error);
    }

    private static void ExpectFailure(Action action, string name)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Missing expected failure: " + name);
    }

    private static void FieldSelection()
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("OwnershipFieldsSelfTest", new Version(1, 0)), "OwnershipFieldsSelfTest", ModuleKind.Dll);
        var module = assembly.MainModule;
        var owner = new TypeDefinition("Shooting", "BaseObject", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        var stage = new TypeDefinition("Shooting", "StageDataPackage", Mono.Cecil.TypeAttributes.Public, module.TypeSystem.Object);
        var script = new TypeDefinition("Shooting", "BaseObject_CS", Mono.Cecil.TypeAttributes.Public, owner);
        module.Types.Add(owner); module.Types.Add(stage); module.Types.Add(script);
        void Add(string name, TypeReference type) => owner.Fields.Add(new FieldDefinition(name, Mono.Cecil.FieldAttributes.Private, type));
        Add("<OriginalPosition>k__BackingField", module.ImportReference(typeof(System.Drawing.PointF)));
        Add("<StageData>k__BackingField", stage); Add("<Time>k__BackingField", module.TypeSystem.Int32);
        foreach (string name in new[] { "velocity", "scaleLength", "scaleWidth", "scaleX", "scaleY", "<AngularVelocityDegree>k__BackingField", "<DirectionVelocityDegree>k__BackingField", "<Ax>k__BackingField", "<Ay>k__BackingField" }) Add(name, module.TypeSystem.Single);
        script.Fields.Add(new FieldDefinition("<AccDirection>k__BackingField", Mono.Cecil.FieldAttributes.Private, module.TypeSystem.Double));
        script.Fields.Add(new FieldDefinition("<AccelerateCS>k__BackingField", Mono.Cecil.FieldAttributes.Private, module.TypeSystem.Single));
        Add("direction", module.TypeSystem.Double); Add("angle", module.TypeSystem.Double);
        Add("angleWithDirection", module.TypeSystem.Boolean); Add("region", module.TypeSystem.Int32);
        stage.Fields.Add(new FieldDefinition("BoundRect", Mono.Cecil.FieldAttributes.Public, module.ImportReference(typeof(System.Drawing.Rectangle))));
        var selected = SelectFields(owner, script, stage);
        var required = owner.Fields.Where(field => new[] { "<AngularVelocityDegree>k__BackingField", "<DirectionVelocityDegree>k__BackingField", "<Ax>k__BackingField", "<Ay>k__BackingField" }.Contains(field.Name))
            .Concat(script.Fields).ToArray();
        foreach (var field in required)
        {
            if (!selected.ContainsKey(field.FullName)) throw new InvalidOperationException("Required field not inventoried: " + field.FullName);
            var declaringType = field.DeclaringType;
            var expectedType = field.FieldType;
            declaringType.Fields.Remove(field);
            Reject();
            var wrongOwner = declaringType == owner ? script : owner;
            wrongOwner.Fields.Add(field);
            Reject(); wrongOwner.Fields.Remove(field); declaringType.Fields.Add(field);
            field.FieldType = expectedType.FullName == "System.Single" ? module.TypeSystem.Double : module.TypeSystem.Single;
            Reject(); field.FieldType = expectedType;
            field.IsStatic = true;
            Reject(); field.IsStatic = false;
        }
        void Reject()
        {
            try { SelectFields(owner, script, stage); }
            catch (InvalidDataException) { return; }
            throw new InvalidOperationException("Invalid required rate/acceleration storage accepted");
        }
    }

    private static void ControlFlow(string root)
    {
        using var assembly = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("OwnershipILSelfTest", new Version(1, 0)), "OwnershipILSelfTest", ModuleKind.Dll);
        var module = assembly.MainModule;
        var type = new TypeDefinition("Test", "Fixture", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
        module.Types.Add(type);
        var field = new FieldDefinition("Count", Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static, module.TypeSystem.Int32);
        type.Fields.Add(field);
        foreach (bool useSwitch in new[] { false, true })
        {
            var method = new MethodDefinition(useSwitch ? "Switch" : "Branch", Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static, module.TypeSystem.Void);
            type.Methods.Add(method);
            var il = method.Body.GetILProcessor();
            var target = Instruction.Create(OpCodes.Nop);
            if (useSwitch) { il.Append(Instruction.Create(OpCodes.Ldc_I4_0)); il.Append(Instruction.Create(OpCodes.Switch, new[] { target })); }
            else il.Append(Instruction.Create(OpCodes.Br_S, target));
            il.Append(target); il.Append(Instruction.Create(OpCodes.Ret));
            var increment = new[] { Instruction.Create(OpCodes.Ldsfld, field), Instruction.Create(OpCodes.Ldc_I4_1), Instruction.Create(OpCodes.Add), Instruction.Create(OpCodes.Stsfld, field) };
            ExpandBranches(method); InsertBefore(method, target, increment);
            if (useSwitch ? ((Instruction[])method.Body.Instructions[1].Operand)[0] != increment[0] : method.Body.Instructions[0].Operand != increment[0])
                throw new InvalidOperationException("Control flow bypasses injected prefix");
            var handler = new ExceptionHandler(ExceptionHandlerType.Finally) { TryStart = target, TryEnd = target, HandlerStart = target, HandlerEnd = target, FilterStart = target };
            method.Body.ExceptionHandlers.Add(handler);
            var marker = Instruction.Create(OpCodes.Nop); InsertBefore(method, target, new[] { marker });
            if (new[] { handler.TryStart, handler.TryEnd, handler.HandlerStart, handler.HandlerEnd, handler.FilterStart }.Any(i => i != marker))
                throw new InvalidOperationException("EH boundaries not retargeted");
            method.Body.ExceptionHandlers.Clear();
        }
        string output = Path.Combine(root, "OwnershipILSelfTest.dll");
        assembly.Write(output);
        var runtime = Assembly.LoadFile(output).GetType("Test.Fixture")!;
        runtime.GetMethod("Branch")!.Invoke(null, null); runtime.GetMethod("Switch")!.Invoke(null, null);
        if ((int)runtime.GetField("Count")!.GetValue(null)! != 2) throw new InvalidOperationException("Prefix execution count wrong");
    }
}

namespace Shooting
{
    public class StageDataPackage { }
    public class BaseObject { public object? StageData { get; set; } }
    public class BaseStage : BaseObject { }
    public class MusicLoading : BaseObject { }
}
