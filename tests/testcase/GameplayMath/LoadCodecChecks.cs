#nullable enable
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SlimDX.Compat;

internal static class LoadCodecChecks
{
    private const string Kernel = "thsss_legacy_math";
    private static long singleCases, doubleCases;
    private static ulong checksum;
    private static long storeCases;
    private static ulong storeChecksum;

    [DllImport(Kernel, EntryPoint = "software_ext_load_float", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int NativeSingle(float input, out LegacyExtended output);
    [DllImport(Kernel, EntryPoint = "software_ext_load_double", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int NativeDouble(double input, out LegacyExtended output);
    [DllImport(Kernel, EntryPoint = "software_ext_store_float", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int NativeStoreSingle(in LegacyExtended input, out float output);
    [DllImport(Kernel, EntryPoint = "software_ext_store_double", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int NativeStoreDouble(in LegacyExtended input, out double output);
    [DllImport("load_codec_state", EntryPoint = "load_codec_get_mxcsr", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetMxcsr();
    [DllImport("load_codec_state", EntryPoint = "load_codec_set_mxcsr", CallingConvention = CallingConvention.Cdecl)]
    private static extern void SetMxcsr(uint value);
    [DllImport("load_codec_state", EntryPoint = "load_codec_get_x87", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint GetX87();

    private static int Main(string[] args)
    {
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0]) || !File.Exists(args[0]))
            throw new ArgumentException("Usage: dotnet LoadCodecChecks.dll <absolute-native-kernel-path>");
        nint kernel = NativeLibrary.Load(args[0]);
        NativeLibrary.SetDllImportResolver(typeof(LoadCodecChecks).Assembly,
            (name, _, _) => name == Kernel ? kernel : nint.Zero);
        if (Marshal.SizeOf<LegacyExtended>() != 16) throw new InvalidOperationException("Expected 16-byte ext80 ABI");
        Console.WriteLine($"LOAD_CODEC_BACKEND {LegacyFloat.Backend} kernel={args[0]}");
        CheckComparator();
        foreach (int precision in new[] { 24, 53 })
        {
            LegacyFloat.Precision = precision;
            long singlesBefore = singleCases, doublesBefore = doubleCases;
            CheckBoundaries();
            CheckRandomFinite();
            long storesBefore = storeCases;
            CheckStoreBoundaries();
            CheckRandomStores();
            CheckHostState();
            Console.WriteLine($"LOAD_CODEC_PC_PASS precision={precision} f32={singleCases - singlesBefore} f64={doubleCases - doublesBefore}");
            Console.WriteLine($"STORE_CODEC_PC_PASS precision={precision} pairs={storeCases - storesBefore}");
        }
        CheckAllSingleNaNs();
        Console.WriteLine($"LOAD_CODEC_PASS f32={singleCases} f64={doubleCases} checksum={checksum:X16}");
        Console.WriteLine($"STORE_CODEC_PASS pairs={storeCases} checksum={storeChecksum:X16}");
        Benchmark();
        BenchmarkStores();
        return 0;
    }

    private static bool Same(in LegacyExtended a, in LegacyExtended b) =>
        a.Significand == b.Significand && a.SignExp == b.SignExp &&
        a.Pad0 == b.Pad0 && a.Pad1 == b.Pad1 && a.Pad2 == b.Pad2;

    private static void RequireSame(in LegacyExtended expected, in LegacyExtended actual, string context)
    {
        if (Same(expected, actual)) return;
        throw new InvalidOperationException($"{context}: expected={Describe(expected)} actual={Describe(actual)}");
    }

    private static string Describe(LegacyExtended value) =>
        $"{value.Significand:X16}/{value.SignExp:X4}/{value.Pad0:X4}/{value.Pad1:X4}/{value.Pad2:X4}";

    private static void CheckComparator()
    {
        LegacyExtended expected = new() { Significand = 0x8000000000000000, SignExp = 0x3fff };
        for (int field = 0; field < 5; field++)
        {
            LegacyExtended corrupt = expected;
            switch (field)
            {
                case 0: corrupt.Significand ^= 1; break;
                case 1: corrupt.SignExp ^= 1; break;
                case 2: corrupt.Pad0 = 1; break;
                case 3: corrupt.Pad1 = 1; break;
                case 4: corrupt.Pad2 = 1; break;
            }
            bool rejected = false;
            try { RequireSame(corrupt, expected, "deliberately corrupted expectation"); }
            catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException($"Comparator ignored field {field}");
        }
        Console.WriteLine("LOAD_CODEC_COMPARATOR_NEGATIVE_PASS fields=5 padding=3");
    }

    private static void CheckSingle(uint bits)
    {
        float input = BitConverter.Int32BitsToSingle(unchecked((int)bits));
        int status = NativeSingle(input, out LegacyExtended expected);
        LegacyExtended actual = LegacyFloat.FromSingle(input);
        if (status != 0) throw new InvalidOperationException($"Native F32 codec status={status}");
        if (!Same(expected, actual)) RequireSame(expected, actual, $"F32 PC{LegacyFloat.Precision} bits={bits:X8}");
        checksum = Mix(checksum, actual);
        singleCases++;
    }

    private static void CheckDouble(ulong bits)
    {
        double input = BitConverter.Int64BitsToDouble(unchecked((long)bits));
        int status = NativeDouble(input, out LegacyExtended expected);
        LegacyExtended actual = LegacyFloat.FromDouble(input);
        if (status != 0) throw new InvalidOperationException($"Native F64 codec status={status}");
        if (!Same(expected, actual)) RequireSame(expected, actual, $"F64 PC{LegacyFloat.Precision} bits={bits:X16}");
        checksum = Mix(checksum, actual);
        doubleCases++;
    }

    private static ulong Mix(ulong sum, LegacyExtended value) => unchecked(((sum ^ value.Significand) * 1099511628211UL) ^
        ((ulong)value.SignExp << 48) ^ ((ulong)value.Pad0 << 32) ^ ((ulong)value.Pad1 << 16) ^ value.Pad2);

    private static void SingleNeighbors(uint magnitude)
    {
        for (uint sign = 0; ; sign = 0x80000000)
        {
            CheckSingle(sign | magnitude);
            if (magnitude != 0) CheckSingle(sign | (magnitude - 1));
            if (magnitude < 0x7fffffff) CheckSingle(sign | (magnitude + 1));
            if (sign != 0) break;
        }
    }

    private static void DoubleNeighbors(ulong magnitude)
    {
        for (ulong sign = 0; ; sign = 0x8000000000000000)
        {
            CheckDouble(sign | magnitude);
            if (magnitude != 0) CheckDouble(sign | (magnitude - 1));
            if (magnitude < 0x7fffffffffffffff) CheckDouble(sign | (magnitude + 1));
            if (sign != 0) break;
        }
    }

    private static void CheckBoundaries()
    {
        for (uint exponent = 0; exponent <= 255; exponent++) SingleNeighbors(exponent << 23);
        for (ulong exponent = 0; exponent <= 2047; exponent++) DoubleNeighbors(exponent << 52);
        for (int bit = 0; bit < 23; bit++) SingleNeighbors(1U << bit);
        for (int bit = 0; bit < 52; bit++) DoubleNeighbors(1UL << bit);
        for (int bit = 0; bit < 52; bit++)
        {
            ulong payload = 1UL << bit;
            DoubleNeighbors(0x7ff0000000000000 | payload);
            DoubleNeighbors(0x7ff0000000000000 | (0xfffffffffffff ^ payload));
            DoubleNeighbors(0x7ff8000000000000 | payload);
        }
        SingleNeighbors(0x7fffffff);
        DoubleNeighbors(0x7fffffffffffffff);
    }

    private static ulong Next(ref ulong state)
    {
        state ^= state << 13;
        state ^= state >> 7;
        state ^= state << 17;
        return state;
    }

    private static void CheckRandomFinite()
    {
        ulong state = 0x9e3779b97f4a7c15;
        for (int count = 0; count < 1_000_000;)
        {
            uint bits = (uint)Next(ref state);
            if ((bits & 0x7f800000) == 0x7f800000) continue;
            CheckSingle(bits);
            count++;
        }
        state = 0xd1b54a32d192ed03;
        for (int count = 0; count < 1_000_000;)
        {
            ulong bits = Next(ref state);
            if ((bits & 0x7ff0000000000000) == 0x7ff0000000000000) continue;
            CheckDouble(bits);
            count++;
        }
    }

    private static void CheckAllSingleNaNs()
    {
        long before = singleCases;
        for (uint payload = 1; payload <= 0x7fffff; payload++)
        {
            CheckSingle(0x7f800000 | payload);
            CheckSingle(0xff800000 | payload);
        }
        if (singleCases - before != 16_777_214) throw new InvalidOperationException("Incomplete F32 NaN enumeration");
        Console.WriteLine("LOAD_CODEC_EXHAUSTIVE_NAN_PASS signed_f32=16777214");
    }

    private static void CheckStore(LegacyExtended input)
    {
        int singleStatus = NativeStoreSingle(input, out float expectedSingle);
        int doubleStatus = NativeStoreDouble(input, out double expectedDouble);
        if (singleStatus != 0 || doubleStatus != 0)
            throw new InvalidOperationException($"Native store status F32={singleStatus} F64={doubleStatus}");
        uint expected32 = unchecked((uint)BitConverter.SingleToInt32Bits(expectedSingle));
        ulong expected64 = unchecked((ulong)BitConverter.DoubleToInt64Bits(expectedDouble));
        uint actual32 = unchecked((uint)BitConverter.SingleToInt32Bits(LegacyFloat.ToSingle(input)));
        ulong actual64 = unchecked((ulong)BitConverter.DoubleToInt64Bits(LegacyFloat.ToDouble(input)));
        if (expected32 != actual32 || expected64 != actual64)
            throw new InvalidOperationException($"Store PC{LegacyFloat.Precision} ext={Describe(input)} expected={expected32:X8}/{expected64:X16} actual={actual32:X8}/{actual64:X16}");
        storeChecksum = unchecked((storeChecksum ^ actual64) * 1099511628211UL ^ actual32);
        storeCases++;
    }

    private static void CheckSignedStore(ushort exponent, ulong significand)
    {
        LegacyExtended input = new() { SignExp = exponent, Significand = significand };
        CheckStore(input);
        input.SignExp |= 0x8000;
        CheckStore(input);
        input.Pad0 = 0xdead; input.Pad1 = 0xbeef; input.Pad2 = 0xa55a;
        CheckStore(input);
        input.SignExp = exponent;
        CheckStore(input);
    }

    private static void CheckStoreBoundaries()
    {
        // Every extended exponent: powers, largest significands, malformed integer bits, and padding.
        for (int exponent = 0; exponent <= 0x7fff; exponent++)
        {
            CheckSignedStore((ushort)exponent, 0x8000000000000000);
            CheckSignedStore((ushort)exponent, ulong.MaxValue);
            CheckSignedStore((ushort)exponent, 0);
            CheckSignedStore((ushort)exponent, 0x7fffffffffffffff);
        }
        foreach ((int fractionBits, int bias) in new[] { (23, 127), (52, 1023) })
        for (int power = 1 - bias - fractionBits - 2; power <= bias + 2; power++)
        {
            int shift = power >= 1 - bias ? 63 - fractionBits :
                Math.Clamp(63 - (power - (1 - bias - fractionBits)), 1, 63);
            ulong unit = 1UL << shift, half = unit >> 1;
            // Exact endpoints, a nonzero discarded bit, and both even/odd RNE ties with neighbors.
            foreach (ulong significand in new[] { 0x8000000000000000UL, 0x8000000000000001UL,
                ulong.MaxValue, ulong.MaxValue - 1, 0x8000000000000000UL | unit,
                0x8000000000000000UL | half, 0x8000000000000000UL | unit | half })
            {
                CheckSignedStore((ushort)(power + 16383), significand);
                CheckSignedStore((ushort)(power + 16383), significand - 1);
                if (significand != ulong.MaxValue) CheckSignedStore((ushort)(power + 16383), significand + 1);
            }
        }
        for (int bit = 0; bit < 64; bit++)
        {
            ulong payload = 1UL << bit;
            CheckSignedStore(0x7fff, payload);
            CheckSignedStore(0x7fff, 0x8000000000000000 | payload);
            CheckSignedStore(0x7fff, 0xc000000000000000 | payload);
            CheckSignedStore(0, payload);
            CheckSignedStore(1, payload);
        }
    }

    private static void CheckRandomStores()
    {
        ulong state = 0xa0761d6478bd642f;
        for (int i = 0; i < 1_000_000; i++)
        {
            ulong significand = Next(ref state), remainder = Next(ref state);
            CheckStore(new LegacyExtended { Significand = significand, SignExp = (ushort)remainder,
                Pad0 = (ushort)(remainder >> 16), Pad1 = (ushort)(remainder >> 32), Pad2 = (ushort)(remainder >> 48) });
        }
        for (int i = 0; i < 200_000; i++)
        {
            bool single = (i & 1) == 0;
            int shift = single ? 40 : 11;
            ulong significand = (Next(ref state) | 0x8000000000000000) & ~((1UL << shift) - 1);
            ulong choice = Next(ref state);
            int exponent = (i & 3) == 0 ? (int)(choice % 32766) + 1 :
                16383 + (single ? (int)(choice % 277) - 149 : (int)(choice % 2098) - 1074);
            var input = new LegacyExtended { Significand = significand,
                SignExp = (ushort)((uint)exponent | (uint)((choice >> 48) & 0x8000)),
                Pad0 = (ushort)choice, Pad1 = (ushort)(choice >> 16), Pad2 = (ushort)(choice >> 32) };
            CheckStore(input);
            input.Significand |= 1;
            CheckStore(input);
        }
    }

    private static void CheckHostState()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            Console.WriteLine("LOAD_CODEC_HOST_STATE_SKIP requires Linux x64 helper");
            return;
        }
        uint[] modes = { 0, 0x40, 0x8000, 0x8040 };
        uint[] singles = { 0, 0x80000000, 1, 0x007fffff, 0x80000001, 0x807fffff,
            0x00800000, 0x3f800001, 0x7f7fffff, 0x7f800001, 0xff800001, 0x7fbfffff };
        ulong[] doubles = { 0, 0x8000000000000000, 1, 0xfffffffffffff, 0x8000000000000001,
            0x10000000000000, 0x3ff0000000000001, 0x7fefffffffffffff, 0x7ff0000000000001, 0xfff0000000000001 };
        var expectedSingle = new LegacyExtended[singles.Length];
        var expectedDouble = new LegacyExtended[doubles.Length];
        for (int i = 0; i < singles.Length; i++)
        {
            NativeSingle(BitConverter.Int32BitsToSingle(unchecked((int)singles[i])), out expectedSingle[i]);
            _ = LegacyFloat.FromSingle(BitConverter.Int32BitsToSingle(unchecked((int)singles[i])));
            _ = LegacyFloat.ToSingle(expectedSingle[i]);
        }
        for (int i = 0; i < doubles.Length; i++)
        {
            NativeDouble(BitConverter.Int64BitsToDouble(unchecked((long)doubles[i])), out expectedDouble[i]);
            _ = LegacyFloat.FromDouble(BitConverter.Int64BitsToDouble(unchecked((long)doubles[i])));
            _ = LegacyFloat.ToDouble(expectedDouble[i]);
        }
        // Resolve/JIT every call before capturing state; no formatting or allocation inside the probe.
        _ = GetMxcsr(); _ = GetX87();
        uint original = GetMxcsr();
        SetMxcsr(original);
        try { _ = ProbeHostState(original, singles, doubles, expectedSingle, expectedDouble); }
        finally { SetMxcsr(original); }
        foreach (uint mode in modes)
        {
            int failure;
            // Clear sticky status so a new invalid/denormal exception cannot hide in existing flags.
            try { failure = ProbeHostState((original & ~(0x8040U | 0x3fU)) | mode, singles, doubles, expectedSingle, expectedDouble); }
            finally { SetMxcsr(original); }
            if (failure != 0) throw new InvalidOperationException($"Host-state probe failed code={failure} DAZ/FTZ={mode:X4}");
        }
        Console.WriteLine("LOAD_CODEC_HOST_STATE_PASS daz_ftz=0000,0040,8000,8040 exact_store_roundtrip=pass mxcsr_full_and_x87_cw_sw=unchanged");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int ProbeHostState(uint mode, uint[] singles, ulong[] doubles,
        LegacyExtended[] expectedSingle, LegacyExtended[] expectedDouble)
    {
        SetMxcsr(mode);
        uint beforeMxcsr = GetMxcsr(), beforeX87 = GetX87();
        bool equal = true;
        for (int repetition = 0; repetition < 1000; repetition++)
        {
            for (int i = 0; i < singles.Length; i++)
            {
                LegacyExtended value = LegacyFloat.FromSingle(BitConverter.Int32BitsToSingle(unchecked((int)singles[i])));
                equal &= Same(expectedSingle[i], value);
                if ((singles[i] & 0x7f800000) != 0x7f800000)
                    equal &= unchecked((uint)BitConverter.SingleToInt32Bits(LegacyFloat.ToSingle(value))) == singles[i];
            }
            for (int i = 0; i < doubles.Length; i++)
            {
                LegacyExtended value = LegacyFloat.FromDouble(BitConverter.Int64BitsToDouble(unchecked((long)doubles[i])));
                equal &= Same(expectedDouble[i], value);
                if ((doubles[i] & 0x7ff0000000000000) != 0x7ff0000000000000)
                    equal &= unchecked((ulong)BitConverter.DoubleToInt64Bits(LegacyFloat.ToDouble(value))) == doubles[i];
            }
        }
        uint afterMxcsr = GetMxcsr(), afterX87 = GetX87();
        if (beforeMxcsr != mode || afterMxcsr != beforeMxcsr) return 1;
        if (afterX87 != beforeX87) return 2;
        return equal ? 0 : 3;
    }

    private static void Benchmark()
    {
#if DEBUG
        Console.WriteLine("LOAD_CODEC_BENCH_SKIP requires Release build");
#else
        uint[] singles = new uint[4096];
        ulong[] doubles = new ulong[4096];
        ulong state = 0x243f6a8885a308d3;
        for (int i = 0; i < singles.Length; i++)
        {
            singles[i] = (uint)Next(ref state);
            doubles[i] = Next(ref state);
        }
        foreach (bool single in new[] { true, false })
        {
            for (int warm = 0; warm < 20; warm++)
            {
                _ = TimedLoads(false, single, singles, doubles, 1);
                _ = TimedLoads(true, single, singles, doubles, 1);
            }
            const int rounds = 256;
            long start = Stopwatch.GetTimestamp();
            ulong managed = TimedLoads(false, single, singles, doubles, rounds);
            long managedTicks = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            ulong native = TimedLoads(true, single, singles, doubles, rounds);
            long nativeTicks = Stopwatch.GetTimestamp() - start;
            if (managed != native) throw new InvalidOperationException("Benchmark checksums differ");
            double scale = 1e9 / Stopwatch.Frequency / (rounds * singles.Length);
            Console.WriteLine($"LOAD_CODEC_BENCH {(single ? "f32" : "f64")} calls={rounds * singles.Length} managed_ns={managedTicks * scale:F2} native_ns={nativeTicks * scale:F2} checksum={managed:X16}");
        }
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong TimedLoads(bool native, bool single, uint[] singles, ulong[] doubles, int rounds)
    {
        ulong sum = 0;
        for (int round = 0; round < rounds; round++)
        for (int i = 0; i < singles.Length; i++)
        {
            LegacyExtended value;
            if (single)
            {
                float input = BitConverter.Int32BitsToSingle(unchecked((int)singles[i]));
                if (native) { if (NativeSingle(input, out value) != 0) throw new InvalidOperationException("Benchmark native F32 status"); }
                else value = LegacyFloat.FromSingle(input);
            }
            else
            {
                double input = BitConverter.Int64BitsToDouble(unchecked((long)doubles[i]));
                if (native) { if (NativeDouble(input, out value) != 0) throw new InvalidOperationException("Benchmark native F64 status"); }
                else value = LegacyFloat.FromDouble(input);
            }
            sum = Mix(sum, value);
        }
        return sum;
    }

    private static void BenchmarkStores()
    {
#if DEBUG
        Console.WriteLine("STORE_CODEC_BENCH_SKIP requires Release build");
#else
        foreach (bool single in new[] { true, false })
        {
            var inputs = new LegacyExtended[4096];
            ulong state = 0xe7037ed1a0b428db;
            for (int i = 0; i < inputs.Length; i++)
            {
                ulong bits = Next(ref state);
                int status;
                if (single)
                {
                    uint raw = (uint)bits;
                    if ((i & 7) == 0) raw &= 0x807fffff;
                    if ((raw & 0x7f800000) == 0x7f800000) raw ^= 0x00800000;
                    status = NativeSingle(BitConverter.Int32BitsToSingle(unchecked((int)raw)), out inputs[i]);
                }
                else
                {
                    if ((i & 7) == 0) bits &= 0x800fffffffffffff;
                    if ((bits & 0x7ff0000000000000) == 0x7ff0000000000000) bits ^= 0x0010000000000000;
                    status = NativeDouble(BitConverter.Int64BitsToDouble(unchecked((long)bits)), out inputs[i]);
                }
                if (status != 0) throw new InvalidOperationException("Store benchmark input codec status");
            }
            for (int warm = 0; warm < 20; warm++)
            {
                _ = TimedStores(false, single, inputs, 1);
                _ = TimedStores(true, single, inputs, 1);
            }
            const int rounds = 256;
            long start = Stopwatch.GetTimestamp();
            ulong managed = TimedStores(false, single, inputs, rounds);
            long managedTicks = Stopwatch.GetTimestamp() - start;
            start = Stopwatch.GetTimestamp();
            ulong native = TimedStores(true, single, inputs, rounds);
            long nativeTicks = Stopwatch.GetTimestamp() - start;
            if (managed != native) throw new InvalidOperationException("Store benchmark checksums differ");
            double scale = 1e9 / Stopwatch.Frequency / (rounds * inputs.Length);
            Console.WriteLine($"STORE_CODEC_BENCH {(single ? "f32" : "f64")} exact_finite_subnormal=512 calls={rounds * inputs.Length} managed_ns={managedTicks * scale:F2} native_ns={nativeTicks * scale:F2} checksum={managed:X16}");
        }
#endif
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ulong TimedStores(bool native, bool single, LegacyExtended[] inputs, int rounds)
    {
        ulong sum = 0;
        for (int round = 0; round < rounds; round++)
        for (int i = 0; i < inputs.Length; i++)
        {
            ulong bits;
            if (single)
            {
                float value;
                if (native) { if (NativeStoreSingle(inputs[i], out value) != 0) throw new InvalidOperationException("Benchmark native F32 store status"); }
                else value = LegacyFloat.ToSingle(inputs[i]);
                bits = unchecked((uint)BitConverter.SingleToInt32Bits(value));
            }
            else
            {
                double value;
                if (native) { if (NativeStoreDouble(inputs[i], out value) != 0) throw new InvalidOperationException("Benchmark native F64 store status"); }
                else value = LegacyFloat.ToDouble(inputs[i]);
                bits = unchecked((ulong)BitConverter.DoubleToInt64Bits(value));
            }
            sum = unchecked((sum ^ bits) * 1099511628211UL);
        }
        return sum;
    }
}
