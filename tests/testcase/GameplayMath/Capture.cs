using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SlimDX.Direct3D9;

// Capture only game state: D3D is used solely to reproduce the original thread precision.
class Capture
{
    [DllImport("msvcrt.dll", EntryPoint="_controlfp")]
    static extern uint ControlFp(uint value,uint mask);
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            if(args.Length!=9) throw new ArgumentException("Expected gameplay probe, inputs, before CSV, after CSV, script inputs, script prefix, conversion inputs, conversion prefix, random edge prefix");
            if(Environment.Version.Major!=2 || IntPtr.Size!=4) throw new PlatformNotSupportedException("Requires actual CLR2 x86");
            if(!Environment.UserInteractive || System.Diagnostics.Process.GetCurrentProcess().SessionId==0)
                throw new PlatformNotSupportedException("Requires interactive Windows desktop");
            string directory=Path.GetDirectoryName(Path.GetFullPath(args[0]));
            Assembly probe=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            Assembly script=Assembly.LoadFrom(Path.Combine(directory,"ScriptProbe.exe"));
            string game=Path.Combine(directory,"THSSS.exe");
            Console.WriteLine("CLR="+Environment.Version+"; pointer="+IntPtr.Size);
            Console.WriteLine("FPU_BEFORE=0x"+ControlFp(0,0).ToString("X8"));
            if((ControlFp(0,0)&0x30300u)!=0x10000u) throw new InvalidOperationException("Expected initial53bit nearest");
            Run(probe,script,game,args,53);
            using(Form window=new Form())
            using(Direct3D d3d=new Direct3D())
            {
                if(d3d.Adapters.Count==0) throw new InvalidOperationException("No D3D adapters");
                AdapterInformation adapter=d3d.Adapters.DefaultAdapter;
                SlimDX.Direct3D9.Capabilities caps=d3d.GetDeviceCaps(adapter.Adapter,DeviceType.Hardware);
                CreateFlags flags=(caps.DeviceCaps&DeviceCaps.HWTransformAndLight)==DeviceCaps.HWTransformAndLight
                    ? CreateFlags.HardwareVertexProcessing : CreateFlags.SoftwareVertexProcessing;
                PresentParameters pp=new PresentParameters();
                pp.BackBufferWidth=640; pp.BackBufferHeight=480; pp.BackBufferFormat=adapter.CurrentDisplayMode.Format;
                pp.DeviceWindowHandle=window.Handle; pp.Windowed=true;
                pp.PresentFlags=PresentFlags.DiscardDepthStencil; pp.SwapEffect=SwapEffect.Discard;
                Console.WriteLine("ADAPTER="+adapter.Details.Description+"; FLAGS="+flags);
                using(Device device=new Device(d3d,adapter.Adapter,DeviceType.Hardware,window.Handle,flags,pp))
                {
                    Console.WriteLine("FPU_AFTER=0x"+ControlFp(0,0).ToString("X8"));
                    if((ControlFp(0,0)&0x30300u)!=0x20000u) throw new InvalidOperationException("Expected postD3D24bit nearest");
                    Run(probe,script,game,args,24);
                }
            }
            Console.WriteLine("ACTUAL_GAMEPLAY_CAPTURE_PASS");
            return 0;
        }
        catch(Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
    }
    static void Run(Assembly probe,Assembly script,string game,string[] args,int precision)
    {
        probe.EntryPoint.Invoke(null,new object[]{new string[]{game,args[precision==53?2:3],args[1]}});
        script.EntryPoint.Invoke(null,new object[]{new string[]{game,args[5]+"-"+precision+".csv",args[4]}});
        Assembly conversion=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"ConversionProbe.exe"));
        conversion.EntryPoint.Invoke(null,new object[]{new string[]{args[6],args[7]+"-"+precision+".csv"}});
        Assembly random=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"RandomEdgeProbe.exe"));
        random.EntryPoint.Invoke(null,new object[]{new string[]{game,args[8]+"-"+precision+".csv"}});
    }
}
