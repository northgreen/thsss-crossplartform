using System;
using System.IO;
using System.Reflection;

// Exercise seeded state, byte consumption, invalid arguments and bookkeeping in the actual game type.
class RandomEdgeProbe
{
    public static void Main(string[] args)
    {
        if(args.Length!=2) throw new ArgumentException("Expected game assembly and output CSV");
        Type type=Assembly.LoadFrom(Path.GetFullPath(args[0])).GetType("Shooting.MyRandom",true);
        PropertyInfo recorded=type.GetProperty("ranValue");
        MethodInfo pm=type.GetMethod("NextPMDouble");
        int[] seeds=new int[]{0,1,-1,42,Int32.MaxValue,-Int32.MaxValue,Int32.MinValue};
        using(StreamWriter output=new StreamWriter(args[1]))
        foreach(int seed in seeds)
        {
            Random random;
            try { random=(Random)Activator.CreateInstance(type,new object[]{seed}); }
            catch(TargetInvocationException error)
            {
                output.WriteLine(seed+",-1,constructor,"+error.InnerException.GetType().Name+",-");
                continue;
            }
            for(int step=0;step<32;step++)
            for(int operation=0;operation<12;operation++)
            {
                string result;
                try
                {
                    switch(operation)
                    {
                        case 0:result=random.Next().ToString("X8");break;
                        case 1:result=random.Next(0).ToString("X8");break;
                        case 2:result=random.Next(-17,-17).ToString("X8");break;
                        case 3:result=random.Next(-1).ToString("X8");break;
                        case 4:result=random.Next(9,8).ToString("X8");break;
                        case 5:
                            byte[] bytes=new byte[17];random.NextBytes(bytes);result=BitConverter.ToString(bytes).Replace("-","");break;
                        case 6:random.NextBytes(new byte[0]);result="empty";break;
                        case 7:random.Next();random.NextBytes(null);result="unexpected";break;
                        case 8:result=BitConverter.DoubleToInt64Bits(random.NextDouble()).ToString("X16");break;
                        case 9:result=BitConverter.DoubleToInt64Bits((double)pm.Invoke(random,null)).ToString("X16");break;
                        case 10:result=random.Next(Int32.MinValue,Int32.MaxValue).ToString("X8");break;
                        case 11:result=random.Next(Int32.MaxValue).ToString("X8");break;
                        default:throw new InvalidOperationException("Unknown operation");
                    }
                }
                catch(ArgumentException error) { result=error.GetType().Name+":"+error.ParamName; }
                output.WriteLine(seed+","+step+","+operation+","+result+","+recorded.GetValue(random,null));
            }
        }
        Console.WriteLine("RANDOM_EDGES_CAPTURE_COMPLETE");
    }
}
