using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

// Same source runs on CLR2 x86 and .NET 8, invoking the actual game's MyRandom.
class RandomProbe
{
    [DllImport("msvcrt.dll", EntryPoint="_controlfp")]
    static extern uint ControlFp(uint value,uint mask);
    static StreamWriter output;
    static void Main(string[] args)
    {
        if(args.Length!=2) throw new ArgumentException("Expected game assembly path and output file");
        Assembly game=Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Random random=(Random)Activator.CreateInstance(game.GetType("Shooting.MyRandom",true),new object[]{0});
        MethodInfo pm=random.GetType().GetMethod("NextPMDouble");
        PropertyInfo ranValue=random.GetType().GetProperty("ranValue");
        Console.WriteLine("CLR="+Environment.Version+"; pointer="+IntPtr.Size);
        if(Environment.OSVersion.Platform==PlatformID.Win32NT)
            Console.WriteLine("RNG_FPU=0x"+ControlFp(0,0).ToString("X8"));
        using(output=new StreamWriter(args[1]))
            for(int i=0;i<10000;i++)
            {
                D("rng.next",i,random.Next());
                D("rng.next100",i,random.Next(100));
                D("rng.next360",i,random.Next(0,360));
                D("rng.double",i,random.NextDouble());
                D("rng.pm",i,(double)pm.Invoke(random,null));
                D("rng.ranValue",i,(int)ranValue.GetValue(random,null));
                D("rng.large",i,random.Next(Int32.MinValue,Int32.MaxValue));
                D("rng.nextMax",i,random.Next(Int32.MaxValue));
            }
        Console.WriteLine("RNG_BASELINE_COMPLETE");
    }
    static void D(string name,int index,double value)
    {
        output.WriteLine(name+","+index+",d,"+BitConverter.DoubleToInt64Bits(value).ToString("X16"));
    }
}
