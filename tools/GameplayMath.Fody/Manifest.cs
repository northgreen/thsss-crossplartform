using System.Globalization;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GameplayMath;

public sealed record OperationRecord(int Offset, string Opcode, string Operand, string[] NominalBefore);
public sealed class MethodRecord
{
    public required string Method { get; init; }
    public required ScopeDecision Scope { get; init; }
    public required OperationRecord[] Numeric { get; set; }
    public required string[] RandomBefore { get; init; }
    public string[] RandomAfter { get; set; } = [];
    public string Action { get; set; } = "excluded";
    public string? Error { get; set; }
}
public sealed class WeaveManifest
{
    public string PolicyVersion { get; init; } = "2026-10-31-v1";
    public required string Assembly { get; init; }
    public string[] DeclaredRoots { get; init; } = [];
    public string[] MissingRoots { get; init; } = [];
    public bool SignatureOnlyRuntimeAdapter { get; set; }
    public int SignatureOnlyRuntimeMethodCount { get; set; }
    public int InputMethodCount { get; set; }
    public List<MethodRecord> Methods { get; } = new();
    public List<string> Errors { get; } = new();
    public int OutsideScopeUnchanged { get; set; }
    public bool Success => Errors.Count == 0;
    public void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
    internal static OperationRecord[] Numeric(MethodDefinition method, StackAnalysis? analysis = null) => !method.HasBody ? [] : method.Body.Instructions
        .Where(i => i.OpCode.Code is Code.Add or Code.Sub or Code.Mul or Code.Div or Code.Rem or Code.Neg or Code.Ldc_R4 or Code.Ldc_R8 or Code.Ceq or Code.Cgt or Code.Cgt_Un or Code.Clt or Code.Clt_Un ||
            i.OpCode.Name.StartsWith("conv.", StringComparison.Ordinal) ||
            analysis?.Before.GetValueOrDefault(i)?.Any(StackAnalysis.Floating) == true ||
            i.Operand is FieldReference field && FloatingSignature(field.FieldType) ||
            i.Operand is MethodReference call && (call.DeclaringType.FullName is "System.Math" or "SlimDX.Vector2" || FloatingSignature(call.ReturnType) || call.Parameters.Any(p => FloatingSignature(p.ParameterType))))
        .Select(i => new OperationRecord(i.Offset, i.OpCode.Name, SemanticFingerprint.Operand(method, i.Operand), analysis?.Before.GetValueOrDefault(i) ?? [])).ToArray();
    static bool FloatingSignature(TypeReference type) => type.MetadataType is MetadataType.Single or MetadataType.Double ||
        type is TypeSpecification specification && FloatingSignature(specification.ElementType);
    internal static string[] RandomCalls(MethodDefinition method) => !method.HasBody ? [] : method.Body.Instructions
        .Where(i => i.OpCode.Code is Code.Call or Code.Callvirt or Code.Newobj)
        .Select(i => (MethodReference)i.Operand)
        .Where(m => m.DeclaringType.FullName is "System.Random" or "Shooting.MyRandom" || m.Name.StartsWith("Next", StringComparison.Ordinal) && m.DeclaringType.Name.Contains("Random", StringComparison.Ordinal))
        .Select(m => m.FullName).ToArray();
}
public sealed class WeaveException(WeaveManifest manifest) : Exception(string.Join(Environment.NewLine, manifest.Errors))
{
    public WeaveManifest Manifest { get; } = manifest;
}
public static class SemanticFingerprint
{
    public static string Of(MethodDefinition method)
    {
        if (!method.HasBody) return "no-body";
        var body = method.Body;
        string Index(Instruction? instruction) => instruction == null ? "end" : body.Instructions.IndexOf(instruction).ToString(CultureInfo.InvariantCulture);
        return string.Join('\n', new[] { "init=" + body.InitLocals,
            "locals=" + string.Join('|', body.Variables.Select(v => TypeIdentity(v.VariableType))),
            "eh=" + string.Join('|', body.ExceptionHandlers.Select(h => $"{h.HandlerType}:{(h.CatchType == null ? "" : TypeIdentity(h.CatchType))}:{Index(h.TryStart)}:{Index(h.TryEnd)}:{Index(h.HandlerStart)}:{Index(h.HandlerEnd)}:{Index(h.FilterStart)}")) }
            .Concat(body.Instructions.Select(i => i.OpCode.Name + " " + Operand(method, i.Operand))));
    }
    internal static string Operand(MethodDefinition method, object? operand) => operand switch {
        Instruction target => "@" + method.Body.Instructions.IndexOf(target),
        Instruction[] targets => string.Join(',', targets.Select(t => "@" + method.Body.Instructions.IndexOf(t))),
        MethodReference reference => MethodIdentity(reference),
        FieldReference field => TypeIdentity(field.DeclaringType) + "::" + field.Name + ":" + TypeIdentity(field.FieldType),
        TypeReference type => TypeIdentity(type),
        CallSite site => $"{site.CallingConvention}:{site.HasThis}:{site.ExplicitThis}:{TypeIdentity(site.ReturnType)}({string.Join(',', site.Parameters.Select(p => TypeIdentity(p.ParameterType)))})",
        VariableDefinition local => "local:" + local.Index,
        ParameterDefinition parameter => "arg:" + parameter.Index,
        float single => "f32:" + BitConverter.SingleToUInt32Bits(single).ToString("X8", CultureInfo.InvariantCulture),
        double value => "f64:" + BitConverter.DoubleToUInt64Bits(value).ToString("X16", CultureInfo.InvariantCulture),
        IFormattable value => value.ToString(null, CultureInfo.InvariantCulture),
        _ => operand?.ToString() ?? ""
    };
    static string MethodIdentity(MethodReference method) => $"{TypeIdentity(method.DeclaringType)}::{method.Name}:{method.CallingConvention}:{method.HasThis}:{method.ExplicitThis}:{TypeIdentity(method.ReturnType)}({string.Join(',', method.Parameters.Select(p => TypeIdentity(p.ParameterType)))})" +
        (method is GenericInstanceMethod generic ? "<" + string.Join(',', generic.GenericArguments.Select(TypeIdentity)) + ">" : "`" + method.GenericParameters.Count);
    static string TypeIdentity(TypeReference type)
    {
        if (type is FunctionPointerType pointer)
            return $"fnptr:{pointer.CallingConvention}:{pointer.HasThis}:{pointer.ExplicitThis}:{TypeIdentity(pointer.ReturnType)}({string.Join(',', pointer.Parameters.Select(p => TypeIdentity(p.ParameterType)))})";
        if (type is GenericParameter parameter) return $"{parameter.Type}:{parameter.Position}:{parameter.Owner}";
        if (type is GenericInstanceType instance) return TypeIdentity(instance.ElementType) + "<" + string.Join(',', instance.GenericArguments.Select(TypeIdentity)) + ">";
        if (type is RequiredModifierType required) return "modreq(" + TypeIdentity(required.ModifierType) + ")" + TypeIdentity(required.ElementType);
        if (type is OptionalModifierType optional) return "modopt(" + TypeIdentity(optional.ModifierType) + ")" + TypeIdentity(optional.ElementType);
        if (type is TypeSpecification specification) return type.FullName + ":" + TypeIdentity(specification.ElementType);
        string scope = type.Scope switch {
            AssemblyNameReference assembly => assembly.FullName,
            ModuleDefinition module => module.Assembly?.Name.FullName ?? module.Name,
            _ => type.Scope?.ToString() ?? ""
        };
        return "[" + scope + "]" + type.FullName;
    }
}
