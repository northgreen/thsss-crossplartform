using GameplayMath;
using Mono.Cecil;
using Mono.Cecil.Cil;
using static TestSupport;

internal static class Tests
{
    static int passed;
    static void Check(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    public static void Run()
    {
        Check("production fixture roundtrip, generic, aliasing, EH, vectors, callbacks", FixtureRoundtrip);
        Check("explicit hierarchy and generated presentation scope", Scope);
        Check("environment camera/draw contracts excluded while simulation Ctrl remains eligible", EnvironmentScope);
        Check("semantic fingerprints include locals, EH and raw constant bits", Fingerprints);
        Check("unmanaged function-pointer fingerprints preserve full signature and convention", FunctionPointers);
        Check("integer arithmetic and checked integer conversions untouched", Integer);
        Check("all ordered/unordered branches execute with NaN and infinities", Branches);
        Check("raw IEEE constant bits survive serialization", Constants);
        Check("floating truth, calli, malformed merge fail with method and offset", Unsupported);
        Check("extended Vx/Vy channels and late component spill preserved", ProfileBoundaries);
        Check("Debug getter return slots normalize without weakening unknown-body rejection", DebugGetters);
        Check("conversion opcode signedness, checkedness and native width", Conversions);
        Check("PDB sequence points and EH boundary identities retained", Symbols);
        Check("generated callback shared with drawing requires review", SharedCallback);
        Check("runtime signature mismatch fails at IL offset", InvalidRuntime);
        Check("compound property/indexer receiver evaluation and RNG order", SideEffects);
        Check("unbound generic and unsafe floating prefix contracts fail", UnsafeBoundaries);
        Console.WriteLine($"GAMEPLAY_WEAVER_TESTS_PASS={passed}; mechanism fixtures, not full-game/x87 parity");
    }
    static void FixtureRoundtrip()
    {
        using var module = Fresh();
        var policy = new ScopePolicy(module);
        var excluded = ScopePolicy.AllTypes(module).SelectMany(t => t.Methods).Where(m => !policy.Decide(m).Eligible).ToDictionary(m => m.FullName, SemanticFingerprint.Of);
        var manifest = RewritePass.Execute(module);
        Require(manifest.Success && manifest.Methods.Any(m => m.RandomBefore.Length == 2), "No RNG sequence audit");
        Require(manifest.Methods.All(m => m.RandomBefore.SequenceEqual(m.RandomAfter)), "RNG calls removed/reordered");
        Require((float)Invoke(module, "Shooting.BaseObject", "AddressLocal")! == 34f, "Floating local alias broken");
        Require((float)Invoke(module, "Shooting.BaseObject", "Generic")! == 6f, "Generic float boundary");
        Require((float)Invoke(module, "Shooting.BaseObject", "GenericField")! == 5f, "Closed generic float field boundary");
        Require((float)Invoke(module, "Shooting.BaseObject", "GenericReference")! == 8f, "Generic float byref boundary");
        Require((float)Invoke(module, "Shooting.BaseObject", "GenericContainers")! == 6f, "Generic container/enumerator boundary");
        Require((float)Invoke(module, "Shooting.BaseObject", "Boxed")! == 3f, "Box boundary");
        Require((double)Invoke(module, "Shooting.BaseObject", "Handler", false)! == 6d, "finally path");
        Require((double)Invoke(module, "Shooting.BaseObject", "Handler", true)! == 8d, "catch/finally path");
        Require((float)Invoke(module, "Shooting.BaseObject", "Vectors")! == 7.5f, "Vector redirect");
        Require((double)Invoke(module, "Shooting.EventGroup", "Twopointangle", 1d)! == Math.Atan(1), "Atan redirect");
        var alias = module.GetType("Shooting.BaseObject").Methods.Single(m => m.Name == "AddressLocal");
        Require(alias.Body.Variables.Any(v => v.VariableType.MetadataType == MetadataType.Single), "Address-taken storage erased");
        object?[] values = [new float[] { 2 }, 3f, 4f];
        Require((float)Invoke(module, "Shooting.BaseObject", "ArrayStorage", values)! == 9f && (float)values[1]! == 9f, "Array/byref boundary");
        using var stream = new MemoryStream(); module.Write(stream); stream.Position = 0;
        using var written = ModuleDefinition.ReadModule(stream);
        foreach (var method in ScopePolicy.AllTypes(written).SelectMany(t => t.Methods).Where(m => excluded.ContainsKey(m.FullName)))
            Require(SemanticFingerprint.Of(method) == excluded[method.FullName], "Excluded semantic drift " + method.FullName);
        Require(!written.AssemblyReferences.Any(r => r.Name.Contains("GameplayMath") || r.Name == "ProofMath"), "Injected helper assembly reference");
    }
    static void Scope()
    {
        using var module = Fresh(); var policy = new ScopePolicy(module);
        MethodDefinition Find(string owner, string name) => module.GetType("Shooting." + owner).Methods.Single(m => m.Name == name);
        Require(policy.Decide(Find("VisualObject", "Ctrl")).Eligible, "Visual Ctrl consumes shared RNG");
        Require(policy.Decide(Find("DerivedStage", "Move")).Eligible, "Derived stage excluded");
        Require(!policy.Decide(Find("WindowInputBridge", "Axis")).Eligible, "Input normalization selected");
        Require(!policy.Decide(Find("BaseObject", "Draw")).Eligible, "Drawing selected");
        var callbacks = ScopePolicy.AllTypes(module).SelectMany(t => t.Methods).Where(m => m.Name.Contains("b__", StringComparison.Ordinal)).ToArray();
        Require(callbacks.Any(m => policy.Decide(m).Eligible), "Simulation callback missing");
        Require(callbacks.Any(m => !policy.Decide(m).Eligible && policy.Decide(m).Reason.Contains("presentation", StringComparison.Ordinal)), "Presentation callback selected");
        Require(!new ScopePolicy(module, true).Decide(Find("MyRandom", "NextPMDouble")).Eligible, "Owned random not excluded");
    }
    static void EnvironmentScope()
    {
        using var module = Fresh();
        var contract = new TypeDefinition("Shooting", "IEnvironment", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        module.Types.Add(contract);
        var environment = new TypeDefinition("Shooting", "EnvironmentScopeFixture", TypeAttributes.Public | TypeAttributes.Class, module.GetType("Shooting.BaseObject"));
        environment.Interfaces.Add(new InterfaceImplementation(contract)); module.Types.Add(environment);
        foreach (string name in new[] { "get_CameraAngle", "get_CameraPos", "SetRenderPara", "Ctrl" })
        {
            var method = new MethodDefinition(name, MethodAttributes.Public, module.TypeSystem.Void);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); environment.Methods.Add(method);
        }
        var policy = new ScopePolicy(module);
        foreach (var method in environment.Methods)
            Require(policy.Decide(method).Eligible == (method.Name == "Ctrl"), "Wrong environment boundary: " + method.Name);
    }
    static void Fingerprints()
    {
        using var module = Synthetic(out var method); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_R8, -0d); il.Emit(OpCodes.Ret);
        var before = SemanticFingerprint.Of(method); method.Body.Instructions[0].Operand = 0d;
        Require(before != SemanticFingerprint.Of(method), "Negative-zero bits omitted");
        before = SemanticFingerprint.Of(method); method.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Single));
        Require(before != SemanticFingerprint.Of(method), "Local types omitted");
    }
    static void FunctionPointers()
    {
        using var module = Synthetic(out var method, "int");
        var pointer = new FunctionPointerType { ReturnType = module.TypeSystem.Void, CallingConvention = MethodCallingConvention.StdCall };
        pointer.Parameters.Add(new ParameterDefinition(module.TypeSystem.IntPtr));
        method.Body.Variables.Add(new VariableDefinition(pointer));
        method.Body.GetILProcessor().Emit(OpCodes.Ldc_I4_0); method.Body.GetILProcessor().Emit(OpCodes.Ret);
        var before = SemanticFingerprint.Of(method);
        pointer.CallingConvention = MethodCallingConvention.C;
        Require(before != SemanticFingerprint.Of(method), "Function-pointer calling convention omitted");
        before = SemanticFingerprint.Of(method); pointer.Parameters[0].ParameterType = module.TypeSystem.Int64;
        Require(before != SemanticFingerprint.Of(method), "Function-pointer parameter identity omitted");
        before = SemanticFingerprint.Of(method); pointer.ReturnType = module.TypeSystem.Double;
        Require(before != SemanticFingerprint.Of(method), "Function-pointer return type omitted");
        using var stream = new MemoryStream(); module.Write(stream); stream.Position = 0;
        using var written = ModuleDefinition.ReadModule(stream);
        var copy = written.GetType("Shooting.Execution").Methods.Single(m => m.Name == method.Name);
        Require(SemanticFingerprint.Of(copy) == SemanticFingerprint.Of(method), "Function-pointer identity changed on serialization");
    }
    static void Integer()
    {
        using var module = Synthetic(out var method, "int"); var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldc_I4, 42); il.Emit(OpCodes.Ldc_I4_3); il.Emit(OpCodes.Add); il.Emit(OpCodes.Conv_Ovf_U1); il.Emit(OpCodes.Neg); il.Emit(OpCodes.Ret);
        var before = SemanticFingerprint.Of(method); RewritePass.Execute(module);
        Require(SemanticFingerprint.Of(method) == before && (int)Invoke(module, "Shooting.Execution", "Probe")! == -45, "Integer IL changed");
    }
    static void Branches()
    {
        foreach (var opcode in new[] { OpCodes.Beq, OpCodes.Bne_Un, OpCodes.Bgt, OpCodes.Bgt_Un, OpCodes.Blt, OpCodes.Blt_Un, OpCodes.Bge, OpCodes.Bge_Un, OpCodes.Ble, OpCodes.Ble_Un })
        {
            using var module = Synthetic(out var method, "int"); method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Double)); method.Parameters.Add(new ParameterDefinition(module.TypeSystem.Double));
            var il = method.Body.GetILProcessor(); var yes = il.Create(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(opcode, yes); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ret); il.Append(yes); il.Emit(OpCodes.Ret);
            RewritePass.Execute(module);
            foreach (double a in new[] { double.NaN, double.NegativeInfinity, -double.Epsilon, -0d, 0d, double.Epsilon, double.PositiveInfinity })
            foreach (double b in new[] { double.NaN, double.NegativeInfinity, -1d, 0d, double.Epsilon, double.PositiveInfinity })
            {
                bool unordered = double.IsNaN(a) || double.IsNaN(b);
                bool expected = opcode.Code switch {
                    Code.Beq => a == b, Code.Bne_Un => a != b, Code.Bgt => a > b, Code.Bgt_Un => unordered || a > b,
                    Code.Blt => a < b, Code.Blt_Un => unordered || a < b, Code.Bge => a >= b, Code.Bge_Un => unordered || a >= b,
                    Code.Ble => a <= b, Code.Ble_Un => unordered || a <= b, _ => throw new InvalidOperationException()
                };
                Require(((int)Invoke(module, "Shooting.Execution", "Probe", a, b)! != 0) == expected, "Unordered branch " + opcode.Name);
            }
        }
    }
    static void Constants()
    {
        foreach (ulong bits in new[] { 0x8000000000000000UL, 0x7ff8123456789abcUL, 1UL, 0x7ff0000000000000UL })
        {
            using var module = Synthetic(out var method); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_R8, BitConverter.UInt64BitsToDouble(bits)); il.Emit(OpCodes.Ret);
            RewritePass.Execute(module); Require(BitConverter.DoubleToUInt64Bits((double)Invoke(module, "Shooting.Execution", "Probe")!) == bits, "Constant bits changed");
        }
    }
    static void Unsupported()
    {
        using (var module = Synthetic(out var method))
        {
            var il = method.Body.GetILProcessor(); var target = il.Create(OpCodes.Ldc_R8, 1d);
            il.Emit(OpCodes.Ldc_R8, 2d); il.Emit(OpCodes.Brtrue, target); il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Ret); il.Append(target); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "IL_0000 brtrue: Floating truth");
        }
        using (var module = Synthetic(out var method))
        {
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Calli, new CallSite(module.TypeSystem.Void))); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R8, 0d)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            Reject(() => RewritePass.Execute(module), "calli: Opcode has no supported");
        }
        using (var module = Synthetic(out var method))
        {
            var il = method.Body.GetILProcessor(); var other = il.Create(OpCodes.Ldc_R8, 1d); var join = il.Create(OpCodes.Pop);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Brtrue, other); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Br, join); il.Append(other); il.Append(join); il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "Unsupported merge");
        }
    }
    static void ProfileBoundaries()
    {
        using var module = Fresh(); RewritePass.Execute(module); var owner = module.GetType("Shooting.BaseObject");
        foreach (var axis in new[] { "Vx", "Vy" })
        {
            var shadow = owner.Methods.Single(m => m.Name == "__LegacyExtended_get_" + axis);
            Require(shadow.IsAssembly && shadow.ReturnType.FullName == "SlimDX.Compat.LegacyExtended", "Extended getter channel not accessible to derived gameplay");
            Require(shadow.Body.Instructions[^2].Operand is MethodReference last && last.Name == "Mul", "Shadow return acquired a float store");
            var instructions = owner.Methods.Single(m => m.Name == "set_" + axis).Body.Instructions;
            int velocity = instructions.ToList().FindIndex(i => i.Operand is MethodReference call && call.Name == "set_Velocity");
            int squareRoot = instructions.ToList().FindIndex(i => i.Operand is MethodReference call && call.Name == "Sqrt");
            var narrowings = instructions.Select((i, index) => (i, index)).Where(x => x.index > squareRoot && x.index < velocity && x.i.Operand is MethodReference call && call.Name == "NarrowSingle").ToArray();
            Require(narrowings.Length >= 1 && instructions[narrowings[^1].index + 1].OpCode.Code == Code.Stloc && ((VariableDefinition)instructions[narrowings[^1].index + 1].Operand).Index == 0, "Late component FSTPS moved/lost");
            Require(instructions.Any(i => i.Operand is MethodReference call && call.Name.StartsWith("__LegacyExtended_", StringComparison.Ordinal)), "Setter still truncates public getter");
        }
    }
    static void DebugGetters()
    {
        using (var module = Fresh())
        {
            var owner = module.GetType("Shooting.BaseObject");
            foreach (var getter in owner.Methods.Where(m => m.Name is "get_Vx" or "get_Vy"))
            {
                Require(getter.Body.Variables.Count == 0, "Expected original straight-line fixture getter");
                var local = new VariableDefinition(module.TypeSystem.Single);
                getter.Body.Variables.Add(local); getter.Body.InitLocals = true;
                var il = getter.Body.GetILProcessor();
                var ret = getter.Body.Instructions.Last(i => i.OpCode.Code == Code.Ret);
                var load = il.Create(OpCodes.Ldloc_0);
                var join = il.Create(OpCodes.Nop);
                il.InsertBefore(ret, il.Create(OpCodes.Stloc_0));
                il.InsertBefore(ret, il.Create(OpCodes.Br_S, join));
                il.InsertBefore(ret, join);
                il.InsertBefore(ret, load);
                il.InsertBefore(getter.Body.Instructions[0], il.Create(OpCodes.Nop));
            }
            RewritePass.Execute(module);
            foreach (var shadow in owner.Methods.Where(m => m.Name.StartsWith("__LegacyExtended_", StringComparison.Ordinal)))
            {
                Require(shadow.Body.Variables.All(v => v.VariableType.MetadataType != MetadataType.Single), "Debug return slot reintroduced float storage");
                Require(shadow.Body.Instructions[^2].Operand is MethodReference call && call.Name == "Mul", "Debug shadow acquired a return narrowing");
            }
        }
        using (var module = Fresh())
        {
            var getter = module.GetType("Shooting.BaseObject").Methods.Single(m => m.Name == "get_Vx");
            getter.Body.Variables.Add(new VariableDefinition(module.TypeSystem.Single));
            Reject(() => RewritePass.Execute(module), "Unreviewed extended-return getter body");
        }
    }
    static void Conversions()
    {
        foreach (var (opcode, helper) in new[] { (OpCodes.Conv_U1, "ToByte"), (OpCodes.Conv_I1, "ToSByte"), (OpCodes.Conv_I2, "ToInt16"), (OpCodes.Conv_U2, "ToUInt16"), (OpCodes.Conv_Ovf_I4, "ToInt32Checked"), (OpCodes.Conv_Ovf_U8, "ToUInt64Checked") })
        {
            using var module = Synthetic(out var method, "int"); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_R8, 4d); il.Emit(opcode); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ret);
            RewritePass.Execute(module); Require(method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == helper), "Wrong conversion helper " + helper);
        }
        using (var module = Synthetic(out var method))
        {
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Conv_R8); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "TargetPointerSize");
        }
        using (var module = Synthetic(out var method))
        {
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Conv_R_Un); il.Emit(OpCodes.Ret);
            RewritePass.Execute(module, new(TargetPointerSize: 8)); Require(method.Body.Instructions.Any(i => i.Operand is MethodReference call && call.Name == "FromUInt64"), "Unsigned native integer conversion lost");
        }
        using (var module = Synthetic(out var method, "int"))
        {
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_R8, 4d); il.Emit(OpCodes.Conv_Ovf_I4_Un); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "Unverified floating conv.ovf.*.un");
        }
    }
    static void Symbols()
    {
        using var module = ModuleDefinition.ReadModule(Fixture, new ReaderParameters { ReadSymbols = true });
        var method = module.GetType("Shooting.BaseObject").Methods.Single(m => m.Name == "Handler");
        var boundaries = method.Body.ExceptionHandlers.Select(h => (h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd)).ToArray();
        var points = method.DebugInformation.SequencePoints.Select(p => (p.StartLine, p.EndLine, p.Document.Url)).ToArray();
        RewritePass.Execute(module);
        Require(boundaries.SequenceEqual(method.Body.ExceptionHandlers.Select(h => (h.TryStart, h.TryEnd, h.HandlerStart, h.HandlerEnd))), "EH target identities changed");
        using var assembly = new MemoryStream(); using var symbols = new MemoryStream();
        module.Write(assembly, new WriterParameters { WriteSymbols = true, SymbolStream = symbols, SymbolWriterProvider = new PortablePdbWriterProvider() }); assembly.Position = 0; symbols.Position = 0;
        using var written = ModuleDefinition.ReadModule(assembly, new ReaderParameters { ReadSymbols = true, SymbolStream = symbols, SymbolReaderProvider = new PortablePdbReaderProvider() });
        var actual = written.GetType("Shooting.BaseObject").Methods.Single(m => m.Name == "Handler").DebugInformation.SequencePoints;
        Require(points.Length != 0 && points.SequenceEqual(actual.Select(p => (p.StartLine, p.EndLine, p.Document.Url))), "PDB source mappings lost");
    }
    static void SharedCallback()
    {
        using var module = Synthetic(out var method); var owner = method.DeclaringType;
        var callback = new MethodDefinition("<Probe>b__0", MethodAttributes.Static | MethodAttributes.Private, module.TypeSystem.Double); owner.Methods.Add(callback);
        callback.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_R8, 1d)); callback.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Call, callback); il.Emit(OpCodes.Ret);
        var draw = new MethodDefinition("Draw", MethodAttributes.Static | MethodAttributes.Public, module.TypeSystem.Double); owner.Methods.Add(draw);
        draw.Body.Instructions.Add(Instruction.Create(OpCodes.Call, callback)); draw.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        Reject(() => RewritePass.Execute(module), "shared by simulation and presentation");
    }
    static void InvalidRuntime()
    {
        using var module = Synthetic(out var method); var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_R8, 1d); il.Emit(OpCodes.Ret);
        module.GetType("SlimDX.Compat.LegacyFloat").Methods.Single(m => m.Name == "FromDouble").ReturnType = module.TypeSystem.Int32;
        Reject(() => RewritePass.Execute(module), "IL_0000 ldc.r8: Incorrect runtime helper signature");
    }
    static void SideEffects()
    {
        using var original = Fresh(); using var woven = Fresh(); RewritePass.Execute(woven);
        var expected = (string)Invoke(original, "Shooting.CounterOwner", "Compound")!;
        Require(expected == "2,1,2,2,2,5,12", "Unexpected counter fixture " + expected);
        Require((string)Invoke(woven, "Shooting.CounterOwner", "Compound")! == expected, "Receiver/indexer evaluated twice");
        var a = new Random(123); var b = new Random(123);
        for (int frame = 0; frame < 100; frame++)
            Require((double)Invoke(original, "Shooting.VisualObject", "Ctrl", a)! == (double)Invoke(woven, "Shooting.VisualObject", "Ctrl", b)!, "RNG evaluation reordered");
        Require(a.Next() == b.Next(), "Visual Ctrl changed shared random consumption");
    }
    static void UnsafeBoundaries()
    {
        using (var module = Synthetic(out var method))
        {
            var call = new MethodReference("Identity", module.TypeSystem.Void, method.DeclaringType);
            var parameter = new GenericParameter("T", call); call.GenericParameters.Add(parameter); call.ReturnType = parameter;
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Call, call); il.Emit(OpCodes.Pop); il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "Unbound generic call boundary");
        }
        using (var module = Synthetic(out var method))
        {
            method.Parameters.Add(new ParameterDefinition(new ByReferenceType(module.TypeSystem.Single)));
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_R4, 3f); il.Emit(OpCodes.Volatile); il.Emit(OpCodes.Stind_R4); il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "Storage prefix requires explicit relocation");
        }
        using (var module = Synthetic(out var method))
        {
            var call = new MethodReference("Vararg", module.TypeSystem.Void, method.DeclaringType) { CallingConvention = MethodCallingConvention.VarArg };
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Call, call); il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "Vararg call requires an explicit reviewed argument contract");
        }
        using (var module = Synthetic(out var method))
        {
            method.Body.Variables.Add(new VariableDefinition(new OptionalModifierType(module.TypeSystem.Object, module.TypeSystem.Single)));
            var il = method.Body.GetILProcessor(); il.Emit(OpCodes.Ldc_R8, 0d); il.Emit(OpCodes.Ret);
            Reject(() => RewritePass.Execute(module), "Unreviewed modified floating type boundary");
        }
    }
}
