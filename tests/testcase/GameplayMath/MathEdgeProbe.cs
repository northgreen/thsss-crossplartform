using System;
class MathEdgeProbe
{
    static void Main()
    {
        Console.WriteLine("CLR="+Environment.Version+"; pointer="+IntPtr.Size);
        double[] infinities=new double[]{Double.PositiveInfinity,Double.NegativeInfinity};
        foreach(double y in infinities) foreach(double x in infinities)
            Console.WriteLine(BitConverter.DoubleToInt64Bits(y).ToString("X16")+","+BitConverter.DoubleToInt64Bits(x).ToString("X16")+","+BitConverter.DoubleToInt64Bits(Math.Atan2(y,x)).ToString("X16"));
    }
}
