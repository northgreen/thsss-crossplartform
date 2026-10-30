using System;
using System.Globalization;
using System.IO;
using System.Reflection;

// Runs the actual private script interpolation overloads, not a copied formula.
public class ScriptProbe
{
    public static void Main(string[] args)
    {
        if(args.Length!=3) throw new ArgumentException("Expected game assembly, CSV output, MathBaseline inputs.bin");
        System.Threading.Thread.CurrentThread.CurrentCulture=CultureInfo.InvariantCulture;
        Assembly game=Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type type=game.GetType("Shooting.Execution",true);
        object execution=Activator.CreateInstance(type);
        BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        MethodInfo d=type.GetMethod("ChangeValue",flags,null,new Type[]{typeof(double)},null);
        MethodInfo f=type.GetMethod("ChangeValue",flags,null,new Type[]{typeof(float)},null);
        MethodInfo n=type.GetMethod("ChangeValue",flags,null,new Type[]{typeof(int)},null);
        if(d==null || f==null || n==null) throw new MissingMethodException(type.FullName,"ChangeValue overloads");
        Console.WriteLine("CLR="+Environment.Version+"; pointer="+IntPtr.Size);
        using(StreamWriter output=new StreamWriter(args[1]))
        using(BinaryReader input=new BinaryReader(File.OpenRead(args[2])))
        {
            if(input.BaseStream.Length!=98304) throw new InvalidDataException("Expected 4096 MathBaseline records");
            for(int i=0;i<4096;i++)
            {
                double angle=input.ReadDouble();
                input.ReadDouble();
                float x=input.ReadSingle(),y=input.ReadSingle();
                int duration=1+i%400;
                type.GetField("time").SetValue(execution,duration);
                type.GetField("ctime").SetValue(execution,1+i%duration);
                type.GetField("value").SetValue(execution,y);
                type.GetField("region").SetValue(execution,"17.125");
                for(int mode=0;mode<3;mode++)
                for(int change=0;change<3;change++)
                {
                    type.GetField("changetype").SetValue(execution,mode);
                    type.GetField("change").SetValue(execution,change);
                    string key="script."+mode+"."+change;
                    double dv=(double)d.Invoke(execution,new object[]{angle});
                    float fv=(float)f.Invoke(execution,new object[]{x});
                    int iv=(int)n.Invoke(execution,new object[]{i});
                    output.WriteLine(key+".double,"+i+",d,"+BitConverter.DoubleToInt64Bits(dv).ToString("X16"));
                    output.WriteLine(key+".float,"+i+",f,"+BitConverter.ToInt32(BitConverter.GetBytes(fv),0).ToString("X8"));
                    // Int32 is exactly representable in binary64; reuse the bit-pattern comparator.
                    output.WriteLine(key+".int,"+i+",d,"+BitConverter.DoubleToInt64Bits(iv).ToString("X16"));
                }
            }
        }
        Console.WriteLine("SCRIPT_CAPTURE_COMPLETE rows=110592");
    }
}
