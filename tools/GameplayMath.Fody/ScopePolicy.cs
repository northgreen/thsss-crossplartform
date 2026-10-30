using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GameplayMath;

public sealed record ScopeDecision(bool Eligible, string Reason, bool Ambiguous = false);

public sealed class ScopePolicy
{
    static readonly HashSet<string> Roots = new(StringComparer.Ordinal) {
        "Shooting.BaseObject", "Shooting.BaseStage", "Shooting.Execution", "Shooting.Event",
        "Shooting.EventGroup", "Shooting.CS_Data", "Shooting.BaseBullet_CS", "Shooting.CSEmitterController",
        "Shooting.BaseObject_CS", "Shooting.BaseEmitter_CS", "Shooting.BaseCover_CS",
        "Shooting.AutoPlane", "Shooting.StageDataPackage", "Shooting.MyRandom"
    };
    static readonly HashSet<string> UiRoots = new(StringComparer.Ordinal) { "Shooting.BaseMenuGroup", "Shooting.BaseMenuItem" };
    public static IReadOnlyCollection<string> DeclaredRoots => Roots.Order(StringComparer.Ordinal).ToArray();
    readonly Dictionary<MethodDefinition, ScopeDecision> decisions = new();
    public ScopePolicy(ModuleDefinition module, bool excludeOwnedRandom = true)
    {
        if (!excludeOwnedRandom) throw new ArgumentException("Shooting.MyRandom is manually owned and must remain excluded", nameof(excludeOwnedRandom));
        var methods = AllTypes(module).SelectMany(t => t.Methods).ToArray();
        foreach (var method in methods)
        {
            var type = method.DeclaringType;
            decisions[method] = !method.HasBody ? new(false, "no managed body") :
                type.Namespace.StartsWith("SlimDX.Compat", StringComparison.Ordinal) ? new(false, "owned compatibility runtime") :
                type.FullName == "Shooting.MyRandom" ? new(false, "explicitly parent-owned random runtime; integer state and software scaling are manually implemented") :
                InHierarchy(Outer(type), UiRoots) ? new(false, "explicit menu/UI hierarchy") :
                method.Name == "InitializeComponent" ? new(false, "form designer initialization") :
                Presentation(method) ? new(false, "presentation member") :
                Generated(method) ? new(false, "outside reviewed simulation roots") :
                InHierarchy(type) ? new(true, "explicit simulation root or derived hierarchy") :
                new(false, "outside reviewed simulation roots");
        }
        // A generated callback is owned by its actual IL references, never by its namespace.
        var owners = new Dictionary<MethodDefinition, HashSet<MethodDefinition>>();
        foreach (var method in methods.Where(m => m.HasBody))
        foreach (var reference in method.Body.Instructions.Select(i => i.Operand).OfType<MethodReference>())
        {
            var target = ResolveLocal(reference, module);
            if (target == null || !Generated(target)) continue;
            if (!owners.TryGetValue(target, out var set)) owners[target] = set = new();
            set.Add(method);
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var (callback, callers) in owners)
            {
                bool simulation = callers.Any(c => decisions[c].Eligible);
                bool presentation = callers.Any(c => Presentation(c) || decisions[c].Reason.Contains("presentation", StringComparison.Ordinal));
                var decision = simulation && presentation ? new ScopeDecision(false, "generated callback shared by simulation and presentation; explicit review required", true) :
                    simulation ? new(true, "generated callback reached from simulation root") :
                    presentation ? new(false, "generated callback owned by presentation") : decisions[callback];
                if (decisions[callback] == decision) continue;
                decisions[callback] = decision; changed = true;
            }
        } while (changed);
        foreach (var method in methods.Where(Generated))
        {
            if (decisions[method].Reason != "outside reviewed simulation roots") continue;
            var owner = method.DeclaringType;
            while (owner.DeclaringType != null) owner = owner.DeclaringType;
            decisions[method] = InHierarchy(owner) && WeaveManifest.Numeric(method).Length != 0
                ? new(false, "unresolved simulation generated ownership; explicit review required", true)
                : new(false, "generated code outside simulation call ownership");
        }
    }
    public ScopeDecision Decide(MethodDefinition method) => decisions[method];
    public static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) => module.Types.SelectMany(Flatten);
    static IEnumerable<TypeDefinition> Flatten(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Flatten));
    static bool Presentation(string name) => new[] { "Render", "Draw", "Show" }.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
    static bool Presentation(MethodDefinition method)
    {
        if (Presentation(method.Name)) return true;
        bool environmentMember = new[] { "get_CameraAngle", "get_CameraPos", "SetRenderPara" }.Any(name =>
            method.Name == name || method.Name.EndsWith("." + name, StringComparison.Ordinal));
        if (!environmentMember) return false;
        // Environment updates may consume shared RNG, but their camera/draw contract is presentation-only.
        var visited = new HashSet<string>();
        for (var type = method.DeclaringType; type != null && visited.Add(type.FullName);)
        {
            if (type.Interfaces.Any(i => i.InterfaceType.FullName == "Shooting.IEnvironment")) return true;
            if (type.BaseType == null || type.BaseType.Scope != method.Module) return false;
            type = type.BaseType.Resolve();
        }
        return false;
    }
    static bool Generated(MethodDefinition m) => m.Name.StartsWith('<') || m.DeclaringType.Name.StartsWith('<');
    static TypeDefinition Outer(TypeDefinition type) => type.DeclaringType == null ? type : Outer(type.DeclaringType);
    static bool InHierarchy(TypeDefinition type) => InHierarchy(type, Roots);
    static bool InHierarchy(TypeDefinition type, HashSet<string> roots)
    {
        var visited = new HashSet<string>();
        for (TypeDefinition? current = type; current != null && visited.Add(current.FullName);)
        {
            if (roots.Contains(current.FullName)) return true;
            if (current.BaseType == null || current.BaseType.Scope != type.Module) return false;
            current = current.BaseType.Resolve();
        }
        return false;
    }
    static MethodDefinition? ResolveLocal(MethodReference reference, ModuleDefinition module)
    {
        if (reference.DeclaringType.GetElementType().Scope != module) return null;
        try { return reference.Resolve(); }
        catch (AssemblyResolutionException) { return null; }
    }
}
