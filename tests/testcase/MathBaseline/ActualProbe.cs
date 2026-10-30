using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

// Invoke actual shipped/current BaseObject methods without constructors or Main.
// No assets, rendering, save files or game loop are accessed.
class ActualProbe
{
    static StreamWriter output;
    static void Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Expected assembly path, output file and binary input file");
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        string directory = Path.GetDirectoryName(Path.GetFullPath(args[0]));
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string file = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        Assembly assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        Type type = assembly.GetType("Shooting.BaseObject", true);
        object subject = FormatterServices.GetUninitializedObject(type);
        PropertyInfo position = type.GetProperty("OriginalPosition");
        Type point = position.PropertyType;
        ConstructorInfo pointConstructor = point.GetConstructor(new Type[] { typeof(float), typeof(float) });
        position.SetValue(subject, pointConstructor.Invoke(new object[] { 123.456f, 234.567f }), null);
        type.GetProperty("MaxVelocity").SetValue(subject, 1000f, null);
        type.GetProperty("Velocity").SetValue(subject, 3.7f, null);
        MethodInfo distance = type.GetMethod("GetDistance", new Type[] { point });
        MethodInfo direction = type.GetMethod("GetDirection", new Type[] { point });
        Console.WriteLine("CLR=" + Environment.Version + "; pointer=" + IntPtr.Size);
        Console.WriteLine("GAME=" + assembly.FullName + "; MVID=" + assembly.ManifestModule.ModuleVersionId);
        using (output = new StreamWriter(args[1]))
        using (BinaryReader inputs = new BinaryReader(File.OpenRead(args[2])))
        {
            for (int i = 0; i < 4096; i++)
            {
                double angle = inputs.ReadDouble();
                inputs.ReadDouble();
                float x = inputs.ReadSingle(), y = inputs.ReadSingle();
                object target = pointConstructor.Invoke(new object[] { x, y });
                D("actual.distance", i, (double)distance.Invoke(subject, new object[] { target }));
                D("actual.direction", i, (double)direction.Invoke(subject, new object[] { target }));
                type.GetProperty("Direction").SetValue(subject, Math.Abs(angle), null);
                F("actual.vx", i, (float)type.GetProperty("Vx").GetValue(subject, null));
                F("actual.vy", i, (float)type.GetProperty("Vy").GetValue(subject, null));
            }
        }
        Console.WriteLine("ACTUAL_BASELINE_COMPLETE");
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
