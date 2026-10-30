using System;
using System.IO;
using System.Runtime.CompilerServices;

// Capture CLR2 conversion instructions explicitly; Convert.ToInt32 has a different contract.
class ConversionProbe
{
    public static void Main(string[] args)
    {
        if(args.Length!=2) throw new ArgumentException("Expected raw double inputs and output CSV");
        Console.WriteLine("CLR="+Environment.Version+"; pointer="+IntPtr.Size);
        using(BinaryReader input=new BinaryReader(File.OpenRead(args[0])))
        using(StreamWriter output=new StreamWriter(args[1]))
        {
            if(input.BaseStream.Length%8!=0) throw new InvalidDataException("Truncated double input");
            int index=0;
            while(input.BaseStream.Position<input.BaseStream.Length)
            {
                double value=input.ReadDouble();
                for(int mode=0;mode<14;mode++)
                {
                    string result;
                    try { result=Cast(mode,value); }
                    catch(OverflowException) { result="OverflowException"; }
                    output.WriteLine(mode+","+index+","+BitConverter.DoubleToInt64Bits(value).ToString("X16")+","+result);
                }
                index++;
            }
            Console.WriteLine("CONVERSION_CAPTURE_COMPLETE inputs="+index);
        }
    }
    static string Cast(int mode,double value)
    {
        switch(mode)
        {
            case 0:return I4(value).ToString("X8");
            case 1:return U4(value).ToString("X8");
            case 2:return I8(value).ToString("X16");
            case 3:return U8(value).ToString("X16");
            case 4:return U1(value).ToString("X2");
            case 5:return I1(value).ToString("X2");
            case 6:return I2(value).ToString("X4");
            case 7:return U2(value).ToString("X4");
            case 8:return checked((int)value).ToString("X8");
            case 9:return checked((uint)value).ToString("X8");
            case 10:return checked((long)value).ToString("X16");
            case 11:return checked((ulong)value).ToString("X16");
            case 12:return checked((byte)value).ToString("X2");
            case 13:return checked((sbyte)value).ToString("X2");
            default:throw new ArgumentOutOfRangeException("mode");
        }
    }
    [MethodImpl(MethodImplOptions.NoInlining)] static int I4(double v) { return unchecked((int)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static uint U4(double v) { return unchecked((uint)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static long I8(double v) { return unchecked((long)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static ulong U8(double v) { return unchecked((ulong)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static byte U1(double v) { return unchecked((byte)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static sbyte I1(double v) { return unchecked((sbyte)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static short I2(double v) { return unchecked((short)v); }
    [MethodImpl(MethodImplOptions.NoInlining)] static ushort U2(double v) { return unchecked((ushort)v); }
}
