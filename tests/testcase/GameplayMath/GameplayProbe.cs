using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;

// C# 2 / CLR2 compatible. All gameplay arithmetic stays in the loaded game.
public class GameplayProbe
{
    static readonly string[] Scenarios = new string[] {
        "constant", "acceleration", "angular", "negative", "destination",
        "binding", "collision", "components"
    };
    static readonly string[] FloatProperties = new string[] {
        "Velocity", "Accelerate", "Ax", "Ay", "AngularVelocityDegree",
        "DirectionVelocityDegree", "ScaleLength", "ScaleWidth", "ScaleVL",
        "ScaleVW", "ScaleX", "ScaleY", "Vx", "Vy"
    };
    static readonly string[] Dictionaries = new string[] {
        "AccelerateDictionary", "VelocityDictionary", "DirectionVelocityDegreeDictionary",
        "DirectionDegreeDictionary", "DestPointDictionary", "TransparentVelocityDictionary",
        "ScaleWidthVelocityDictionary", "ScaleLengthVelocityDictionary"
    };
    sealed class Assignment
    {
        public string Name;
        public object Value;
    }
    sealed class PointInput
    {
        public float X, Y;
    }
    sealed class Scheduled
    {
        public string Name;
        public int Time;
        public object Value;
    }
    sealed class Scenario
    {
        public Assignment[][] Initial;
        public Scheduled[] Events;
        public Assignment[][][] Frames;
    }
    sealed class Game
    {
        public Type ObjectType;
        public ConstructorInfo PointConstructor;
        public object Stage;
        public MethodInfo Ctrl, Distance, Direction, Hit, Binding;

        public object Point(PointInput point)
        {
            return PointConstructor.Invoke(new object[] { point.X, point.Y });
        }
        public object NewObject()
        {
            object value = Activator.CreateInstance(ObjectType);
            Set(value, "StageData", Stage);
            foreach (string name in Dictionaries)
                if (!(ObjectType.GetField(name).GetValue(value) is IDictionary))
                    throw new InvalidOperationException("Constructor did not initialize " + name);
            return value;
        }
        public object Get(object value, string name)
        {
            return ObjectType.GetProperty(name).GetValue(value, null);
        }
        public void Set(object value, string name, object input)
        {
            PointInput point = input as PointInput;
            ObjectType.GetProperty(name).SetValue(value, point == null ? input : Point(point), null);
        }
        public void Apply(object value, Assignment[] assignments)
        {
            foreach (Assignment assignment in assignments) Set(value, assignment.Name, assignment.Value);
        }
    }

    public static void Main(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("Expected game assembly, output CSV, inputs.bin");
        string assemblyPath = Path.GetFullPath(args[0]);
        string outputPath = Path.GetFullPath(args[1]);
        string inputPath = Path.GetFullPath(args[2]);
        if (outputPath == inputPath || outputPath == assemblyPath)
            throw new ArgumentException("Output must not overwrite the assembly or inputs");
        System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
        Scenario[] scenarios = ReadInputs(inputPath);
        Game game = LoadGame(assemblyPath);
        using (StreamWriter output = new StreamWriter(outputPath))
        {
            output.WriteLine("scenario,frame,time,x_f32,y_f32,velocity_f32,direction_f64,angle_f64,scaleLength_f32,scaleWidth_f32,vx_f32,vy_f32,distance_f64,targetDirection_f64,hit");
            for (int i = 0; i < scenarios.Length; i++) Run(game, scenarios[i], i, output);
        }
        Console.WriteLine("GAMEPLAY_CAPTURE_COMPLETE scenarios=8 frames=" + scenarios[0].Frames.Length);
    }

    static bool Contains(string[] names, string name)
    {
        return Array.IndexOf(names, name) >= 0;
    }
    static int Count(BinaryReader reader, int minimum, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < minimum || count > maximum) throw new InvalidDataException("Invalid input count: " + count);
        return count;
    }
    static float ReadFloat(BinaryReader reader)
    {
        float value = reader.ReadSingle();
        if (Single.IsNaN(value) || Single.IsInfinity(value) || value < -1000000f || value > 1000000f)
            throw new InvalidDataException("Input float outside finite fixture range");
        return value;
    }
    static PointInput ReadPoint(BinaryReader reader)
    {
        PointInput point = new PointInput();
        point.X = ReadFloat(reader); point.Y = ReadFloat(reader);
        return point;
    }
    static object ReadProperty(BinaryReader reader, string name)
    {
        if (Contains(FloatProperties, name)) return ReadFloat(reader);
        if (name == "OriginalPosition" || name == "DestPoint") return ReadPoint(reader);
        if (name == "Region") return Count(reader, 0, 100);
        if (name == "AngleWithDirection") return Count(reader, 0, 1) != 0;
        if (name != "Direction" && name != "Angle")
            throw new InvalidDataException("Unsupported input property: " + name);
        double value = reader.ReadDouble();
        if (Double.IsNaN(value) || Double.IsInfinity(value) || value < -1000.0 || value > 1000.0)
            throw new InvalidDataException("Input angle outside finite fixture range");
        return value;
    }
    static Assignment[] ReadAssignments(BinaryReader reader)
    {
        Assignment[] values = new Assignment[Count(reader, 0, 32)];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new Assignment();
            values[i].Name = reader.ReadString();
            values[i].Value = ReadProperty(reader, values[i].Name);
        }
        return values;
    }
    static Scheduled[] ReadEvents(BinaryReader reader, int frames)
    {
        Scheduled[] events = new Scheduled[Count(reader, 0, 64)];
        for (int i = 0; i < events.Length; i++)
        {
            Scheduled entry = new Scheduled();
            entry.Name = reader.ReadString();
            if (!Contains(Dictionaries, entry.Name)) throw new InvalidDataException("Unsupported dictionary: " + entry.Name);
            entry.Time = Count(reader, 1, frames);
            entry.Value = entry.Name == "DestPointDictionary" ? (object)ReadPoint(reader) : ReadFloat(reader);
            events[i] = entry;
        }
        return events;
    }
    static Scenario[] ReadInputs(string path)
    {
        using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
        {
            if (reader.ReadInt32() != 0x314D5047 || reader.ReadInt32() != 1)
                throw new InvalidDataException("Expected GameplayMath input format GPM1 version 1");
            int frames = Count(reader, 1000, 4096);
            if (reader.ReadInt32() != Scenarios.Length) throw new InvalidDataException("Expected eight scenarios");
            Scenario[] scenarios = new Scenario[Scenarios.Length];
            for (int i = 0; i < scenarios.Length; i++)
            {
                if (reader.ReadString() != Scenarios[i]) throw new InvalidDataException("Unexpected scenario order");
                Scenario scenario = new Scenario();
                scenario.Initial = new Assignment[3][];
                for (int j = 0; j < 3; j++) scenario.Initial[j] = ReadAssignments(reader);
                scenario.Events = ReadEvents(reader, frames);
                scenario.Frames = new Assignment[frames][][];
                for (int f = 0; f < frames; f++)
                {
                    scenario.Frames[f] = new Assignment[3][];
                    for (int j = 0; j < 3; j++) scenario.Frames[f][j] = ReadAssignments(reader);
                }
                scenarios[i] = scenario;
            }
            if (reader.BaseStream.Position != reader.BaseStream.Length) throw new InvalidDataException("Trailing input bytes");
            return scenarios;
        }
    }

    static Game LoadGame(string assemblyPath)
    {
        string directory = Path.GetDirectoryName(assemblyPath);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string file = Path.Combine(directory, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        Assembly assembly = Assembly.LoadFrom(assemblyPath);
        Game game = new Game();
        game.ObjectType = assembly.GetType("Shooting.BaseObject", true);
        Type stageType = assembly.GetType("Shooting.StageDataPackage", true);
        game.Stage = FormatterServices.GetUninitializedObject(stageType);
        FieldInfo bounds = stageType.GetField("BoundRect");
        bounds.SetValue(game.Stage, Activator.CreateInstance(bounds.FieldType, new object[] { 16, 24, 640, 480 }));
        Type pointType = game.ObjectType.GetProperty("OriginalPosition").PropertyType;
        game.PointConstructor = pointType.GetConstructor(new Type[] { typeof(float), typeof(float) });
        game.Ctrl = game.ObjectType.GetMethod("Ctrl", Type.EmptyTypes);
        game.Distance = game.ObjectType.GetMethod("GetDistance", new Type[] { game.ObjectType });
        game.Direction = game.ObjectType.GetMethod("GetDirection", new Type[] { game.ObjectType });
        game.Hit = game.ObjectType.GetMethod("HitCheck", new Type[] { game.ObjectType });
        game.Binding = game.ObjectType.GetMethod("SetBinding", new Type[] { game.ObjectType });
        Console.WriteLine("CLR=" + Environment.Version + "; pointer=" + IntPtr.Size);
        Console.WriteLine("GAME=" + assembly.FullName + "; MVID=" + assembly.ManifestModule.ModuleVersionId);
        return game;
    }
    static void Run(Game game, Scenario scenario, int index, StreamWriter output)
    {
        object[] objects = new object[] { game.NewObject(), game.NewObject(), game.NewObject() };
        for (int j = 0; j < 3; j++) game.Apply(objects[j], scenario.Initial[j]);
        foreach (Scheduled entry in scenario.Events)
        {
            IDictionary dictionary = (IDictionary)game.ObjectType.GetField(entry.Name).GetValue(objects[0]);
            PointInput point = entry.Value as PointInput;
            dictionary.Add(entry.Time, point == null ? entry.Value : game.Point(point));
        }
        // SetBinding intentionally executes one real Ctrl; its Time=1 state is recorded.
        if (index == 5) game.Binding.Invoke(objects[0], new object[] { objects[2] });
        WriteFrame(game, objects, index, -1, output);
        for (int frame = 0; frame < scenario.Frames.Length; frame++)
        {
            for (int j = 0; j < 3; j++) game.Apply(objects[j], scenario.Frames[frame][j]);
            if (index == 5) game.Ctrl.Invoke(objects[2], null);
            game.Ctrl.Invoke(objects[0], null);
            if ((int)game.Get(objects[0], "Time") != frame + 1 + (index == 5 ? 1 : 0))
                throw new InvalidOperationException("Ctrl did not advance exactly once");
            WriteFrame(game, objects, index, frame, output);
        }
        Console.WriteLine("CAPTURED " + Scenarios[index] + " frames=" + scenario.Frames.Length);
    }
    static string Bits(object value)
    {
        if (value is float)
        {
            float number = (float)value;
            if (Single.IsNaN(number) || Single.IsInfinity(number)) throw new InvalidOperationException("Nonfinite game float");
            return BitConverter.ToInt32(BitConverter.GetBytes(number), 0).ToString("X8");
        }
        double d = (double)value;
        if (Double.IsNaN(d) || Double.IsInfinity(d)) throw new InvalidOperationException("Nonfinite game double");
        return BitConverter.DoubleToInt64Bits(d).ToString("X16");
    }
    static void WriteFrame(Game game, object[] objects, int scenario, int frame, StreamWriter output)
    {
        object subject = objects[0], target = objects[1];
        object point = game.Get(subject, "OriginalPosition");
        Type pointType = point.GetType();
        output.Write(Scenarios[scenario] + "," + frame + "," + game.Get(subject, "Time"));
        output.Write("," + Bits(pointType.GetProperty("X").GetValue(point, null)));
        output.Write("," + Bits(pointType.GetProperty("Y").GetValue(point, null)));
        foreach (string name in new string[] { "Velocity", "Direction", "Angle", "ScaleLength", "ScaleWidth", "Vx", "Vy" })
            output.Write("," + Bits(game.Get(subject, name)));
        output.Write("," + Bits(game.Distance.Invoke(subject, new object[] { target })));
        output.Write("," + Bits(game.Direction.Invoke(subject, new object[] { target })));
        output.WriteLine("," + ((bool)game.Hit.Invoke(subject, new object[] { target }) ? "1" : "0"));
    }
}
