using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
class SolutionsProbe
{
    [DllImport("legacy_x87")] static extern double legacy_sin(double x);
    [DllImport("legacy_x87")] static extern double legacy_cos(double x);
    [DllImport("legacy_x87")] static extern double legacy_atan2(double y, double x);
    [DllImport("legacy_x87")] static extern double legacy_direction(float px, float py, float tx, float ty);
    [DllImport("legacy_x87")] static extern double legacy_distance(float px, float py, float tx, float ty);
    [DllImport("legacy_x87")] static extern double legacy_sqrt(double x);
    [DllImport("legacy_x87")] static extern int legacy_precision(int bits);
    [DllImport("legacy_x87")] static extern float legacy_move(float position,float velocity,double direction,float scale);
    static int precision;
    static Dictionary<string,string> expected = new Dictionary<string,string>();
    static Dictionary<string,int> failures = new Dictionary<string,int>();
    static Dictionary<string,int> totals = new Dictionary<string,int>();
    static void Main(string[] args)
    {
        precision=args.Length>1 ? int.Parse(args[1]) : 53;
        if(legacy_precision(precision)!=0) throw new ArgumentException("Precision must be 24 or 53");
        string suffix=precision==24 ? "-d3d" : "";
        foreach (string file in new string[]{"windows-clr2"+suffix+"-x86.csv", "actual-windows-clr2"+suffix+"-x86.csv", "random-windows-clr2"+suffix+"-x86.csv"})
            foreach (string row in File.ReadLines(Path.Combine(args[0],file)))
            { string[] p=row.Split(','); expected[p[0]+","+p[1]]=p[3]; }
        using (BinaryReader input=new BinaryReader(File.OpenRead(Path.Combine(args[0],"inputs.bin"))))
            for(int i=0;i<4096;i++)
            {
                double a=input.ReadDouble(), b=input.ReadDouble();
                float x=input.ReadSingle(), y=input.ReadSingle();
                D("sin",i,legacy_sin(a)); D("cos",i,legacy_cos(a)); D("atan2",i,legacy_atan2(a,b));
                D("sqrt",i,legacy_sqrt(b));
                D("distance",i,legacy_distance(x,y,0,0));
                float dx=123.456f,dy=234.567f;
                D("actual.distance",i,legacy_distance(dx,dy,x,y));
                D("actual.direction",i,legacy_direction(dx,dy,x,y));
                double normalized=Math.Abs(a);
                while(normalized>Math.PI*2) normalized=R(normalized-Math.PI*2);
                F("actual.vx",i,3.7f*(float)legacy_cos(normalized));
                F("actual.vy",i,3.7f*(float)legacy_sin(normalized));
                F("vx",i,3.7f*(float)legacy_cos(a)); F("vy",i,3.7f*(float)legacy_sin(a));
                F("move",i,legacy_move(123.456f,3.7f,a,0.9f));
                float length=(float)legacy_distance(x,y,0,0);
                F("vector2.length",i,length);
                float reciprocal=(float)R(1.0/length);
                F("vector2.normalize.x",i,(float)R((double)x*reciprocal));
                F("vector2.normalize.y",i,(float)R((double)y*reciprocal));
                Matrix(i,(float)a,(float)b,0.37f);
            }
        using (BinaryReader input=new BinaryReader(File.OpenRead(Path.Combine(args[0],"inputs.bin"))))
            for(int trajectory=0;trajectory<16;trajectory++)
            {
                input.BaseStream.Position=trajectory*24;
                double direction=input.ReadDouble();
                float x=123.456f,y=234.567f;
                for(int frame=0;frame<10000;frame++)
                {
                    x=legacy_move(x,3.7f,direction,0.9f);
                    y=legacy_move(y,3.7f,R(direction-Math.PI/2),1.1f);
                    F("trajectory.x."+trajectory,frame,x);
                    F("trajectory.y."+trajectory,frame,y);
                }
            }
        CheckRandom();
        int total=0,failed=0;
        foreach(var p in totals) { int f=failures.ContainsKey(p.Key)?failures[p.Key]:0; total+=p.Value;failed+=f; Console.WriteLine(p.Key+": "+f+" / "+p.Value+" different"); }
        Console.WriteLine("TOTAL="+total+"; DIFFERENT="+failed);
        if(failed!=0) Environment.ExitCode=1;
    }
    static void D(string n,int i,double v) { Check(n,i,BitConverter.DoubleToInt64Bits(v).ToString("X16")); }
    static void CheckRandom()
    {
        var random=new LegacyRandomModel(0,R);
        for(int i=0;i<10000;i++)
        {
            D("rng.next",i,random.Next());
            D("rng.next100",i,random.Next(100));
            D("rng.next360",i,random.Next(0,360));
            D("rng.double",i,random.NextDouble());
            D("rng.pm",i,random.NextPMDouble());
            D("rng.ranValue",i,random.RanValue);
            D("rng.large",i,random.Next(int.MinValue,int.MaxValue));
            D("rng.nextMax",i,random.Next(int.MaxValue));
        }
    }
    static double R(double x) { return precision==24 ? (double)(float)x : x; }
    static double Mul(double x,double y) { return R(x*y); }
    static double Add(double x,double y) { return R(x+y); }
    static double Sub(double x,double y) { return R(x-y); }
    static void Matrix(int i,float yaw,float pitch,float roll)
    {
        float hr=roll*.5f,hp=pitch*.5f,hy=yaw*.5f;
        float sr=(float)legacy_sin(hr),cr=(float)legacy_cos(hr);
        float sp=(float)legacy_sin(hp),cp=(float)legacy_cos(hp);
        float sy=(float)legacy_sin(hy),cy=(float)legacy_cos(hy);
        double a=Mul(cy,sp),b=Mul(sy,cp),c=Mul(cy,cp),d=Mul(sy,sp);
        float x=(float)Add(Mul(sr,b),Mul(cr,a));
        float y=(float)Sub(Mul(cr,b),Mul(sr,a));
        float z=(float)Sub(Mul(sr,c),Mul(cr,d));
        float w=(float)Add(Mul(sr,d),Mul(cr,c));
        float xx=(float)Mul(x,x),yy=(float)Mul(y,y),zz=(float)Mul(z,z);
        float yx=(float)Mul(y,x),wz=(float)Mul(w,z),zy=(float)Mul(z,y),wx=(float)Mul(w,x);
        F("matrix.ypr.11",i,(float)Sub(1,Mul(Add(zz,yy),2)));
        F("matrix.ypr.12",i,(float)Mul(Add(wz,yx),2));
        F("matrix.ypr.23",i,(float)Mul(Add(wx,zy),2));
    }
    static void F(string n,int i,float v) { Check(n,i,BitConverter.ToInt32(BitConverter.GetBytes(v),0).ToString("X8")); }
    static void Check(string n,int i,string bits)
    {
        if(!totals.ContainsKey(n))totals[n]=0;
        totals[n]++;
        if(expected[n+","+i]!=bits) {if(!failures.ContainsKey(n))failures[n]=0;failures[n]++;}
    }
}
