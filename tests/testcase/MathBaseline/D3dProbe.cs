using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SlimDX.Direct3D9;
class D3dProbe
{
    [DllImport("msvcrt.dll", EntryPoint="_controlfp")]
    static extern uint ControlFp(uint value, uint mask);
    [STAThread]
    static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception e) {
            Console.WriteLine("D3D_ERROR_TYPE=" + e.GetType().FullName);
            Console.WriteLine("D3D_ERROR_MESSAGE=" + e.Message);
            Console.WriteLine(e.ToString());
            Environment.ExitCode=1;
        }
    }
    static void Run(string[] args)
    {
        if (args.Length != 5)
            throw new ArgumentException("Expected Probe.exe, probe output CSV, inputs.bin, actual output CSV and random output CSV");
        if (Environment.Version.Major != 2 || IntPtr.Size != 4)
            throw new PlatformNotSupportedException("Historical capture requires Windows CLR2 x86");
        if (Environment.OSVersion.Platform != PlatformID.Win32NT ||
            !Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0)
            throw new PlatformNotSupportedException("D3D capture requires an interactive Windows desktop, not SSH/session 0");
        Console.WriteLine("CLR=" + Environment.Version + "; pointer=" + IntPtr.Size);
        Console.WriteLine("FPU_BEFORE=0x" + ControlFp(0,0).ToString("X8"));
        if ((ControlFp(0,0) & 0x30300u) != 0x10000u)
            throw new InvalidOperationException("Expected initial 53-bit round-to-nearest x87 mode");
        using (Form window = new Form())
        using (Direct3D d3d = new Direct3D())
        {
            if (d3d.Adapters.Count == 0)
                throw new InvalidOperationException("D3D has no adapters; run from an interactive Windows desktop");
            AdapterInformation adapter = d3d.Adapters.DefaultAdapter;
            SlimDX.Direct3D9.Capabilities caps = d3d.GetDeviceCaps(adapter.Adapter, DeviceType.Hardware);
            // Original flags deliberately omit FpuPreserve: CreateDevice changes x87 precision.
            CreateFlags flags = (caps.DeviceCaps & DeviceCaps.HWTransformAndLight) == DeviceCaps.HWTransformAndLight ? CreateFlags.HardwareVertexProcessing : CreateFlags.SoftwareVertexProcessing;
            PresentParameters pp = new PresentParameters();
            pp.BackBufferWidth=640; pp.BackBufferHeight=480;
            pp.BackBufferFormat=adapter.CurrentDisplayMode.Format;
            pp.DeviceWindowHandle=window.Handle; pp.Windowed=true;
            pp.PresentFlags=PresentFlags.DiscardDepthStencil;
            pp.SwapEffect=SwapEffect.Discard;
            Console.WriteLine("ADAPTER=" + adapter.Details.Description + "; FLAGS=" + flags);
            using (Device device = new Device(d3d, adapter.Adapter, DeviceType.Hardware, window.Handle, flags, pp))
            {
                Console.WriteLine("FPU_AFTER=0x" + ControlFp(0,0).ToString("X8"));
                if ((ControlFp(0,0) & 0x30300u) != 0x20000u)
                    throw new InvalidOperationException("Original D3D flags did not produce 24-bit round-to-nearest x87 mode");
                // CLR starts a new managed thread with its own floating environment.
                System.Threading.Thread thread=new System.Threading.Thread(delegate() {
                    Console.WriteLine("NEW_MANAGED_THREAD_FPU=0x" + ControlFp(0,0).ToString("X8"));
                });
                thread.Start(); thread.Join();
                Assembly probe = Assembly.LoadFrom(args[0]);
                probe.EntryPoint.Invoke(null, new object[] { new string[] {args[1],args[2]} });
                Assembly actual = Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(args[0]), "ActualProbe.exe"));
                actual.EntryPoint.Invoke(null, new object[] { new string[] { Path.Combine(Path.GetDirectoryName(args[0]), "THSSS.exe"), args[3], args[2] } });
                Assembly random=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(args[0]), "RandomProbe.exe"));
                random.EntryPoint.Invoke(null,new object[]{new string[]{Path.Combine(Path.GetDirectoryName(args[0]), "THSSS.exe"),args[4]}});
            }
        }
    }
}
