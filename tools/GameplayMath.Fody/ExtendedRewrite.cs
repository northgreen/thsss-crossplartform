using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GameplayMath;

sealed class ExtendedRewrite(MethodDefinition method, RuntimeContract runtime, StackAnalysis analysis,
    Dictionary<string, MethodDefinition> shadows, int pointerSize)
{
    ModuleDefinition Module => method.Module;
    TypeReference Extended => runtime.Extended;
    readonly Dictionary<VariableDefinition, TypeReference> locals = method.Body.Variables.ToDictionary(v => v, v => v.VariableType);
    readonly HashSet<VariableDefinition> addressed = method.Body.Instructions.Where(i => i.OpCode.Code is Code.Ldloca or Code.Ldloca_S).Select(i => (VariableDefinition)i.Operand).ToHashSet();
    readonly List<Instruction> emitted = new();
    Instruction current = null!;
    string[] stack = [];
    static bool Float(TypeReference type) => StackAnalysis.Floating(StackAnalysis.Type(type));
    Exception Error(string reason) => new NotSupportedException($"{method.FullName} IL_{current.Offset:X4} {current.OpCode}: {reason}");
    void Emit(OpCode opcode) => emitted.Add(Instruction.Create(opcode));
    void Emit(OpCode opcode, MethodReference target) => emitted.Add(Instruction.Create(opcode, target));
    void Emit(OpCode opcode, VariableDefinition local) => emitted.Add(Instruction.Create(opcode, local));
    MethodReference Resolve(string name)
    {
        try { return runtime.Import(name); }
        catch (NotSupportedException error) { throw Error(error.Message); }
    }
    void Call(string name) => Emit(OpCodes.Call, Resolve(name));
    void Load(TypeReference type) => Call(type.MetadataType == MetadataType.Single ? "FromSingle" : "FromDouble");
    void Store(TypeReference type) => Call(type.MetadataType == MetadataType.Single ? "ToSingle" : "ToDouble");
    VariableDefinition Temp(TypeReference type) { var variable = new VariableDefinition(type); method.Body.Variables.Add(variable); return variable; }
    void Original() => emitted.Add(Clone(current));
    static Instruction Clone(Instruction instruction) { var copy = Instruction.Create(OpCodes.Nop); copy.OpCode = instruction.OpCode; copy.Operand = instruction.Operand; return copy; }
    public void Run()
    {
        foreach (var local in locals.Keys.Where(v => Float(v.VariableType) && !addressed.Contains(v))) local.VariableType = Extended;
        foreach (var instruction in method.Body.Instructions.ToArray())
        {
            current = instruction; stack = analysis.Before[instruction]; emitted.Clear();
            Rewrite();
            if (emitted.Count == 0) throw Error("Empty instruction replacement");
            if (instruction.Previous?.OpCode.Code is Code.Volatile or Code.Unaligned && emitted[0].OpCode != instruction.OpCode)
                throw Error("Storage prefix requires explicit relocation across the floating bridge");
            // Keep instruction identity: branch/EH boundaries and sequence points still refer to it.
            instruction.OpCode = emitted[0].OpCode; instruction.Operand = emitted[0].Operand;
            var cursor = instruction;
            foreach (var added in emitted.Skip(1)) { method.Body.GetILProcessor().InsertAfter(cursor, added); cursor = added; }
        }
        foreach (var instruction in method.Body.Instructions) ExpandBranch(instruction);
        var check = new StackAnalysis(method); check.Run();
        method.Body.MaxStackSize = Math.Max(1, check.Before.Values.Max(s => s.Length) + 1);
    }
    VariableDefinition Local() => current.Operand is VariableDefinition variable ? variable : method.Body.Variables[(int)current.OpCode.Code - (int)(current.OpCode.Code is >= Code.Stloc_0 and <= Code.Stloc_3 ? Code.Stloc_0 : Code.Ldloc_0)];
    TypeReference Argument()
    {
        if (current.Operand is ParameterDefinition parameter) return parameter.ParameterType;
        int index = (int)current.OpCode.Code - (int)Code.Ldarg_0;
        return index == 0 && method.HasThis ? method.DeclaringType : method.Parameters[index - (method.HasThis ? 1 : 0)].ParameterType;
    }
    void Rewrite()
    {
        var code = current.OpCode.Code;
        if (code is Code.Call or Code.Callvirt or Code.Newobj) { RewriteCall((MethodReference)current.Operand); return; }
        if (code is Code.Add or Code.Sub or Code.Mul or Code.Div or Code.Rem or Code.Neg && StackAnalysis.Floating(stack[^1])) { Call(code.ToString()); return; }
        if (ComparisonName(code) is string comparison && stack.Length >= 2 && (StackAnalysis.Floating(stack[^1]) || StackAnalysis.Floating(stack[^2])))
        {
            if (!StackAnalysis.Floating(stack[^1]) || !StackAnalysis.Floating(stack[^2])) throw Error("Mixed non-floating comparison");
            Call(comparison);
            if (current.Operand is Instruction target) emitted.Add(Instruction.Create(OpCodes.Brtrue, target));
            return;
        }
        if (code.ToString().StartsWith("Conv_", StringComparison.Ordinal)) { Conversion(code); return; }
        switch (code)
        {
            case Code.Ldc_R4: Original(); Load(Module.TypeSystem.Single); return;
            case Code.Ldc_R8: Original(); Load(Module.TypeSystem.Double); return;
            case Code.Ldarg_0: case Code.Ldarg_1: case Code.Ldarg_2: case Code.Ldarg_3: case Code.Ldarg: case Code.Ldarg_S:
                Original(); if (Float(Argument())) Load(Argument()); return;
            case Code.Starg: case Code.Starg_S: if (Float(Argument())) Store(Argument()); Original(); return;
            case Code.Ldloc: case Code.Ldloc_S: case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
                Original(); if (addressed.Contains(Local()) && Float(locals[Local()])) Load(locals[Local()]); return;
            case Code.Stloc: case Code.Stloc_S: case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3:
                if (addressed.Contains(Local()) && Float(locals[Local()])) Store(locals[Local()]); Original(); return;
            case Code.Ldfld: case Code.Ldsfld:
                Original(); var field = StackAnalysis.FieldType((FieldReference)current.Operand); if (Float(field)) Load(field); return;
            case Code.Stfld: case Code.Stsfld:
                var stored = StackAnalysis.FieldType((FieldReference)current.Operand); if (Float(stored)) Store(stored); Original(); return;
            case Code.Ldelem_R4: case Code.Ldind_R4: Original(); Load(Module.TypeSystem.Single); return;
            case Code.Ldelem_R8: case Code.Ldind_R8: Original(); Load(Module.TypeSystem.Double); return;
            case Code.Stelem_R4: case Code.Stind_R4: Store(Module.TypeSystem.Single); Original(); return;
            case Code.Stelem_R8: case Code.Stind_R8: Store(Module.TypeSystem.Double); Original(); return;
            case Code.Ldobj: case Code.Ldelem_Any: case Code.Unbox_Any:
                Original(); if (Float((TypeReference)current.Operand)) Load((TypeReference)current.Operand); return;
            case Code.Stobj: case Code.Stelem_Any: case Code.Box:
                if (Float((TypeReference)current.Operand)) Store((TypeReference)current.Operand); Original(); return;
            case Code.Ret: if (Float(method.ReturnType)) Store(method.ReturnType); Original(); return;
            default: Original(); return;
        }
    }
    void RewriteCall(MethodReference call)
    {
        if (current.Previous?.OpCode.Code == Code.Tail && (Float(call.ReturnType) || call.Parameters.Any(p => Float(p.ParameterType))))
            throw Error("Floating tail call requires an explicit return-channel contract");
        if (shadows.TryGetValue(call.FullName, out var shadow)) { Emit(current.OpCode, shadow); return; }
        if (call.DeclaringType.FullName == "System.Math" && call.Name is "Sin" or "Cos" or "Atan" or "Atan2" or "Sqrt") { Call(call.Name); return; }
        if (call.DeclaringType.FullName == "System.Math" && call.Name == "Abs" && Float(call.ReturnType)) { Call(call.ReturnType.MetadataType == MetadataType.Single ? "AbsSingle" : "AbsDouble"); return; }
        if (call.DeclaringType.FullName == "System.Math" && Float(call.ReturnType)) throw Error("Unsupported Math intrinsic: " + call.FullName);
        if (call.DeclaringType.FullName == "SlimDX.Vector2" && call.Name is not (".ctor" or "get_Zero"))
        {
            string? helper = call.Name switch {
                "Length" when call.HasThis && call.Parameters.Count == 0 => "Vector2Length",
                "Normalize" when call.HasThis && call.Parameters.Count == 0 => "Vector2Normalize",
                "op_Addition" => "Vector2Add", "op_Subtraction" => "Vector2Subtract",
                "op_Multiply" when call.Parameters.Count == 2 && call.Parameters[0].ParameterType.FullName == "System.Single" => "Vector2Scale",
                "op_Multiply" => "Vector2Multiply", "Lerp" => "Vector2Lerp", _ => null
            };
            if (helper == null) throw Error("Unreviewed Vector2 boundary: " + call.FullName);
            var replacement = Resolve(helper);
            var sourceTypes = call.Parameters.Select(p => StackAnalysis.Bind(p.ParameterType, call)).ToList();
            if (call.HasThis) sourceTypes.Insert(0, new ByReferenceType(call.DeclaringType));
            if (!sourceTypes.Select(t => t.FullName).SequenceEqual(replacement.Parameters.Select(p => p.ParameterType.FullName)) ||
                (helper == "Vector2Length" ? call.ReturnType.MetadataType != MetadataType.Single : call.ReturnType.FullName != replacement.ReturnType.FullName))
                throw Error("Unreviewed Vector2 overload: " + call.FullName);
            if (current.Previous?.OpCode.Code == Code.Constrained) throw Error("Constrained Vector2 receiver requires explicit prefix relocation");
            if (call.HasThis && !stack[^(call.Parameters.Count + 1)].EndsWith('&')) throw Error("Vector2 receiver must be a managed reference");
            Spill(replacement.Parameters.Select(p => p.ParameterType).ToArray());
            Emit(OpCodes.Call, replacement); return;
        }
        TypeReference[] parameters;
        try { parameters = call.Parameters.Select(p => StackAnalysis.Bind(p.ParameterType, call)).ToArray(); }
        catch (NotSupportedException error) { throw Error(error.Message); }
        // Actual cold/invoked CLR2 JIT: FSTPS component AFTER sqrt, BEFORE set_Velocity.
        if (method.DeclaringType.FullName == "Shooting.BaseObject" && method.Name is "set_Vx" or "set_Vy" && call.Name == "set_Velocity")
        {
            var component = locals.Keys.SingleOrDefault(v => v.Index == 0 && locals[v].MetadataType == MetadataType.Single);
            if (component == null || addressed.Contains(component)) throw Error("Unreviewed component spill profile");
            Emit(OpCodes.Ldloc, component); Call("NarrowSingle"); Emit(OpCodes.Stloc, component);
        }
        Spill(parameters);
        Original();
        if (current.OpCode.Code != Code.Newobj && Float(StackAnalysis.Bind(call.ReturnType, call))) Load(StackAnalysis.Bind(call.ReturnType, call));
    }
    void Spill(TypeReference[] parameters)
    {
        if (!parameters.Any(Float)) return;
        if (current.Previous?.OpCode.Code is Code.Constrained or Code.Tail) throw Error("Prefix on floating call requires explicit relocation");
        var temps = parameters.Select(type => Temp(Float(type) ? Extended : type)).ToArray();
        for (int index = temps.Length - 1; index >= 0; index--) Emit(OpCodes.Stloc, temps[index]);
        for (int index = 0; index < temps.Length; index++) { Emit(OpCodes.Ldloc, temps[index]); if (Float(parameters[index])) Store(parameters[index]); }
    }
    void Conversion(Code code)
    {
        bool floating = StackAnalysis.Floating(stack[^1]);
        if (code is Code.Conv_R4 or Code.Conv_R8 or Code.Conv_R_Un)
        {
            if (floating)
            {
                if (code == Code.Conv_R_Un) throw Error("conv.r.un requires integer operand");
                Call(code == Code.Conv_R4 ? "NarrowSingle" : "NarrowDouble"); return;
            }
            string suffix = stack[^1] switch { "i4" => "Int32", "i8" => "Int64", "native" when pointerSize == 4 => "Int32", "native" when pointerSize == 8 => "Int64", _ => throw Error("Native integer floating conversion requires explicit TargetPointerSize=4 or 8") };
            if (stack[^1] == "native") Emit(pointerSize == 4 ? OpCodes.Conv_I4 : OpCodes.Conv_I8);
            if (code == Code.Conv_R_Un) suffix = suffix.Replace("Int", "UInt", StringComparison.Ordinal);
            Call("From" + suffix);
            Call(code == Code.Conv_R4 ? "NarrowSingle" : "NarrowDouble"); return;
        }
        if (!floating) { Original(); return; }
        bool checkedConversion = code.ToString().StartsWith("Conv_Ovf_", StringComparison.Ordinal);
        if (checkedConversion && code.ToString().EndsWith("_Un", StringComparison.Ordinal)) throw Error("Unverified floating conv.ovf.*.un contract");
        string? target = code switch {
            Code.Conv_I1 or Code.Conv_Ovf_I1 => "SByte", Code.Conv_U1 or Code.Conv_Ovf_U1 => "Byte",
            Code.Conv_I2 or Code.Conv_Ovf_I2 => "Int16", Code.Conv_U2 or Code.Conv_Ovf_U2 => "UInt16",
            Code.Conv_I4 or Code.Conv_Ovf_I4 => "Int32", Code.Conv_U4 or Code.Conv_Ovf_U4 => "UInt32",
            Code.Conv_I8 or Code.Conv_Ovf_I8 => "Int64", Code.Conv_U8 or Code.Conv_Ovf_U8 => "UInt64",
            Code.Conv_I or Code.Conv_Ovf_I when pointerSize == 4 => "Int32", Code.Conv_I or Code.Conv_Ovf_I when pointerSize == 8 => "Int64",
            Code.Conv_U or Code.Conv_Ovf_U when pointerSize == 4 => "UInt32", Code.Conv_U or Code.Conv_Ovf_U when pointerSize == 8 => "UInt64", _ => null
        };
        if (target == null) throw Error("Floating conversion requires verified checked/width contract: " + code);
        Call("To" + target + (checkedConversion ? "Checked" : ""));
        if (code is Code.Conv_I or Code.Conv_Ovf_I) Emit(OpCodes.Conv_I);
        if (code is Code.Conv_U or Code.Conv_Ovf_U) Emit(OpCodes.Conv_U);
    }
    static string? ComparisonName(Code code) => code switch {
        Code.Ceq or Code.Beq or Code.Beq_S => "Equal", Code.Bne_Un or Code.Bne_Un_S => "NotEqual",
        Code.Cgt or Code.Bgt or Code.Bgt_S => "Greater", Code.Cgt_Un or Code.Bgt_Un or Code.Bgt_Un_S => "GreaterUn",
        Code.Clt or Code.Blt or Code.Blt_S => "Less", Code.Clt_Un or Code.Blt_Un or Code.Blt_Un_S => "LessUn",
        Code.Bge or Code.Bge_S => "GreaterEqual", Code.Bge_Un or Code.Bge_Un_S => "GreaterEqualUn",
        Code.Ble or Code.Ble_S => "LessEqual", Code.Ble_Un or Code.Ble_Un_S => "LessEqualUn", _ => null
    };
    static void ExpandBranch(Instruction instruction)
    {
        instruction.OpCode = instruction.OpCode.Code switch {
            Code.Br_S => OpCodes.Br, Code.Brtrue_S => OpCodes.Brtrue, Code.Brfalse_S => OpCodes.Brfalse,
            Code.Beq_S => OpCodes.Beq, Code.Bne_Un_S => OpCodes.Bne_Un,
            Code.Bge_S => OpCodes.Bge, Code.Bge_Un_S => OpCodes.Bge_Un, Code.Bgt_S => OpCodes.Bgt, Code.Bgt_Un_S => OpCodes.Bgt_Un,
            Code.Ble_S => OpCodes.Ble, Code.Ble_Un_S => OpCodes.Ble_Un, Code.Blt_S => OpCodes.Blt, Code.Blt_Un_S => OpCodes.Blt_Un,
            Code.Leave_S => OpCodes.Leave, _ => instruction.OpCode
        };
    }
}
