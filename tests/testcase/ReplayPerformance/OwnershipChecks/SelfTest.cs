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
        state.GetType().GetProperty("StageData")!.SetValue(state, stage);
        objectType.GetProperty("StageData")!.SetValue(owner, stage);
        OwnershipProbe.Arm(stage); OwnershipProbe.EnterFrame(state);
        var time = objectType.GetProperty("Time")!;
        time.SetValue(owner, 1);
        objectType.GetMethod("HitCheck", new[] { objectType, typeof(float) })!.Invoke(owner, new object?[] { null, 0f });
        string report = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!, "smoke-positive.json");
        OwnershipProbe.SaveReport(report);
        Worker(() => time.SetValue(owner, 999), true);
        if ((int)time.GetValue(owner)! != 1) throw new InvalidOperationException("Forbidden write happened before guard");
        ExpectFailure(() => OwnershipProbe.SaveReport(Path.Combine(Path.GetDirectoryName(report)!, "smoke-negative.json")), "actual setter violation report");
        Console.WriteLine("OWNERSHIP_ACTUAL_WOVEN_SETTER_PASS forbidden write blocked before mutation; " + report);
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
        Console.WriteLine("OWNERSHIP_SELF_TEST_PASS main+worker+loading+unpublished+assignment+empty+branch+switch+EH");
    }

    private static void Worker(Action action, bool shouldFail)
    {
        Exception? error = null;
        var worker = new Thread(() => { try { action(); } catch (Exception exception) { error = exception is TargetInvocationException ? exception.InnerException : exception; } });
        worker.Start(); worker.Join();
        if (shouldFail ? error is not InvalidOperationException : error != null)
            throw new InvalidOperationException("Unexpected worker gate result", error);
    }

    private static void ExpectFailure(Action action, string name)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Missing expected failure: " + name);
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
