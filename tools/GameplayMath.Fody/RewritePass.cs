using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GameplayMath;

public sealed record WeaveOptions(bool ExcludeOwnedRandom = true, int TargetPointerSize = 0);

public static class RewritePass
{
    public static WeaveManifest Execute(ModuleDefinition module, WeaveOptions? options = null)
    {
        options ??= new();
        if (options.TargetPointerSize is not (0 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(options), "TargetPointerSize must be 0, 4, or 8");
        if (ScopePolicy.AllTypes(module).SelectMany(t => t.Methods).Any(m => m.Name.StartsWith("__LegacyExtended_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Assembly already contains extended shadows; weave a fresh compiler output");
        var policy = new ScopePolicy(module, options.ExcludeOwnedRandom);
        var typeNames = ScopePolicy.AllTypes(module).Select(t => t.FullName).ToHashSet(StringComparer.Ordinal);
        var report = new WeaveManifest { Assembly = module.Assembly.Name.FullName,
            DeclaredRoots = ScopePolicy.DeclaredRoots.ToArray(), MissingRoots = ScopePolicy.DeclaredRoots.Where(r => !typeNames.Contains(r)).ToArray() };
        var methods = ScopePolicy.AllTypes(module).SelectMany(t => t.Methods).ToArray();
        report.InputMethodCount = methods.Length;
        var untouched = methods.Where(m => !policy.Decide(m).Eligible).ToDictionary(m => m, SemanticFingerprint.Of);
        var records = methods.ToDictionary(m => m, m => new MethodRecord {
            Method = m.FullName, Scope = policy.Decide(m), Numeric = WeaveManifest.Numeric(m), RandomBefore = WeaveManifest.RandomCalls(m)
        });
        report.Methods.AddRange(records.Values);
        var runtime = new RuntimeContract(module);
        var shadows = CreateShadows(methods.Where(m => policy.Decide(m).Eligible), runtime);
        foreach (var method in methods.Concat(shadows.Values))
        {
            var record = records.GetValueOrDefault(method);
            if (record != null && !record.Scope.Eligible)
            {
                record.RandomAfter = WeaveManifest.RandomCalls(method);
                if (record.Scope.Ambiguous) { record.Action = "review-required"; report.Errors.Add(method.FullName + ": " + record.Scope.Reason); }
                continue;
            }
            if (record == null)
            {
                record = new MethodRecord { Method = method.FullName, Scope = new(true, "proven CLR2 extended getter return channel"), Numeric = WeaveManifest.Numeric(method), RandomBefore = WeaveManifest.RandomCalls(method) };
                report.Methods.Add(record);
            }
            try
            {
                var analysis = new StackAnalysis(method); analysis.Run();
                record.Numeric = WeaveManifest.Numeric(method, analysis);
                var before = SemanticFingerprint.Of(method);
                new ExtendedRewrite(method, runtime, analysis, shadows, options.TargetPointerSize).Run();
                record.Action = before == SemanticFingerprint.Of(method) ? "eligible-unchanged" : "rewritten";
                record.RandomAfter = WeaveManifest.RandomCalls(method);
                if (!record.RandomBefore.SequenceEqual(record.RandomAfter)) throw new InvalidOperationException("RNG call sequence changed: " + method.FullName);
            }
            catch (Exception error) when (error is NotSupportedException or InvalidOperationException)
            {
                record.Action = "unsupported"; record.Error = error.Message; report.Errors.Add(error.Message);
            }
        }
        foreach (var (method, expected) in untouched)
        {
            if (SemanticFingerprint.Of(method) != expected) report.Errors.Add("Outside-scope semantics changed: " + method.FullName);
            else report.OutsideScopeUnchanged++;
        }
        if (!report.Success) throw new WeaveException(report);
        return report;
    }
    static Dictionary<string, MethodDefinition> CreateShadows(IEnumerable<MethodDefinition> methods, RuntimeContract runtime)
    {
        var shadows = new Dictionary<string, MethodDefinition>();
        foreach (var getter in methods.Where(m => m.DeclaringType.FullName == "Shooting.BaseObject" && m.Name is "get_Vx" or "get_Vy"))
        {
            if (getter.IsVirtual || getter.IsStatic || getter.Body.ExceptionHandlers.Count != 0 || getter.Parameters.Count != 0 || getter.ReturnType.MetadataType != MetadataType.Single)
                throw new NotSupportedException("Unreviewed extended-return getter body: " + getter.FullName);
            var expression = GetterExpression(getter);
            var shadow = new MethodDefinition("__LegacyExtended_" + getter.Name, MethodAttributes.Assembly | MethodAttributes.HideBySig, runtime.Extended);
            shadow.Body.InitLocals = getter.Body.InitLocals;
            getter.DeclaringType.Methods.Add(shadow);
            foreach (var original in expression)
            {
                var copy = Instruction.Create(OpCodes.Nop); copy.OpCode = original.OpCode; copy.Operand = original.Operand;
                shadow.Body.Instructions.Add(copy);
            }
            shadows.Add(getter.FullName, shadow);
        }
        return shadows;
    }

    static Instruction[] GetterExpression(MethodDefinition getter)
    {
        var code = getter.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        bool StraightLine(IEnumerable<Instruction> instructions) => instructions.All(i =>
            i.Operand is not Instruction and not Instruction[] &&
            i.OpCode.FlowControl is not (FlowControl.Branch or FlowControl.Cond_Branch or FlowControl.Return or FlowControl.Throw));
        if (code.Length != 0 && code[^1].OpCode.Code == Code.Ret && getter.Body.Variables.Count == 0 && StraightLine(code[..^1]))
            return code;

        // Debug C# lowers the same expression to `stloc; br; ldloc; ret`.
        // That compiler return slot is not an additional CLR2 physical float store.
        if (getter.Body.Variables.Count == 1 && getter.Body.Variables[0].VariableType.MetadataType == MetadataType.Single && code.Length >= 4)
        {
            var slot = getter.Body.Variables[0];
            bool Store(Instruction i) => i.OpCode.Code == Code.Stloc_0 ||
                ((i.OpCode.Code is Code.Stloc or Code.Stloc_S) && ReferenceEquals(i.Operand, slot));
            bool Load(Instruction i) => i.OpCode.Code == Code.Ldloc_0 ||
                ((i.OpCode.Code is Code.Ldloc or Code.Ldloc_S) && ReferenceEquals(i.Operand, slot));
            var target = code[^3].Operand as Instruction;
            while (target?.OpCode.Code == Code.Nop) target = target.Next;
            if (Store(code[^4]) && (code[^3].OpCode.Code is Code.Br or Code.Br_S) &&
                ReferenceEquals(target, code[^2]) && Load(code[^2]) && code[^1].OpCode.Code == Code.Ret &&
                StraightLine(code[..^4]) && code[..^4].All(i => i.Operand is not VariableDefinition &&
                    i.OpCode.Code is not (Code.Ldloc_0 or Code.Ldloc_1 or Code.Ldloc_2 or Code.Ldloc_3 or
                        Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3)))
                return code[..^4].Append(code[^1]).ToArray();
        }
        throw new NotSupportedException("Unreviewed extended-return getter body: " + getter.FullName);
    }
}
