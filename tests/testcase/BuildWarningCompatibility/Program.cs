using System.ComponentModel;
using System.Reflection;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

internal static class Program
{
    private static int callbacks;
    private const BindingFlags DeclaredMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static void Main()
    {
        Type[] types = { typeof(Control), typeof(Form), typeof(ListControl), typeof(NumericUpDown), typeof(CheckBox), typeof(ComboBox), typeof(ListBox), typeof(TreeView), typeof(TrackBar), typeof(ScrollBar), typeof(Timer) };
        int events = 0;
        foreach (Type type in types)
        {
            var instance = Activator.CreateInstance(type);
            Check(StorageField(instance).GetValue(instance) == null, $"{type.Name}: storage must be lazy");
            foreach (EventInfo eventInfo in type.GetEvents(DeclaredMembers))
            {
                if (type.GetField(eventInfo.Name, DeclaredMembers) != null) continue;
                CheckSubscription(instance, eventInfo);
                events++;
            }
            ((IDisposable)instance).Dispose();
        }
        Check(events == 31, $"Expected 31 placeholder events, found {events}");
        CheckHiddenEvents(new ComboBox());
        CheckHiddenEvents(new ListBox());
        CheckNoAutomaticCallbacks();
        Check(callbacks == 0, "Placeholder events fired during subscription, property changes, or disposal");
        CheckDispatch();
        CheckAssemblyBoundaries();
    }

    private static void CheckAssemblyBoundaries()
    {
        Assembly game = typeof(Control).Assembly;
        Check(!game.GetTypes().Any(type => type.Namespace == "System.ComponentModel" || type.Namespace?.StartsWith("System.ComponentModel.", StringComparison.Ordinal) == true), "THSSS still defines BCL ComponentModel types");
        Check(typeof(ComponentResourceManager).Assembly != game && typeof(IContainer).Assembly != game && typeof(EventHandlerList).Assembly != game, "ComponentModel bindings must resolve to the BCL");
        string[] resources = game.GetManifestResourceNames();
        Check(!resources.Contains("Shooting.Properties.Resources.resources"), "Unused designer resources are still embedded");
        Check(resources.Contains("Shooting.RenderForm_Main.resources"), "Main form resource manifest is missing");
        var form = new Shooting.RenderForm_Main();
        Check(form.Name == "RenderForm_Main" && form.ClientSize == new System.Drawing.Size(640, 480), "Main form initialization changed");
        // The existing main-form resx contains no data entries, including no icon.
        Check(form.Icon == null, "Empty main form resources unexpectedly supplied an icon");
        // RenderForm_Main's existing Dispose override recursively calls itself; construction must not exercise it.
    }

    private static void CheckSubscription(object instance, EventInfo eventInfo)
    {
        Delegate first = Delegate.CreateDelegate(eventInfo.EventHandlerType, typeof(Program).GetMethod(nameof(First), BindingFlags.Static | BindingFlags.NonPublic));
        Delegate second = Delegate.CreateDelegate(eventInfo.EventHandlerType, typeof(Program).GetMethod(nameof(Second), BindingFlags.Static | BindingFlags.NonPublic));
        eventInfo.AddEventHandler(instance, first);
        eventInfo.AddEventHandler(instance, first);
        eventInfo.AddEventHandler(instance, second);
        CheckHandlers(instance, eventInfo, first, first, second);
        eventInfo.RemoveEventHandler(instance, first);
        CheckHandlers(instance, eventInfo, first, second);
        eventInfo.RemoveEventHandler(instance, second);
        CheckHandlers(instance, eventInfo, first);
        eventInfo.RemoveEventHandler(instance, first);
        eventInfo.RemoveEventHandler(instance, first);
        CheckHandlers(instance, eventInfo);
        eventInfo.AddEventHandler(instance, first);
    }

    private static FieldInfo StorageField(object instance) =>
        (instance is Control ? typeof(Control) : typeof(Timer)).GetField("_compatibilityEvents", DeclaredMembers)
        ?? throw new InvalidOperationException("THSSS is stale: rebuild the parent project with the updated WinFormsStubs before running this fixture");

    private static void CheckHandlers(object instance, EventInfo eventInfo, params Delegate[] expected)
    {
        var storage = (EventHandlerList)StorageField(instance).GetValue(instance);
        string key = string.Intern(eventInfo.DeclaringType.Name + "." + eventInfo.Name);
        Delegate[] actual = storage[key]?.GetInvocationList() ?? Array.Empty<Delegate>();
        Check(actual.SequenceEqual(expected), $"{key}: subscription order/removal changed");
    }

    private static void CheckHiddenEvents(ListControl control)
    {
        EventInfo baseEvent = typeof(ListControl).GetEvent(nameof(ListControl.SelectedIndexChanged), DeclaredMembers);
        EventInfo hiddenEvent = control.GetType().GetEvent(nameof(ListControl.SelectedIndexChanged), DeclaredMembers);
        EventHandler first = First;
        EventHandler second = Second;
        baseEvent.AddEventHandler(control, first);
        hiddenEvent.AddEventHandler(control, second);
        CheckHandlers(control, baseEvent, first);
        CheckHandlers(control, hiddenEvent, second);
        hiddenEvent.RemoveEventHandler(control, second);
        CheckHandlers(control, baseEvent, first);
        control.Dispose();
    }

    private static void CheckNoAutomaticCallbacks()
    {
        using var control = new Control();
        control.Click += First;
        control.Load += First;
        control.Disposed += First;
        control.TextChanged += First;
        control.Text = "changed";
        control.Show();
        control.Hide();
        control.Refresh();
        using var checkBox = new CheckBox();
        checkBox.CheckedChanged += First;
        checkBox.Checked = true;
        using var trackBar = new TrackBar();
        trackBar.ValueChanged += First;
        trackBar.Value = 42;
        using var timer = new Timer();
        timer.Tick += First;
        timer.Interval = 1;
        timer.Start();
        timer.Stop();
    }

    private static void CheckDispatch()
    {
        using var control = new Control();
        var observed = new List<string>();
        KeyEventHandler down = (sender, args) => { Check(ReferenceEquals(sender, control) && args.KeyCode == Keys.Z, "KeyDown payload changed"); observed.Add("down"); };
        control.KeyDown += down;
        control.KeyUp += (_, args) => { Check(args.KeyCode == Keys.Z, "KeyUp payload changed"); observed.Add("up"); };
        var mouse = new MouseEventArgs(MouseButtons.Left, 1, 12, 34);
        control.MouseMove += (_, args) => { Check(ReferenceEquals(args, mouse), "MouseMove payload changed"); observed.Add("move"); };
        control.MouseDown += (_, args) => { Check(ReferenceEquals(args, mouse), "MouseDown payload changed"); observed.Add("press"); };
        control.MouseUp += (_, args) => { Check(ReferenceEquals(args, mouse), "MouseUp payload changed"); observed.Add("release"); };
        control.Resize += (_, _) => observed.Add("resize");
        control.SizeChanged += (_, _) => observed.Add("size");
        control.Paint += (_, args) => { Check(args.ClipRectangle.IsEmpty, "Paint payload changed"); observed.Add("paint"); };
        control.DispatchKey(Keys.Z, true);
        control.DispatchKey(Keys.Z, false);
        control.DispatchMouse(mouse, 0);
        control.DispatchMouse(mouse, 1);
        control.DispatchMouse(mouse, 2);
        control.DispatchResize();
        control.DispatchPaint();
        Check(observed.SequenceEqual(new[] { "down", "up", "move", "press", "release", "resize", "size", "paint" }), "Dispatch order changed");
        control.KeyDown -= down;
        control.DispatchKey(Keys.Z, true);
        control.Dispose();
        Check(observed.Count == 8, "Unsubscription or Dispose produced a callback");
        Console.WriteLine("PASS BuildWarningCompatibility: 31 event APIs, dispatch, BCL types and resource boundaries");
    }

    private static void First(object sender, EventArgs args) => callbacks++;
    private static void Second(object sender, EventArgs args) => callbacks++;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
