// WinForms stubs for cross-platform compatibility (single source of truth)
using System;
using System.ComponentModel;

namespace System.Windows.Forms
{
    // ---- Enums ----
    public enum AnchorStyles { None = 0, Top = 1, Bottom = 2, Left = 4, Right = 8 }
    public enum DockStyle { None = 0, Top = 1, Bottom = 2, Left = 3, Right = 4, Fill = 5 }
    public enum BorderStyle { None = 0, FixedSingle = 1, Fixed3D = 2 }
    public enum BackgroundImageLayout { Tile = 0, Center = 1, Stretch = 2, Zoom = 3, None = 4 }
    public enum FormStartPosition { Manual = 0, CenterScreen = 1, WindowsDefaultLocation = 2, WindowsDefaultBounds = 3, CenterParent = 4 }
    public enum FormBorderStyle { None = 0, FixedSingle = 1, Fixed3D = 2, FixedDialog = 3, Sizable = 4, FixedToolWindow = 5, SizableToolWindow = 6 }
    public enum FormWindowState { Normal = 0, Minimized = 1, Maximized = 2 }
    public enum DialogResult { None = 0, OK = 1, Cancel = 2, Abort = 3, Retry = 4, Ignore = 5, Yes = 6, No = 7 }
    public enum MessageBoxButtons { OK = 0, OKCancel = 1, AbortRetryIgnore = 2, YesNoCancel = 3, YesNo = 4, RetryCancel = 5 }
    public enum MessageBoxIcon { None = 0, Hand = 16, Question = 32, Exclamation = 48, Asterisk = 64, Stop = 16, Error = 16, Warning = 48, Information = 64 }
    public enum MouseButtons { None = 0, Left = 1048576, Right = 2097152, Middle = 4194304 }

    public enum Keys
    {
        None = 0, Back = 8, Tab = 9, Enter = 13, Return = 13, ShiftKey = 16, ControlKey = 17, Menu = 18, Pause = 19, CapsLock = 20, Escape = 27, Space = 32,
        PageUp = 33, PageDown = 34, End = 35, Home = 36, Left = 37, Up = 38, Right = 39, Down = 40,
        PrintScreen = 44, Insert = 45, Delete = 46,
        D0 = 48, D1 = 49, D2 = 50, D3 = 51, D4 = 52, D5 = 53, D6 = 54, D7 = 55, D8 = 56, D9 = 57,
        A = 65, B = 66, C = 67, D = 68, E = 69, F = 70, G = 71, H = 72, I = 73, J = 74, K = 75,
        L = 76, M = 77, N = 78, O = 79, P = 80, Q = 81, R = 82, S = 83, T = 84, U = 85, V = 86,
        W = 87, X = 88, Y = 89, Z = 90,
        LWin = 91, RWin = 92,
        NumPad0 = 96, NumPad1 = 97, NumPad2 = 98, NumPad3 = 99, NumPad4 = 100,
        NumPad5 = 101, NumPad6 = 102, NumPad7 = 103, NumPad8 = 104, NumPad9 = 105,
        Multiply = 106, Add = 107, Separator = 108, Subtract = 109, Decimal = 110, Divide = 111,
        F1 = 112, F2 = 113, F3 = 114, F4 = 115, F5 = 116, F6 = 117, F7 = 118, F8 = 119,
        F9 = 120, F10 = 121, F11 = 122, F12 = 123,
        NumLock = 144, Scroll = 145,
        LShiftKey = 160, RShiftKey = 161, LControlKey = 162, RControlKey = 163, LMenu = 164, RMenu = 165,
        OemSemicolon = 186, Oemplus = 187, Oemcomma = 188, OemMinus = 189,
        OemPeriod = 190, OemQuestion = 191, Oemtilde = 192,
        OemOpenBrackets = 219, OemPipe = 220, OemCloseBrackets = 221, OemQuotes = 222,
    }

    // ---- Event args & delegates ----
    public class KeyEventArgs : EventArgs
    {
        public KeyEventArgs() { }
        public KeyEventArgs(Keys keyCode) { KeyCode = KeyData = keyCode; KeyValue = (int)keyCode; }
        public Keys KeyCode { get; set; }
        public Keys KeyData { get; set; }
        public int KeyValue { get; set; }
        public bool Shift { get; set; }
        public bool Alt { get; set; }
        public bool Control { get; set; }
        public bool Handled { get; set; }
        public bool SuppressKeyPress { get; set; }
    }

    public class KeyPressEventArgs : EventArgs
    {
        public char KeyChar { get; set; }
        public bool Handled { get; set; }
    }

    public class MouseEventArgs : EventArgs
    {
        public MouseEventArgs() { }
        public MouseEventArgs(MouseButtons button, int clicks, int x, int y)
        { Button = button; Clicks = clicks; X = x; Y = y; }
        public MouseEventArgs(int button, int clicks, int x, int y)
        { Button = (MouseButtons)button; Clicks = clicks; X = x; Y = y; }
        public int X { get; set; }
        public int Y { get; set; }
        public MouseButtons Button { get; set; }
        public int Clicks { get; set; }
        public int Delta { get; set; }
    }

    public class PaintEventArgs : EventArgs
    {
        public PaintEventArgs() { }
        public PaintEventArgs(System.Drawing.Graphics graphics, System.Drawing.Rectangle clipRect)
        { Graphics = graphics; ClipRectangle = clipRect; }
        public System.Drawing.Graphics Graphics { get; set; }
        public System.Drawing.Rectangle ClipRectangle { get; set; }
    }

    public class FormClosingEventArgs : EventArgs
    {
        public bool Cancel { get; set; }
        public CloseReason CloseReason { get; set; }
    }

    public class FormClosedEventArgs : EventArgs
    {
        public CloseReason CloseReason { get; set; }
    }

    public enum CloseReason { None = 0, WindowsShutDown = 1, MdiFormClosing = 2, UserClosing = 3, TaskManagerClosing = 4, FormOwnerClosing = 5, ApplicationExitCall = 6 }

    // EventHandler is System.EventHandler - do not redefine
    public delegate void KeyEventHandler(object sender, KeyEventArgs e);
    public delegate void KeyPressEventHandler(object sender, KeyPressEventArgs e);
    public delegate void MouseEventHandler(object sender, MouseEventArgs e);
    public delegate void PaintEventHandler(object sender, PaintEventArgs e);
    public delegate void FormClosingEventHandler(object sender, FormClosingEventArgs e);

    // ---- Layout helper types ----
    public struct Padding
    {
        public int Left, Top, Right, Bottom;
        public Padding(int all) { Left = Top = Right = Bottom = all; }
        public Padding(int left, int top, int right, int bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }
        public static Padding Empty => new Padding(0);
    }

    // NOTE: Cursor is declared in AdditionalTypes.cs (System.Drawing namespace) - do not duplicate.

    // ---- Control hierarchy ----
    public class Control : IDisposable
    {
        private bool _disposed;
        // Designer-only events retain subscriptions without inventing new runtime dispatch.
        private EventHandlerList _compatibilityEvents;
        protected EventHandlerList CompatibilityEvents => _compatibilityEvents ??= new EventHandlerList();

        public string Name { get; set; }
        public virtual string Text { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Right => Left + Width;
        public int Bottom => Top + Height;
        public System.Drawing.Point Location { get; set; }
        public System.Drawing.Size Size { get; set; }
        public System.Drawing.Size MinimumSize { get; set; }
        public System.Drawing.Size MaximumSize { get; set; }
        public virtual System.Drawing.Size ClientSize { get; set; }
        public System.Drawing.Color BackColor { get; set; }
        public System.Drawing.Color ForeColor { get; set; }
        public System.Drawing.Font Font { get; set; }
        public virtual bool Visible { get; set; }
        public bool Enabled { get; set; }
        public virtual bool Focused => false;
        public bool IsDisposed => _disposed;
        public Control Parent { get; set; }
        public IntPtr Handle { get; set; }
        public object Tag { get; set; }
        public int TabIndex { get; set; }
        public bool TabStop { get; set; }
        public AnchorStyles Anchor { get; set; }
        public DockStyle Dock { get; set; }
        public Padding Margin { get; set; }
        public Padding Padding { get; set; }
        public ControlCollection Controls { get; set; }
        public object BackgroundImage { get; set; }
        public SlimDX.Direct3D9.ImageLayout BackgroundImageLayout { get; set; }
        public BorderStyle BorderStyle { get; set; }
        public object Cursor { get; set; }
        public bool UseWaitCursor { get; set; }
        public bool AllowDrop { get; set; }
        public ContextMenuStrip ContextMenuStrip { get; set; }
        public bool AutoSize { get; set; }
        public bool AutoScroll { get; set; }
        public bool DoubleBuffered { get; set; }
        public bool ShowFocusCues => false;
        public object Region { get; set; }

        public Control() { Controls = new ControlCollection(this); Enabled = true; Visible = true; }

        public void Focus() { }
        public void Hide() { Visible = false; }
        public void Show() { Visible = true; }
        public void BringToFront() { }
        public void SendToBack() { }
        public void Invalidate() { }
        public void Invalidate(bool invalidateChildren) { }
        public void Update() { }
        public void Refresh() { }
        public void PerformLayout() { }
        public void ResumeLayout(bool performLayout) { }
        public void SuspendLayout() { }
        public System.Drawing.Graphics CreateGraphics()
            => System.Drawing.Graphics.FromImage(new System.Drawing.Bitmap(Width > 0 ? Width : 1, Height > 0 ? Height : 1));
        public System.Drawing.Point PointToClient(System.Drawing.Point p) => p;
        public System.Drawing.Point PointToScreen(System.Drawing.Point p) => p;

        protected internal virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing) { /* release managed resources */ }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void DispatchKey(Keys key, bool down)
        {
            var args = new KeyEventArgs(key);
            if (down) KeyDown?.Invoke(this, args);
            else KeyUp?.Invoke(this, args);
        }
        public void DispatchResize() { Resize?.Invoke(this, EventArgs.Empty); SizeChanged?.Invoke(this, EventArgs.Empty); }
        public void DispatchMouse(MouseEventArgs args, int kind)
        {
            if (kind == 0) MouseMove?.Invoke(this, args);
            else if (kind == 1) MouseDown?.Invoke(this, args);
            else MouseUp?.Invoke(this, args);
        }
        public void DispatchPaint() => Paint?.Invoke(this, new PaintEventArgs(null, System.Drawing.Rectangle.Empty));

        public event EventHandler Click { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(Click), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(Click), value); }
        public event EventHandler DoubleClick { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(DoubleClick), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(DoubleClick), value); }
        public event EventHandler MouseEnter { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(MouseEnter), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(MouseEnter), value); }
        public event EventHandler MouseLeave { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(MouseLeave), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(MouseLeave), value); }
        public event MouseEventHandler MouseMove;
        public event MouseEventHandler MouseDown;
        public event MouseEventHandler MouseUp;
        public event MouseEventHandler MouseWheel { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(MouseWheel), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(MouseWheel), value); }
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event KeyPressEventHandler KeyPress { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(KeyPress), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(KeyPress), value); }
        public event PaintEventHandler Paint;
        public event EventHandler Resize;
        public event EventHandler Move { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(Move), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(Move), value); }
        public event EventHandler GotFocus { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(GotFocus), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(GotFocus), value); }
        public event EventHandler LostFocus { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(LostFocus), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(LostFocus), value); }
        public event EventHandler EnabledChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(EnabledChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(EnabledChanged), value); }
        public event EventHandler VisibleChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(VisibleChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(VisibleChanged), value); }
        public event EventHandler TextChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(TextChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(TextChanged), value); }
        public event EventHandler SizeChanged;
        public event EventHandler LocationChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(LocationChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(LocationChanged), value); }
        public event EventHandler BackColorChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(BackColorChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(BackColorChanged), value); }
        public event EventHandler ForeColorChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(ForeColorChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(ForeColorChanged), value); }
        public event EventHandler FontChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(FontChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(FontChanged), value); }
        public event EventHandler ParentChanged { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(ParentChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(ParentChanged), value); }
        public event EventHandler HandleCreated { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(HandleCreated), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(HandleCreated), value); }
        public event EventHandler HandleDestroyed { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(HandleDestroyed), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(HandleDestroyed), value); }
        public event EventHandler Disposed { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(Disposed), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(Disposed), value); }
        public event EventHandler Load { add => CompatibilityEvents.AddHandler(nameof(Control) + "." + nameof(Load), value); remove => CompatibilityEvents.RemoveHandler(nameof(Control) + "." + nameof(Load), value); }
    }

    public class ControlCollection
    {
        private Control _owner;
        public ControlCollection(Control owner) { _owner = owner; }
        public void Add(Control control) { }
        public void AddRange(Control[] controls) { }
        public void Remove(Control control) { }
        public void Clear() { }
        public int Count => 0;
        public Control this[int index] => null;
        public Control this[string name] => null;
        public bool Contains(Control control) => false;
    }

    public class ScrollableControl : Control { }

    public class ContainerControl : ScrollableControl
    {
        public AutoScaleMode AutoScaleMode { get; set; }
        public System.Drawing.SizeF AutoScaleDimensions { get; set; }
    }

    public class UserControl : ContainerControl
    {
        public UserControl() { }
    }

    public class Form : ContainerControl
    {
        public FormStartPosition StartPosition { get; set; }
        public FormBorderStyle FormBorderStyle { get; set; }
        public FormWindowState WindowState { get; set; }
        public bool MaximizeBox { get; set; }
        public bool MinimizeBox { get; set; }
        public bool ShowInTaskbar { get; set; }
        public bool TopMost { get; set; }
        public DialogResult DialogResult { get; set; }
        public object MainMenuStrip { get; set; }
        public object Icon { get; set; }
        public bool IsMdiContainer { get; set; }
        public virtual void Close() { if (DispatchClosing()) DispatchClosed(); }
        private bool _closed;
        public bool DispatchClosing()
        {
            if (_closed) return true;
            var args = new FormClosingEventArgs { CloseReason = CloseReason.UserClosing };
            FormClosing?.Invoke(this, args);
            FormClosingEx?.Invoke(this, args);
            if (args.Cancel) return false;
            _closed = true;
            return true;
        }
        public void DispatchClosed() => FormClosed?.Invoke(this, EventArgs.Empty);
        public void DispatchFocus(bool focused)
        {
            if (focused) Activated?.Invoke(this, EventArgs.Empty);
            else Deactivate?.Invoke(this, EventArgs.Empty);
        }
        public DialogResult ShowDialog() => DialogResult.OK;
        public DialogResult ShowDialog(IWin32Window owner) => DialogResult.OK;
        public void SetDesktopLocation(int x, int y) { }
        public void Activate() { }
        public event FormClosingEventHandler FormClosing;
        public event FormClosingEventHandler FormClosingEx;
        public event EventHandler FormClosed;
        public event EventHandler Shown { add => CompatibilityEvents.AddHandler(nameof(Form) + "." + nameof(Shown), value); remove => CompatibilityEvents.RemoveHandler(nameof(Form) + "." + nameof(Shown), value); }
        public event EventHandler Activated;
        public event EventHandler Deactivate;
    }

    public interface IWin32Window { IntPtr Handle { get; } }

    public static class MessageBox
    {
        public static DialogResult Show(string text) => Show(text, "THSSS");
        public static DialogResult Show(string text, string caption)
        {
            Console.Error.WriteLine($"[{caption}] {text}");
            return DialogResult.OK;
        }
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons) => Show(text, caption);
        public static DialogResult Show(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(text, caption);
        public static DialogResult Show(IWin32Window owner, string text) => Show(text);
        public static DialogResult Show(IWin32Window owner, string text, string caption) => Show(text, caption);
        public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Show(text, caption);
    }

    public static class Application
    {
        public static string StartupPath => AppDomain.CurrentDomain.BaseDirectory;
        public static string ExecutablePath => System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        public static void Run(Form form) { }
        public static void Run() { }
        public static void Exit() { }
        public static void DoEvents() { }
        public static bool ExitThread() => true;
        public static void EnableVisualStyles() { }
        public static void SetCompatibleTextRenderingDefault(bool defaultValue) { }
    }

    public static class Cursor { }

    // ---- Common controls ----
    public enum HorizontalAlignment
    {
        Left = 0,
        Center = 1,
        Right = 2,
    }

    public enum AutoScaleMode
    {
        None = 0,
        Font = 1,
        Dpi = 2,
        Inherit = 3,
    }

    public class UpDownBase : Control
    {
        public HorizontalAlignment TextAlign { get; set; }
    }

    public class ButtonBase : Control
    {
        public bool UseVisualStyleBackColor { get; set; }
    }

    public class ListControl : Control
    {
        public int ItemHeight { get; set; }
        public bool HorizontalScrollbar { get; set; }
        public System.Collections.IList Items { get; } = new System.Collections.ArrayList();
        public bool FormattingEnabled { get; set; }
        public object SelectedItem { get; set; }
        public int SelectedIndex { get; set; }
        public event EventHandler SelectedIndexChanged { add => CompatibilityEvents.AddHandler(nameof(ListControl) + "." + nameof(SelectedIndexChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(ListControl) + "." + nameof(SelectedIndexChanged), value); }
    }

    public class CommonDialog
    {
        public virtual DialogResult ShowDialog() => DialogResult.Cancel;
    }

    public class Button : ButtonBase { public DialogResult DialogResult { get; set; } }
    public class Label : Control { }
    public class Panel : Control { }
    public class GroupBox : Control { }
    public class PictureBox : Control { public object Image { get; set; } public PictureBoxSizeMode SizeMode { get; set; } }
    public enum PictureBoxSizeMode { Normal = 0, StretchImage = 1, AutoSize = 2, CenterImage = 3, Zoom = 4 }

    public class TextBox : Control
    {
        public bool Multiline { get; set; }
        public bool ReadOnly { get; set; }
        public char PasswordChar { get; set; }
        public int MaxLength { get; set; }
        public bool WordWrap { get; set; }
        public ScrollBars ScrollBars { get; set; }
    }

    public enum ScrollBars { None = 0, Horizontal = 1, Vertical = 2, Both = 3 }

    public class RichTextBox : Control
    {
        public bool ReadOnly { get; set; }
        public string SelectedText { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public void AppendText(string text) { }
        public void Clear() { }
        public void Select(int start, int length) { }
        public void SaveFile(string path) { }
    }

    public class NumericUpDown : UpDownBase
    {
        public decimal Value { get; set; }
        public decimal Minimum { get; set; }
        public decimal Maximum { get; set; }
        public decimal Increment { get; set; }
        public int DecimalPlaces { get; set; }
        public bool ReadOnly { get; set; }
        public event EventHandler ValueChanged { add => CompatibilityEvents.AddHandler(nameof(NumericUpDown) + "." + nameof(ValueChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(NumericUpDown) + "." + nameof(ValueChanged), value); }
    }

    public class CheckBox : ButtonBase
    {
        public bool Checked { get; set; }
        public CheckState CheckState { get; set; }
        public bool AutoCheck { get; set; }
        public event EventHandler CheckedChanged { add => CompatibilityEvents.AddHandler(nameof(CheckBox) + "." + nameof(CheckedChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(CheckBox) + "." + nameof(CheckedChanged), value); }
    }

    public enum CheckState { Unchecked = 0, Checked = 1, Indeterminate = 2 }

    public class RadioButton : CheckBox { }

    public class ComboBox : ListControl
    {
        public object SelectedItem { get; set; }
        public int SelectedIndex { get; set; }
        public ComboBoxStyle DropDownStyle { get; set; }
        public void BeginUpdate() { }
        public void EndUpdate() { }
        public event EventHandler SelectedIndexChanged { add => CompatibilityEvents.AddHandler(nameof(ComboBox) + "." + nameof(SelectedIndexChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(ComboBox) + "." + nameof(SelectedIndexChanged), value); }
    }

    public enum ComboBoxStyle { Simple = 0, DropDown = 1, DropDownList = 2 }

    public class ListBox : ListControl
    {
        public object SelectedItem { get; set; }
        public int SelectedIndex { get; set; }
        public void BeginUpdate() { }
        public void EndUpdate() { }
        public void ClearSelected() { }
        public event EventHandler SelectedIndexChanged { add => CompatibilityEvents.AddHandler(nameof(ListBox) + "." + nameof(SelectedIndexChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(ListBox) + "." + nameof(SelectedIndexChanged), value); }
    }

    public class ListView : Control
    {
        public int SelectedIndices { get; set; }
        public View View { get; set; }
    }

    public enum View { LargeIcon = 0, Details = 1, SmallIcon = 2, List = 3, Tile = 4 }

    public class TreeView : Control
    {
        public TreeNodeCollection Nodes { get; set; }
        public TreeNode SelectedNode { get; set; }
        public void ExpandAll() { }
        public void CollapseAll() { }
        public event EventHandler AfterSelect { add => CompatibilityEvents.AddHandler(nameof(TreeView) + "." + nameof(AfterSelect), value); remove => CompatibilityEvents.RemoveHandler(nameof(TreeView) + "." + nameof(AfterSelect), value); }
    }

    public class TreeNodeCollection
    {
        public void Add(TreeNode node) { }
        public void Add(string text) { }
        public void Clear() { }
        public int Count => 0;
        public TreeNode this[int index] => null;
    }

    public class TreeNode
    {
        public string Name { get; set; }
        public System.Drawing.Color ForeColor { get; set; }
        public string Text { get; set; }
        public object Tag { get; set; }
        public TreeNodeCollection Nodes { get; set; }
        public bool IsExpanded { get; set; }
        public TreeNode() { Nodes = new TreeNodeCollection(); }
        public TreeNode(string text) : this() { Text = text; }
    }

    public class ProgressBar : Control { public int Value { get; set; } public int Minimum { get; set; } public int Maximum { get; set; } }
    public class TrackBar : Control { public int Value { get; set; } public int Minimum { get; set; } public int Maximum { get; set; } public event EventHandler ValueChanged { add => CompatibilityEvents.AddHandler(nameof(TrackBar) + "." + nameof(ValueChanged), value); remove => CompatibilityEvents.RemoveHandler(nameof(TrackBar) + "." + nameof(ValueChanged), value); } }
    public class ScrollBar : Control { public int Value { get; set; } public int Minimum { get; set; } public int Maximum { get; set; } public event EventHandler Scroll { add => CompatibilityEvents.AddHandler(nameof(ScrollBar) + "." + nameof(Scroll), value); remove => CompatibilityEvents.RemoveHandler(nameof(ScrollBar) + "." + nameof(Scroll), value); } }
    public class HScrollBar : ScrollBar { }
    public class VScrollBar : ScrollBar { }

    public class TabControl : Control { public int SelectedIndex { get; set; } }
    public class TabPage : Control { }
    public class SplitContainer : Control { }
    public class TableLayoutPanel : Control { }
    public class FlowLayoutPanel : Control { }
    public class StatusStrip : Control { }
    public class MenuStrip : Control { }
    public class ContextMenuStrip : Control { }
    public class ToolStrip : Control { }
    public class ToolStripButton : Control { }
    public class ToolStripMenuItem : Control { }
    public class ToolStripStatusLabel : Control { }

    public class Timer : IDisposable
    {
        private EventHandlerList _compatibilityEvents;
        private EventHandlerList CompatibilityEvents => _compatibilityEvents ??= new EventHandlerList();
        public int Interval { get; set; }
        public bool Enabled { get; set; }
        public event EventHandler Tick { add => CompatibilityEvents.AddHandler(nameof(Timer) + "." + nameof(Tick), value); remove => CompatibilityEvents.RemoveHandler(nameof(Timer) + "." + nameof(Tick), value); }
        public void Start() { Enabled = true; }
        public void Stop() { Enabled = false; }
        public void Dispose() { }
    }

    // ---- Dialogs ----
    public class FileDialog
    {
        public string FileName { get; set; }
        public string Filter { get; set; }
        public string Title { get; set; }
        public DialogResult ShowDialog() => DialogResult.Cancel;
        public DialogResult ShowDialog(IWin32Window owner) => DialogResult.Cancel;
    }

    public class OpenFileDialog : FileDialog { public bool CheckFileExists { get; set; } public bool Multiselect { get; set; } }
    public class SaveFileDialog : FileDialog { public bool OverwritePrompt { get; set; } }

    public class FolderBrowserDialog : CommonDialog
    {
        public string SelectedPath { get; set; }
        public string Description { get; set; }
        public DialogResult ShowDialog() => DialogResult.Cancel;
    }

    public class ColorDialog
    {
        public System.Drawing.Color Color { get; set; }
        public DialogResult ShowDialog() => DialogResult.Cancel;
    }

    public class FontDialog
    {
        public System.Drawing.Font Font { get; set; }
        public DialogResult ShowDialog() => DialogResult.Cancel;
    }
}
