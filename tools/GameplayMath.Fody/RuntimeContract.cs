using Mono.Cecil;

namespace GameplayMath;

public sealed class RuntimeContract
{
    readonly ModuleDefinition module;
    readonly TypeDefinition runtime;
    public TypeReference Extended { get; }
    public RuntimeContract(ModuleDefinition module)
    {
        this.module = module;
        Extended = module.GetType("SlimDX.Compat.LegacyExtended") ?? throw new InvalidOperationException("Game assembly must define SlimDX.Compat.LegacyExtended");
        runtime = module.GetType("SlimDX.Compat.LegacyFloat") ?? throw new InvalidOperationException("Game assembly must define SlimDX.Compat.LegacyFloat");
        var definition = (TypeDefinition)Extended;
        if (!definition.IsValueType || definition.ClassSize != 16)
            throw new InvalidOperationException("LegacyExtended must be a 16-byte value type with explicit/sequential layout size");
    }
    public MethodReference Import(string name)
    {
        var methods = runtime.Methods.Where(m => m.Name == name && m.IsPublic && m.IsStatic).ToArray();
        if (methods.Length != 1) throw new NotSupportedException($"Runtime contract requires exactly one public static LegacyFloat.{name}; found {methods.Length}");
        var method = methods[0];
        var ext = Extended.FullName;
        string conversionName = name.EndsWith("Checked", StringComparison.Ordinal) ? name[..^7] : name;
        var conversionType = conversionName switch {
            "ToByte" => "System.Byte", "ToSByte" => "System.SByte", "ToInt16" => "System.Int16", "ToUInt16" => "System.UInt16",
            "ToInt32" => "System.Int32", "ToUInt32" => "System.UInt32", "ToInt64" => "System.Int64", "ToUInt64" => "System.UInt64", _ => null
        };
        var expected = conversionType != null ? (conversionType, new[] { ext }) : name switch {
            "FromSingle" => (ext, new[] { "System.Single" }), "FromDouble" => (ext, new[] { "System.Double" }),
            "FromInt32" => (ext, new[] { "System.Int32" }), "FromUInt32" => (ext, new[] { "System.UInt32" }),
            "FromInt64" => (ext, new[] { "System.Int64" }), "FromUInt64" => (ext, new[] { "System.UInt64" }),
            "ToSingle" => ("System.Single", new[] { ext }), "ToDouble" => ("System.Double", new[] { ext }),
            "ToInt32" => ("System.Int32", new[] { ext }), "ToUInt32" => ("System.UInt32", new[] { ext }),
            "ToInt64" => ("System.Int64", new[] { ext }), "ToUInt64" => ("System.UInt64", new[] { ext }),
            "ToByte" => ("System.Byte", new[] { ext }),
            "NarrowSingle" or "NarrowDouble" or "Sin" or "Cos" or "Atan" or "Sqrt" or "Neg" or "AbsSingle" or "AbsDouble" => (ext, new[] { ext }),
            "Add" or "Sub" or "Mul" or "Div" or "Rem" or "Atan2" => (ext, new[] { ext, ext }),
            "Equal" or "NotEqual" or "Greater" or "GreaterUn" or "Less" or "LessUn" or "GreaterEqual" or "GreaterEqualUn" or "LessEqual" or "LessEqualUn" => ("System.Boolean", new[] { ext, ext }),
            "Vector2Length" => (ext, new[] { "SlimDX.Vector2&" }),
            "Vector2Normalize" => ("System.Void", new[] { "SlimDX.Vector2&" }),
            "Vector2Add" or "Vector2Subtract" => ("SlimDX.Vector2", new[] { "SlimDX.Vector2", "SlimDX.Vector2" }),
            "Vector2Multiply" => ("SlimDX.Vector2", new[] { "SlimDX.Vector2", "System.Single" }),
            "Vector2Scale" => ("SlimDX.Vector2", new[] { "System.Single", "SlimDX.Vector2" }),
            "Vector2Lerp" => ("SlimDX.Vector2", new[] { "SlimDX.Vector2", "SlimDX.Vector2", "System.Single" }),
            _ => throw new NotSupportedException("Unreviewed runtime export " + name)
        };
        if (method.ReturnType.FullName != expected.Item1 || !method.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(expected.Item2))
            throw new NotSupportedException("Incorrect runtime helper signature: " + method.FullName);
        return module.ImportReference(method);
    }
}
