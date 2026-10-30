using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SlimDX.Direct3D9;
class ParseCapture
{
    [DllImport("msvcrt.dll",EntryPoint="_controlfp")] static extern uint ControlFp(uint value,uint mask);
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if((args.Length!=4 && args.Length!=5) || Environment.Version.Major!=2 || IntPtr.Size!=4) throw new ArgumentException("Requires CLR2 x86: probe, input, before TSV, after TSV, optional class name");
            Assembly probe=Assembly.LoadFrom(args[0]);
            MethodInfo run=probe.GetType(args.Length==5?args[4]:"ParseProbe",true).GetMethod("Run");
            Console.WriteLine("BEFORE=0x"+ControlFp(0,0).ToString("X8"));
            run.Invoke(null,new object[]{args[1],args[2],"before-d3d"});
            using(Form form=new Form()) using(Direct3D d3d=new Direct3D())
            {
                AdapterInformation adapter=d3d.Adapters.DefaultAdapter;
                SlimDX.Direct3D9.Capabilities caps=d3d.GetDeviceCaps(adapter.Adapter,DeviceType.Hardware);
                CreateFlags flags=(caps.DeviceCaps&DeviceCaps.HWTransformAndLight)==DeviceCaps.HWTransformAndLight ? CreateFlags.HardwareVertexProcessing : CreateFlags.SoftwareVertexProcessing;
                PresentParameters parameters=new PresentParameters(); parameters.BackBufferWidth=640;parameters.BackBufferHeight=480;
                parameters.BackBufferFormat=adapter.CurrentDisplayMode.Format;parameters.DeviceWindowHandle=form.Handle;parameters.Windowed=true;
                parameters.PresentFlags=PresentFlags.DiscardDepthStencil;parameters.SwapEffect=SwapEffect.Discard;
                using(Device device=new Device(d3d,adapter.Adapter,DeviceType.Hardware,form.Handle,flags,parameters))
                {
                    Console.WriteLine("AFTER=0x"+ControlFp(0,0).ToString("X8"));
                    if((ControlFp(0,0)&0x30300u)!=0x20000u) throw new InvalidOperationException("Expected postD3D24bit");
                    run.Invoke(null,new object[]{args[1],args[3],"after-d3d"});
                }
            }
            Console.WriteLine("ACTUAL_ASSET_PARSE_CAPTURE_PASS");return 0;
        }
        catch(Exception error) { Console.Error.WriteLine(error.ToString());return 1; }
    }
}
