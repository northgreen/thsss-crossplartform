using System;
using SlimDX.Compat;

namespace Shooting;

public class MyRandom : Random
{
	private readonly int[] seedArray = new int[56];
	private int inext;
	private int inextp = 21;

	public int ranValue { get; private set; }

	public MyRandom()
		: this(Environment.TickCount)
	{
	}

	public MyRandom(int Seed)
		: base(Seed)
	{
		// Integer state is independent of the caller's floating precision; scaling is not.
		int previous = unchecked(161803398 - Math.Abs(Seed));
		int current = 1;
		seedArray[55] = previous;
		for (int i = 1; i < 55; i++)
		{
			int index = 21 * i % 55;
			seedArray[index] = current;
			current = unchecked(previous - current);
			if (current < 0) current = unchecked(current + int.MaxValue);
			previous = seedArray[index];
		}
		for (int pass = 1; pass < 5; pass++)
		for (int i = 1; i < 56; i++)
		{
			seedArray[i] = unchecked(seedArray[i] - seedArray[1 + (i + 30) % 55]);
			if (seedArray[i] < 0) seedArray[i] = unchecked(seedArray[i] + int.MaxValue);
		}
	}

	private int InternalSample()
	{
		if (++inext >= 56) inext = 1;
		if (++inextp >= 56) inextp = 1;
		int result = unchecked(seedArray[inext] - seedArray[inextp]);
		if (result < 0) result = unchecked(result + int.MaxValue);
		seedArray[inext] = result;
		return result;
	}

	protected override double Sample() => LegacyFloat.ToDouble(LegacyFloat.Mul(
		LegacyFloat.FromInt32(InternalSample()), LegacyFloat.FromDouble(1.0 / int.MaxValue)));

	private double LargeSample()
	{
		int result = InternalSample();
		if (InternalSample() % 2 == 0) result = -result;
		return LegacyFloat.ToDouble(LegacyFloat.Div(
			LegacyFloat.Add(LegacyFloat.FromInt32(result), LegacyFloat.FromInt32(int.MaxValue - 1)),
			LegacyFloat.FromUInt32(4294967293u)));
	}

	public override int Next()
	{
		ranValue = InternalSample();
		return ranValue;
	}

	public override int Next(int maxValue)
	{
		if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
		ranValue = LegacyFloat.ToInt32(LegacyFloat.Mul(LegacyFloat.FromDouble(Sample()), LegacyFloat.FromInt32(maxValue)));
		return ranValue;
	}

	public override int Next(int minValue, int maxValue)
	{
		if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
		long range = (long)maxValue - minValue;
		if (range <= int.MaxValue)
			ranValue = unchecked(LegacyFloat.ToInt32(LegacyFloat.Mul(LegacyFloat.FromDouble(Sample()), LegacyFloat.FromInt64(range))) + minValue);
		else
			ranValue = unchecked((int)(LegacyFloat.ToInt64(LegacyFloat.Mul(LegacyFloat.FromDouble(LargeSample()), LegacyFloat.FromInt64(range))) + minValue));
		return ranValue;
	}

	public override double NextDouble()
	{
		double num = Sample();
		ranValue = LegacyFloat.ToInt32(LegacyFloat.Mul(LegacyFloat.FromDouble(num), LegacyFloat.FromInt32(100)));
		return num;
	}

	public override void NextBytes(byte[] buffer)
	{
		ranValue = 0;
		if (buffer == null) throw new ArgumentNullException(nameof(buffer));
		for (int i = 0; i < buffer.Length; i++) buffer[i] = (byte)(InternalSample() % 256);
	}

	public double NextPMDouble()
	{
		return LegacyFloat.ToDouble(LegacyFloat.Sub(
			LegacyFloat.Mul(LegacyFloat.FromInt32(2), LegacyFloat.FromDouble(NextDouble())), LegacyFloat.FromInt32(1)));
	}
}
