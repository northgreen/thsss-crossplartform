using GameplayMath;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Runtime.Loader;

if (args.Length != 0)
{
    if (args.Length == 2 && args[0] == "--check-integration")
    {
        using var integrated = ModuleDefinition.ReadModule(args[1]);
        TestSupport.Require(integrated.GetType("Shooting.BaseObject").Methods.Count(m => m.Name.StartsWith("__LegacyExtended_", StringComparison.Ordinal)) == 2, "Fody MSBuild did not execute the production plugin");
        TestSupport.Require((float)TestSupport.Invoke(integrated, "Shooting.BaseObject", "AddressLocal")! == 34f, "Integrated alias bridge failed");
        TestSupport.Require((double)TestSupport.Invoke(integrated, "Shooting.BaseObject", "Handler", true)! == 8d, "Integrated EH execution failed");
        TestSupport.Require(!integrated.AssemblyReferences.Any(r => r.Name.Contains("GameplayMath") || r.Name == "ProofMath"), "Integrated helper DLL dependency");
        Console.WriteLine("FODY_MSBUILD_EXECUTION_PASS"); return;
    }
    if (args.Length != 4 || args[0] is not ("--audit" or "--weave")) throw new ArgumentException("--audit|--weave input.dll output.dll manifest.json");
    var resolver = new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
    using var module = ModuleDefinition.ReadModule(args[1], new ReaderParameters { AssemblyResolver = resolver, ReadSymbols = File.Exists(Path.ChangeExtension(args[1], ".pdb")) });
    int inputMethods = ScopePolicy.AllTypes(module).Sum(t => t.Methods.Count);
    bool signatureAdapter = args[0] == "--audit" && module.GetType("SlimDX.Compat.LegacyFloat") == null;
    if (signatureAdapter) TestSupport.AddRuntimeSignatures(module);
    int signatureMethods = ScopePolicy.AllTypes(module).Sum(t => t.Methods.Count) - inputMethods;
    var policy = new ScopePolicy(module, excludeOwnedRandom: true);
    var excluded = ScopePolicy.AllTypes(module).SelectMany(t => t.Methods).Where(m => !policy.Decide(m).Eligible).ToDictionary(m => m.FullName, SemanticFingerprint.Of);
    try
    {
        var manifest = RewritePass.Execute(module, new(ExcludeOwnedRandom: true));
        manifest.SignatureOnlyRuntimeAdapter = signatureAdapter; manifest.SignatureOnlyRuntimeMethodCount = signatureMethods; manifest.InputMethodCount = inputMethods; manifest.Write(args[3]);
        if (args[0] == "--weave")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
            module.Write(args[2], new WriterParameters { WriteSymbols = module.HasSymbols });
            using var output = ModuleDefinition.ReadModule(args[2]);
            foreach (var method in ScopePolicy.AllTypes(output).SelectMany(t => t.Methods).Where(m => excluded.ContainsKey(m.FullName)))
                TestSupport.Require(SemanticFingerprint.Of(method) == excluded[method.FullName], "Serialized excluded semantics changed: " + method.FullName);
        }
        Console.WriteLine($"MANIFEST_PASS inputMethods={inputMethods} reportMethods={manifest.Methods.Count} rewritten={manifest.Methods.Count(m => m.Action == "rewritten")} eligibleNumeric={manifest.Methods.Count(m => m.Scope.Eligible && m.Numeric.Length != 0)} outsideOriginalInputUnchanged={manifest.OutsideScopeUnchanged - signatureMethods}; audit={args[0] == "--audit"}; signatureOnlyRuntimeAdapter={signatureAdapter}; absentRoots={string.Join(',', manifest.MissingRoots)}");
    }
    catch (WeaveException error)
    {
        error.Manifest.SignatureOnlyRuntimeAdapter = signatureAdapter; error.Manifest.SignatureOnlyRuntimeMethodCount = signatureMethods; error.Manifest.InputMethodCount = inputMethods;
        error.Manifest.Write(args[3]);
        foreach (var detail in error.Manifest.Errors) Console.Error.WriteLine(detail);
        Console.Error.WriteLine($"MANIFEST_REJECTED errors={error.Manifest.Errors.Count}; report={args[3]}");
        Environment.ExitCode = 1;
    }
    return;
}

Tests.Run();

internal static class TestSupport
{
    public static string Fixture => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../Fixture/bin/Release/net8.0/Fixture.dll"));
    public static ModuleDefinition Fresh() => ModuleDefinition.ReadModule(Fixture);
    public static ModuleDefinition Synthetic(out MethodDefinition method, string result = "double")
    {
        var module = Fresh();
        foreach (var type in module.Types.Where(t => t.Namespace == "Shooting").ToArray()) module.Types.Remove(type);
        var owner = new TypeDefinition("Shooting", "Execution", TypeAttributes.Public, module.TypeSystem.Object); module.Types.Add(owner);
        method = new MethodDefinition("Probe", MethodAttributes.Public | MethodAttributes.Static, result == "int" ? module.TypeSystem.Int32 : module.TypeSystem.Double); owner.Methods.Add(method);
        return module;
    }
    public static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    public static void Reject(Action action, string expected)
    {
        try { action(); }
        catch (Exception error) when (error is WeaveException or NotSupportedException && error.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException("Expected rejection containing " + expected);
    }
    public static object? Invoke(ModuleDefinition module, string type, string method, params object?[] arguments)
    {
        using var stream = new MemoryStream(); module.Write(stream); stream.Position = 0;
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), true);
        try { return context.LoadFromStream(stream).GetType(type)!.GetMethod(method)!.Invoke(null, arguments); }
        finally { context.Unload(); }
    }
    public static void AddRuntimeSignatures(ModuleDefinition module)
    {
        using var fixture = Fresh();
        var extended = new TypeDefinition("SlimDX.Compat", "LegacyExtended", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.SequentialLayout, module.ImportReference(typeof(ValueType))) { ClassSize = 16, PackingSize = 0 };
        module.Types.Add(extended);
        var runtime = new TypeDefinition("SlimDX.Compat", "LegacyFloat", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, module.TypeSystem.Object); module.Types.Add(runtime);
        TypeReference Map(TypeReference type) => type is ByReferenceType byref ? new ByReferenceType(Map(byref.ElementType)) :
            type.FullName == extended.FullName ? extended : module.GetType(type.FullName) ?? module.ImportReference(type);
        foreach (var source in fixture.GetType(runtime.FullName).Methods.Where(m => m.IsPublic && m.IsStatic))
        {
            var target = new MethodDefinition(source.Name, MethodAttributes.Public | MethodAttributes.Static, Map(source.ReturnType));
            foreach (var parameter in source.Parameters) target.Parameters.Add(new ParameterDefinition(Map(parameter.ParameterType)));
            target.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull)); target.Body.Instructions.Add(Instruction.Create(OpCodes.Throw)); runtime.Methods.Add(target);
        }
    }
}
