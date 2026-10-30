using Mono.Cecil;
using Mono.Cecil.Cil;

namespace GameplayMath;

// The nominal analysis and post-rewrite analysis use the same fixed point.
public sealed class StackAnalysis(MethodDefinition method)
{
    public Dictionary<Instruction, string[]> Before { get; } = new();
    readonly Queue<Instruction> pending = new();
    public static bool Floating(string type) => type is "f4" or "f8";
    public static bool Integer(string type) => type is "i4" or "i8" or "native";
    public static string Type(TypeReference type)
    {
        if (type is ByReferenceType reference) return Type(reference.ElementType) + "&";
        if (type is TypeSpecification specification && type is not (ArrayType or PointerType) && specification.ElementType.MetadataType is MetadataType.Single or MetadataType.Double)
            throw new NotSupportedException("Unreviewed modified floating type boundary: " + type.FullName);
        if (type is TypeDefinition definition && definition.IsEnum)
            return Type(definition.Fields.Single(f => f.Name == "value__").FieldType);
        if (type.MetadataType == MetadataType.ValueType && type.Scope == type.Module)
        {
            var resolved = type.Resolve();
            if (resolved?.IsEnum == true) return Type(resolved);
        }
        return type.MetadataType switch {
            MetadataType.Boolean or MetadataType.Char or MetadataType.SByte or MetadataType.Byte or
            MetadataType.Int16 or MetadataType.UInt16 or MetadataType.Int32 or MetadataType.UInt32 => "i4",
            MetadataType.Int64 or MetadataType.UInt64 => "i8",
            MetadataType.IntPtr or MetadataType.UIntPtr => "native",
            MetadataType.Single => "f4", MetadataType.Double => "f8", _ => type.FullName
        };
    }
    public static TypeReference Bind(TypeReference type, MethodReference call)
    {
        if (type is GenericParameter parameter)
        {
            if (parameter.Type == GenericParameterType.Method && call is GenericInstanceMethod method)
                return method.GenericArguments[parameter.Position];
            if (parameter.Type == GenericParameterType.Type && call.DeclaringType is GenericInstanceType declaring)
                return declaring.GenericArguments[parameter.Position];
            throw new NotSupportedException("Unbound generic call boundary: " + call.FullName);
        }
        if (type is ByReferenceType byref) return new ByReferenceType(Bind(byref.ElementType, call));
        if (type is ArrayType array) return new ArrayType(Bind(array.ElementType, call), array.Rank);
        if (type is GenericInstanceType instance)
        {
            var bound = new GenericInstanceType(instance.ElementType);
            foreach (var argument in instance.GenericArguments) bound.GenericArguments.Add(Bind(argument, call));
            return bound;
        }
        return type;
    }
    public static TypeReference FieldType(FieldReference field) => field.DeclaringType is GenericInstanceType
        ? Bind(field.FieldType, new MethodReference("__field_boundary", field.FieldType, field.DeclaringType)) : field.FieldType;
    Exception Error(Instruction instruction, string detail) => new NotSupportedException($"{method.FullName} IL_{instruction.Offset:X4} {instruction.OpCode}: {detail}");
    public void Run()
    {
        if (!method.HasBody || method.Body.Instructions.Count == 0) return;
        Before.Clear(); pending.Clear();
        try
        {
            Type(method.ReturnType);
            foreach (var parameter in method.Parameters) Type(parameter.ParameterType);
            foreach (var variable in method.Body.Variables) Type(variable.VariableType);
        }
        catch (NotSupportedException error) { throw Error(method.Body.Instructions[0], error.Message); }
        Merge(method.Body.Instructions[0], [], method.Body.Instructions[0]);
        foreach (var handler in method.Body.ExceptionHandlers)
        {
            if (handler.HandlerType == ExceptionHandlerType.Filter)
                throw Error(handler.FilterStart, "Exception filters require an explicit reviewed contract");
            Merge(handler.HandlerStart, handler.HandlerType == ExceptionHandlerType.Catch ? [Type(handler.CatchType)] : [], handler.HandlerStart);
        }
        while (pending.TryDequeue(out var instruction))
        {
            var stack = Before[instruction].ToList();
            try { Step(instruction, stack); }
            catch (NotSupportedException error) when (!error.Message.StartsWith(method.FullName + " IL_", StringComparison.Ordinal))
            { throw Error(instruction, error.Message); }
            foreach (var next in Successors(instruction)) Merge(next, stack.ToArray(), instruction);
        }
        var unreachable = method.Body.Instructions.FirstOrDefault(i => !Before.ContainsKey(i));
        if (unreachable != null) throw Error(unreachable, "Unreachable code requires explicit review");
    }
    void Merge(Instruction target, string[] stack, Instruction source)
    {
        if (!Before.TryGetValue(target, out var previous)) { Before[target] = stack; pending.Enqueue(target); return; }
        if (previous.Length != stack.Length) throw Error(source, $"Stack-height merge at IL_{target.Offset:X4}");
        var merged = previous.ToArray();
        for (int index = 0; index < previous.Length; index++)
        {
            if (previous[index] == stack[index]) continue;
            if (Floating(previous[index]) && Floating(stack[index])) merged[index] = "f8";
            else if (previous[index] == "null" && Reference(stack[index])) merged[index] = stack[index];
            else if (!(stack[index] == "null" && Reference(previous[index]))) throw Error(source, $"Unsupported merge {previous[index]} / {stack[index]} at IL_{target.Offset:X4}");
        }
        if (merged.SequenceEqual(previous)) return;
        Before[target] = merged; pending.Enqueue(target);
    }
    static bool Reference(string type) => !Integer(type) && !Floating(type) && !type.EndsWith('&') && !type.EndsWith('*') && type != "handle";
    static IEnumerable<Instruction> Successors(Instruction instruction)
    {
        if (instruction.OpCode.Code is Code.Endfinally or Code.Endfilter or Code.Jmp) yield break;
        if (instruction.Operand is Instruction target) yield return target;
        if (instruction.Operand is Instruction[] targets) foreach (var next in targets) yield return next;
        if (instruction.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw or FlowControl.Branch) yield break;
        if (instruction.Next != null) yield return instruction.Next;
    }
    string Argument(int index) => index == 0 && method.HasThis ? Type(method.DeclaringType) : Type(method.Parameters[index - (method.HasThis ? 1 : 0)].ParameterType);
    void Step(Instruction instruction, List<string> stack)
    {
        string Pop() { if (stack.Count == 0) throw Error(instruction, "Stack underflow"); var type = stack[^1]; stack.RemoveAt(stack.Count - 1); return type; }
        void Pops(int count) { for (int index = 0; index < count; index++) Pop(); }
        var code = instruction.OpCode.Code;
        if (code.ToString().StartsWith("Conv_Ovf_", StringComparison.Ordinal))
        {
            Pop(); stack.Add(code.ToString().Contains("I8", StringComparison.Ordinal) || code.ToString().Contains("U8", StringComparison.Ordinal) ? "i8" :
                code is Code.Conv_Ovf_I or Code.Conv_Ovf_I_Un or Code.Conv_Ovf_U or Code.Conv_Ovf_U_Un ? "native" : "i4"); return;
        }
        switch (code)
        {
            case Code.Nop: case Code.Br: case Code.Br_S: case Code.Constrained: case Code.Readonly:
            case Code.Volatile: case Code.Unaligned: case Code.Tail: return;
            case Code.Leave: case Code.Leave_S: stack.Clear(); return;
            case Code.Endfinally: case Code.Rethrow: if (stack.Count != 0) throw Error(instruction, "Nonempty handler exit stack"); return;
            case Code.Throw: Pop(); stack.Clear(); return;
            case Code.Ldarg_0: case Code.Ldarg_1: case Code.Ldarg_2: case Code.Ldarg_3:
                stack.Add(Argument((int)code - (int)Code.Ldarg_0)); return;
            case Code.Ldarg: case Code.Ldarg_S: stack.Add(Type(((ParameterDefinition)instruction.Operand).ParameterType)); return;
            case Code.Ldarga: case Code.Ldarga_S: stack.Add(Type(((ParameterDefinition)instruction.Operand).ParameterType) + "&"); return;
            case Code.Starg: case Code.Starg_S: Pop(); return;
            case Code.Ldloc_0: case Code.Ldloc_1: case Code.Ldloc_2: case Code.Ldloc_3:
                stack.Add(Type(method.Body.Variables[(int)code - (int)Code.Ldloc_0].VariableType)); return;
            case Code.Stloc_0: case Code.Stloc_1: case Code.Stloc_2: case Code.Stloc_3: Pop(); return;
            case Code.Ldloc: case Code.Ldloc_S: stack.Add(Type(((VariableDefinition)instruction.Operand).VariableType)); return;
            case Code.Ldloca: case Code.Ldloca_S: stack.Add(Type(((VariableDefinition)instruction.Operand).VariableType) + "&"); return;
            case Code.Stloc: case Code.Stloc_S: Pop(); return;
            case Code.Ldc_I4: case Code.Ldc_I4_S: case Code.Ldc_I4_M1:
            case Code.Ldc_I4_0: case Code.Ldc_I4_1: case Code.Ldc_I4_2: case Code.Ldc_I4_3:
            case Code.Ldc_I4_4: case Code.Ldc_I4_5: case Code.Ldc_I4_6: case Code.Ldc_I4_7: case Code.Ldc_I4_8: stack.Add("i4"); return;
            case Code.Ldc_I8: stack.Add("i8"); return;
            case Code.Ldc_R4: stack.Add("f4"); return;
            case Code.Ldc_R8: stack.Add("f8"); return;
            case Code.Ldnull: stack.Add("null"); return;
            case Code.Ldstr: stack.Add("System.String"); return;
            case Code.Dup: var duplicate = Pop(); stack.Add(duplicate); stack.Add(duplicate); return;
            case Code.Pop: Pop(); return;
            case Code.Ldfld: Pop(); stack.Add(Type(FieldType((FieldReference)instruction.Operand))); return;
            case Code.Ldflda: Pop(); stack.Add(Type(FieldType((FieldReference)instruction.Operand)) + "&"); return;
            case Code.Ldsflda: stack.Add(Type(FieldType((FieldReference)instruction.Operand)) + "&"); return;
            case Code.Ldsfld: stack.Add(Type(FieldType((FieldReference)instruction.Operand))); return;
            case Code.Stfld: Pops(2); return;
            case Code.Stsfld: Pop(); return;
            case Code.Call: case Code.Callvirt: case Code.Newobj:
                var call = (MethodReference)instruction.Operand;
                if ((call.CallingConvention & (MethodCallingConvention)15) == MethodCallingConvention.VarArg)
                    throw Error(instruction, "Vararg call requires an explicit reviewed argument contract");
                Pops(call.Parameters.Count + (call.HasThis && code != Code.Newobj ? 1 : 0));
                if (code == Code.Newobj) stack.Add(Type(call.DeclaringType));
                else if (call.ReturnType.MetadataType != MetadataType.Void) stack.Add(Type(Bind(call.ReturnType, call)));
                return;
            case Code.Conv_R4: Pop(); stack.Add("f4"); return;
            case Code.Conv_R8: case Code.Conv_R_Un: Pop(); stack.Add("f8"); return;
            case Code.Conv_I: case Code.Conv_U: Pop(); stack.Add("native"); return;
            case Code.Conv_I8: case Code.Conv_U8: Pop(); stack.Add("i8"); return;
            case Code.Conv_I1: case Code.Conv_I2: case Code.Conv_I4: case Code.Conv_U1: case Code.Conv_U2: case Code.Conv_U4: Pop(); stack.Add("i4"); return;
            case Code.Add: case Code.Sub: case Code.Mul: case Code.Div: case Code.Rem:
            case Code.Div_Un: case Code.Rem_Un: case Code.And: case Code.Or: case Code.Xor:
            case Code.Add_Ovf: case Code.Add_Ovf_Un: case Code.Sub_Ovf: case Code.Sub_Ovf_Un: case Code.Mul_Ovf: case Code.Mul_Ovf_Un:
                var right = Pop(); var left = Pop();
                if ((Floating(left) || Floating(right)) && code is not (Code.Add or Code.Sub or Code.Mul or Code.Div or Code.Rem))
                    throw Error(instruction, "Integer-only opcode received floating operands");
                if (left == right && (Floating(left) || Integer(left))) { stack.Add(left); return; }
                if (Floating(left) && Floating(right)) { stack.Add("f8"); return; }
                if (Integer(right) && (left.EndsWith('&') || left.EndsWith('*')) && code is Code.Add or Code.Sub) { stack.Add(left); return; }
                throw Error(instruction, $"Unsupported binary operands {left}, {right}");
            case Code.Shl: case Code.Shr: case Code.Shr_Un: Pop(); var shifted = Pop(); if (!Integer(shifted)) throw Error(instruction, "Noninteger shift"); stack.Add(shifted); return;
            case Code.Neg: case Code.Not:
                var unary = Pop(); if (!Floating(unary) && !Integer(unary)) throw Error(instruction, "Invalid unary " + unary);
                if (code == Code.Not && Floating(unary)) throw Error(instruction, "Bitwise not requires integer"); stack.Add(unary); return;
            case Code.Ceq: case Code.Cgt: case Code.Cgt_Un: case Code.Clt: case Code.Clt_Un: Pops(2); stack.Add("i4"); return;
            case Code.Brtrue: case Code.Brtrue_S: case Code.Brfalse: case Code.Brfalse_S: case Code.Switch:
                if (Floating(Pop())) throw Error(instruction, "Floating truth branch requires explicit comparison"); return;
            case Code.Beq: case Code.Beq_S: case Code.Bne_Un: case Code.Bne_Un_S:
            case Code.Bge: case Code.Bge_S: case Code.Bge_Un: case Code.Bge_Un_S:
            case Code.Bgt: case Code.Bgt_S: case Code.Bgt_Un: case Code.Bgt_Un_S:
            case Code.Ble: case Code.Ble_S: case Code.Ble_Un: case Code.Ble_Un_S:
            case Code.Blt: case Code.Blt_S: case Code.Blt_Un: case Code.Blt_Un_S: Pops(2); return;
            case Code.Ldlen: Pop(); stack.Add("native"); return;
            case Code.Ldelema: Pops(2); stack.Add(Type((TypeReference)instruction.Operand) + "&"); return;
            case Code.Ldelem_R4: Pops(2); stack.Add("f4"); return;
            case Code.Ldelem_R8: Pops(2); stack.Add("f8"); return;
            case Code.Ldind_R4: Pop(); stack.Add("f4"); return;
            case Code.Ldind_R8: Pop(); stack.Add("f8"); return;
            case Code.Ldelem_I: Pops(2); stack.Add("native"); return;
            case Code.Ldind_I: Pop(); stack.Add("native"); return;
            case Code.Ldelem_I8: Pops(2); stack.Add("i8"); return;
            case Code.Ldind_I8: Pop(); stack.Add("i8"); return;
            case Code.Ldelem_I1: case Code.Ldelem_U1: case Code.Ldelem_I2: case Code.Ldelem_U2: case Code.Ldelem_I4: case Code.Ldelem_U4: Pops(2); stack.Add("i4"); return;
            case Code.Ldind_I1: case Code.Ldind_U1: case Code.Ldind_I2: case Code.Ldind_U2: case Code.Ldind_I4: case Code.Ldind_U4: Pop(); stack.Add("i4"); return;
            case Code.Ldelem_Ref: Pops(2); stack.Add("System.Object"); return;
            case Code.Ldind_Ref: Pop(); stack.Add("System.Object"); return;
            case Code.Stind_R4: case Code.Stind_R8: case Code.Stind_I: case Code.Stind_I1: case Code.Stind_I2: case Code.Stind_I4: case Code.Stind_I8: case Code.Stind_Ref: Pops(2); return;
            case Code.Stelem_R4: case Code.Stelem_R8: case Code.Stelem_I: case Code.Stelem_I1: case Code.Stelem_I2: case Code.Stelem_I4: case Code.Stelem_I8: case Code.Stelem_Ref: Pops(3); return;
            case Code.Ldelem_Any: Pops(2); stack.Add(Type((TypeReference)instruction.Operand)); return;
            case Code.Stelem_Any: Pops(3); return;
            case Code.Ldobj: Pop(); stack.Add(Type((TypeReference)instruction.Operand)); return;
            case Code.Stobj: case Code.Cpobj: Pops(2); return;
            case Code.Initobj: Pop(); return;
            case Code.Newarr: Pop(); stack.Add(((TypeReference)instruction.Operand).FullName + "[]"); return;
            case Code.Box: Pop(); stack.Add("System.Object"); return;
            case Code.Unbox: Pop(); stack.Add(Type((TypeReference)instruction.Operand) + "&"); return;
            case Code.Unbox_Any: Pop(); stack.Add(Type((TypeReference)instruction.Operand)); return;
            case Code.Castclass: case Code.Isinst: Pop(); stack.Add(Type((TypeReference)instruction.Operand)); return;
            case Code.Ldftn: stack.Add("native"); return;
            case Code.Ldvirtftn: Pop(); stack.Add("native"); return;
            case Code.Ldtoken: stack.Add("handle"); return;
            case Code.Sizeof: stack.Add("i4"); return;
            case Code.Ret:
                if (method.ReturnType.MetadataType != MetadataType.Void) Pop();
                if (stack.Count != 0) throw Error(instruction, "Nonempty return stack"); return;
            default: throw Error(instruction, "Opcode has no supported stack transfer rule");
        }
    }
}
