using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using SlimDX;

// One source is compiled with the guest's CLR 2 compiler and with .NET 8.
// Values are recorded as bits so formatting and locale cannot hide differences.
class Probe
{
    static StreamWriter output;
    static void Main(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Expected output file and binary input file");
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        using (output = new StreamWriter(args[0]))
        using (BinaryReader inputs = new BinaryReader(File.OpenRead(args[1])))
        {
            Console.WriteLine("CLR=" + Environment.Version + "; pointer=" + IntPtr.Size);
            Console.WriteLine("Core=" + typeof(object).Assembly.FullName);
            Console.WriteLine("Math=" + typeof(Vector2).Assembly.FullName);
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                Console.WriteLine("FPU_CONTROL=0x" + ControlFp(0, 0).ToString("X8"));
            for (int i = 0; i < 4096; i++)
            {
                double a = inputs.ReadDouble();
                double b = inputs.ReadDouble();
                float x = inputs.ReadSingle();
                float y = inputs.ReadSingle();
                D("sin", i, Math.Sin(a));
                D("cos", i, Math.Cos(a));
                D("atan2", i, Math.Atan2(a, b));
                D("sqrt", i, Math.Sqrt(b));
                D("pow", i, Math.Pow(b, 1.5));
                F("vx", i, 3.7f * (float)Math.Cos(a));
                F("vy", i, 3.7f * (float)Math.Sin(a));
                F("move", i, Move(123.456f, 3.7f, a, 0.9f));
                D("distance", i, Distance(x, y));
                Vector2 v = new Vector2(x, y);
                F("vector2.length", i, v.Length());
                v.Normalize();
                F("vector2.normalize.x", i, v.X);
                F("vector2.normalize.y", i, v.Y);
                Matrix m = Matrix.RotationYawPitchRoll((float)a, (float)b, 0.37f);
                F("matrix.ypr.11", i, m.M11);
                F("matrix.ypr.12", i, m.M12);
                F("matrix.ypr.23", i, m.M23);
            }
            // Same formula as BaseObject.Move; no graphics, input or player data.
            for (int trajectory = 0; trajectory < 16; trajectory++)
            {
                float x = 123.456f, y = 234.567f;
                inputs.BaseStream.Position = trajectory * 24;
                double direction = inputs.ReadDouble();
                for (int frame = 0; frame < 10000; frame++)
                {
                    x = Move(x, 3.7f, direction, 0.9f);
                    y = Move(y, 3.7f, direction - Math.PI / 2.0, 1.1f);
                    F("trajectory.x." + trajectory, frame, x);
                    F("trajectory.y." + trajectory, frame, y);
                }
            }
            string[] decimalInputs = { "0.1", "1.23456789", "16777217", "3.4028234E38", "1.17549435E-38", "0.000000000000000000000000000000000000000000001" };
            for (int i = 0; i < decimalInputs.Length; i++)
                F("parse", i, float.Parse(decimalInputs[i], CultureInfo.InvariantCulture));
        }
        Console.WriteLine("OUTPUT=" + args[0]);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static float Move(float position, float velocity, double direction, float scale)
    {
        return (float)((double)position + (double)velocity * Math.Cos(direction) * (double)scale);
    }
    [System.Runtime.InteropServices.DllImport("msvcrt.dll", EntryPoint = "_controlfp")]
    static extern uint ControlFp(uint value, uint mask);
    [MethodImpl(MethodImplOptions.NoInlining)]
    static double Distance(float x, float y)
    {
        return Math.Sqrt(x * x + y * y);
    }
    static void D(string name, int index, double value)
    {
        output.WriteLine(name + "," + index + ",d," + BitConverter.DoubleToInt64Bits(value).ToString("X16"));
    }
    static void F(string name, int index, float value)
    {
        output.WriteLine(name + "," + index + ",f," + BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("X8"));
    }
}
