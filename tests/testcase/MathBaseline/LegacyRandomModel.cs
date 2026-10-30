using System;

// Investigation model of the captured CLR2 Random plus the game's MyRandom bookkeeping.
// Integer state follows the actual CLR2 IL; each floating operation uses the measured mode.
// This is not production code or a complete x87 emulator (normal finite corpus only).
sealed class LegacyRandomModel
{
    readonly int[] state = new int[56];
    readonly Func<double, double> round;
    int index;
    int partner = 21;
    public int RanValue { get; private set; }

    public LegacyRandomModel(int seed, Func<double, double> round)
    {
        this.round = round;
        int mj = 161803398 - Math.Abs(seed);
        int mk = 1;
        state[55] = mj;
        for (int i = 1; i < 55; i++)
        {
            int ii = (21 * i) % 55;
            state[ii] = mk;
            mk = mj - mk;
            if (mk < 0) mk += int.MaxValue;
            mj = state[ii];
        }
        for (int k = 1; k < 5; k++)
            for (int i = 1; i < 56; i++)
            {
                state[i] -= state[1 + (i + 30) % 55];
                if (state[i] < 0) state[i] += int.MaxValue;
            }
    }

    int InternalSample()
    {
        if (++index >= 56) index = 1;
        if (++partner >= 56) partner = 1;
        int result = state[index] - state[partner];
        if (result < 0) result += int.MaxValue;
        state[index] = result;
        return result;
    }

    double Sample() => round(InternalSample() * (1.0 / int.MaxValue));
    public int Next() => RanValue = InternalSample();
    public int Next(int max) => RanValue = (int)round(Sample() * max);
    public double NextDouble()
    {
        double sample = Sample();
        RanValue = (int)round(sample * 100.0);
        return sample;
    }
    public double NextPMDouble() => round(round(2.0 * NextDouble()) - 1.0);
    public int Next(int min, int max)
    {
        long range = (long)max - min;
        return RanValue = range <= int.MaxValue
            ? (int)round(Sample() * range) + min
            : (int)((long)round(LargeSample() * range) + min);
    }
    double LargeSample()
    {
        int result = InternalSample();
        if (InternalSample() % 2 == 0) result = -result;
        return round(round(result + 2147483646.0) / 4294967293.0);
    }
}
