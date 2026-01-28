using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class Program
{
    // Hotkey Ctrl+Alt+U
    private const int VK_U = 0x55;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    // Hooks
    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    // Keyboard messages
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;

    // Mouse messages (block clicks & wheel, allow move)
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;
    private const int WM_XBUTTONDOWN = 0x020B;
    private const int WM_XBUTTONUP = 0x020C;
    private static volatile bool _locked;
    private static volatile bool _hooksInstalled;
    private static MainForm? _mainForm;
    private static CancellationTokenSource? _pipeCts;
    private static string? _pipeName;
    private static Process? _watchdogProcess;

    private static IntPtr _kbdHook = IntPtr.Zero;
    private static IntPtr _mouseHook = IntPtr.Zero;
    private static LowLevelProc? _kbdProc;
    private static LowLevelProc? _mouseProc;
    private static LowLevelProc? _watchdogKbdProc;
    private static volatile bool _watchdogCtrlDown;
    private static volatile bool _watchdogAltDown;

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length > 0 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
        {
            RunWatchdog(args);
            return;
        }

        _mainForm = new MainForm();
        StartPipeServer();
        InstallHooks();
        StartWatchdogProcess();
        Application.Run(_mainForm);
        StopWatchdogProcess();
        StopPipeServer();
        UninstallHooks();
    }

    private sealed class MainForm : Form
    {
        private readonly RoundedButton _toggle = new() { Width = 240, Height = 44 };
        private readonly Label _hotkey = new() { AutoSize = true };
        private readonly NotifyIcon _tray = new();
        private readonly ContextMenuStrip _trayMenu = new();
        private Icon? _trayIcon;

        public MainForm()
        {
            Text = "AntiCat";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            ShowInTaskbar = true;
            ClientSize = new Size(420, 240);
            DoubleBuffered = true;

            var bgPath = Path.Combine(AppContext.BaseDirectory, "assets", "background.png");
            if (File.Exists(bgPath))
            {
                BackgroundImage = Image.FromFile(bgPath);
                BackgroundImageLayout = ImageLayout.Stretch;
            }

            var iconPath = Path.Combine(AppContext.BaseDirectory, "assets", "tray.ico");
            if (File.Exists(iconPath))
            {
                _trayIcon = new Icon(iconPath);
                Icon = _trayIcon;
            }

            _toggle.Width = 220;
            _toggle.Height = 60;
            _toggle.Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold);
            _toggle.SetTheme(
                baseColor: Color.FromArgb(245, 245, 245),
                hoverColor: Color.FromArgb(255, 255, 255),
                pressedColor: Color.FromArgb(228, 228, 228),
                textColor: Color.FromArgb(20, 20, 20),
                radius: 18);
            _toggle.Location = new Point((ClientSize.Width - _toggle.Width) / 2, (ClientSize.Height - _toggle.Height) / 2);
            _toggle.Anchor = AnchorStyles.None;
            _toggle.Click += (_, __) => Toggle();

            _hotkey.Text = "Ctrl+Alt+U";
            _hotkey.Font = new Font("Segoe UI", 11, FontStyle.Regular);
            _hotkey.ForeColor = Color.FromArgb(24, 24, 24);
            _hotkey.BackColor = Color.Transparent;
            _hotkey.Location = new Point((ClientSize.Width - _hotkey.Width) / 2, _toggle.Top - 28);

            Controls.Add(_toggle);
            Controls.Add(_hotkey);

            _tray.Icon = _trayIcon ?? SystemIcons.Shield;
            _tray.Visible = true;
            _tray.DoubleClick += (_, __) => Toggle();

            _trayMenu.Items.Add("Lock/Unlock (Ctrl+Alt+U)", null, (_, __) => Toggle());
            _trayMenu.Items.Add("Показать окно", null, (_, __) =>
            {
                ShowInTaskbar = true;
                Show();
                WindowState = FormWindowState.Normal;
                Activate();
            });
            _trayMenu.Items.Add("Выход", null, (_, __) => Close());
            _tray.ContextMenuStrip = _trayMenu;

            Resize += (_, __) =>
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    Hide();
                    ShowInTaskbar = false;
                }
                CenterButton();
            };

            UpdateUi();
        }

        private void CenterButton()
        {
            _toggle.Location = new Point((ClientSize.Width - _toggle.Width) / 2, (ClientSize.Height - _toggle.Height) / 2);
            _hotkey.Location = new Point((ClientSize.Width - _hotkey.Width) / 2, _toggle.Top - 28);
        }

        private void Toggle()
        {
            if (!_hooksInstalled)
            {
                MessageBox.Show("Хуки не установлены. Попробуйте запустить приложение от имени администратора.",
                    "AntiCat", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetLocked(!_locked);
            UpdateUi();
        }

        public void SetLocked(bool locked)
        {
            _locked = locked;
            UpdateUi();
        }

        private void UpdateUi()
        {
            _toggle.Text = _locked ? "UNLOCK" : "LOCK";
            _tray.Text = _locked ? "AntiCat: LOCK" : "AntiCat: UNLOCK";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;
                return;
            }

            _locked = false;
            _tray.Visible = false;
            _tray.Dispose();
            _trayIcon?.Dispose();
            base.OnFormClosing(e);
        }
    }

    private static void InstallHooks()
    {
        _kbdProc = KeyboardHook;
        _mouseProc = MouseHook;

        using var p = Process.GetCurrentProcess();
        using var m = p.MainModule;
        IntPtr hMod = m?.ModuleName is null ? IntPtr.Zero : GetModuleHandle(m.ModuleName);

        _kbdHook = SetWindowsHookEx(WH_KEYBOARD_LL, _kbdProc, hMod, 0);
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, hMod, 0);
        _hooksInstalled = _kbdHook != IntPtr.Zero && _mouseHook != IntPtr.Zero;
        if (!_hooksInstalled)
        {
            int err = Marshal.GetLastWin32Error();
            MessageBox.Show($"Не удалось установить хуки (Win32Error={err}).", "AntiCat",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void UninstallHooks()
    {
        if (_kbdHook != IntPtr.Zero) UnhookWindowsHookEx(_kbdHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _kbdHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;
    }

    private static IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _locked)
            return (IntPtr)1;
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static bool IsBlockedMouseMsg(int msg) =>
        msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
        msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
        msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP ||
        msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL ||
        msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP;

    private static IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _locked)
        {
            int msg = wParam.ToInt32();
            if (IsBlockedMouseMsg(msg))
                return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    // P/Invoke
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    private sealed class RoundedButton : Button
    {
        private Color _baseColor = Color.White;
        private Color _hoverColor = Color.White;
        private Color _pressedColor = Color.White;
        private Color _textColor = Color.Black;
        private int _radius = 16;
        private Color _currentColor = Color.White;
        private Color _targetColor = Color.White;
        private readonly System.Windows.Forms.Timer _anim = new() { Interval = 16 };

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                     ControlStyles.SupportsTransparentBackColor, true);
            _anim.Tick += (_, __) => AnimateStep();
        }

        public void SetTheme(Color baseColor, Color hoverColor, Color pressedColor, Color textColor, int radius)
        {
            _baseColor = baseColor;
            _hoverColor = hoverColor;
            _pressedColor = pressedColor;
            _textColor = textColor;
            _radius = radius;
            _currentColor = baseColor;
            _targetColor = baseColor;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _targetColor = _hoverColor;
            _anim.Start();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _targetColor = _baseColor;
            _anim.Start();
        }

        protected override void OnMouseDown(MouseEventArgs mevent)
        {
            base.OnMouseDown(mevent);
            _targetColor = _pressedColor;
            _anim.Start();
        }

        protected override void OnMouseUp(MouseEventArgs mevent)
        {
            base.OnMouseUp(mevent);
            _targetColor = ClientRectangle.Contains(PointToClient(Cursor.Position)) ? _hoverColor : _baseColor;
            _anim.Start();
        }

        private void AnimateStep()
        {
            _currentColor = LerpColor(_currentColor, _targetColor, 0.2f);
            Invalidate();
            if (ColorsClose(_currentColor, _targetColor))
            {
                _currentColor = _targetColor;
                _anim.Stop();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Color.Transparent);
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), _radius);
            using var brush = new SolidBrush(_currentColor);
            e.Graphics.FillPath(brush, path);

            TextRenderer.DrawText(
                e.Graphics, Text, Font, ClientRectangle, _textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void OnPaintBackground(PaintEventArgs pevent)
        {
            // No-op to avoid default border/background drawing.
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            using var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), _radius);
            Region = new Region(path);
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color LerpColor(Color a, Color b, float t)
        {
            int r = a.R + (int)((b.R - a.R) * t);
            int g = a.G + (int)((b.G - a.G) * t);
            int bl = a.B + (int)((b.B - a.B) * t);
            int al = a.A + (int)((b.A - a.A) * t);
            return Color.FromArgb(al, r, g, bl);
        }

        private static bool ColorsClose(Color a, Color b) =>
            Math.Abs(a.R - b.R) < 2 && Math.Abs(a.G - b.G) < 2 &&
            Math.Abs(a.B - b.B) < 2 && Math.Abs(a.A - b.A) < 2;
    }

    private static void StartPipeServer()
    {
        _pipeName = $"AntiCatLock_{Process.GetCurrentProcess().Id}";
        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;
        Task.Run(() => PipeServerLoop(_pipeName, token), token);
    }

    private static void StopPipeServer()
    {
        if (_pipeCts is null) return;
        _pipeCts.Cancel();
        _pipeCts.Dispose();
        _pipeCts = null;
    }

    private static async Task PipeServerLoop(string pipeName, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Message, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token).ConfigureAwait(false);
                using var reader = new StreamReader(server);
                string? line = await reader.ReadLineAsync().ConfigureAwait(false);
                if (line is null) continue;
                if (string.Equals(line, "unlock", StringComparison.OrdinalIgnoreCase))
                    _mainForm?.BeginInvoke(() => _mainForm.SetLocked(false));
                else if (string.Equals(line, "toggle", StringComparison.OrdinalIgnoreCase))
                    _mainForm?.BeginInvoke(() => _mainForm.SetLocked(!_locked));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(250, token).ConfigureAwait(false);
            }
        }
    }

    private static void StartWatchdogProcess()
    {
        if (_pipeName is null) return;
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath)) return;
        var args = $"--watchdog --pipe {_pipeName} --parent {Process.GetCurrentProcess().Id}";
        var psi = new ProcessStartInfo(exePath, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _watchdogProcess = Process.Start(psi);
    }

    private static void StopWatchdogProcess()
    {
        try
        {
            _watchdogProcess?.Kill(true);
        }
        catch
        {
            // best effort
        }
        _watchdogProcess = null;
    }

    private static void RunWatchdog(string[] args)
    {
        string? pipe = null;
        int parentPid = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--pipe", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                pipe = args[i + 1];
            if (string.Equals(args[i], "--parent", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                int.TryParse(args[i + 1], out parentPid);
        }

        if (string.IsNullOrWhiteSpace(pipe))
            return;

        _watchdogKbdProc = (nCode, wParam, lParam) =>
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    int vk = info.vkCode;
                    bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                    if (vk == VK_CONTROL || vk == VK_LCONTROL || vk == VK_RCONTROL)
                        _watchdogCtrlDown = down;
                    if (vk == VK_MENU || vk == VK_LMENU || vk == VK_RMENU)
                        _watchdogAltDown = down;

                    if (down && _watchdogCtrlDown && _watchdogAltDown && vk == VK_U)
                    {
                        TrySendPipeMessage(pipe, "toggle");
                        return (IntPtr)1;
                    }
                }
            }
            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        };

        using var p = Process.GetCurrentProcess();
        using var m = p.MainModule;
        IntPtr hMod = m?.ModuleName is null ? IntPtr.Zero : GetModuleHandle(m.ModuleName);
        IntPtr hook = SetWindowsHookEx(WH_KEYBOARD_LL, _watchdogKbdProc, hMod, 0);

        var form = new Form { ShowInTaskbar = false, WindowState = FormWindowState.Minimized, Opacity = 0 };
        var parentWatch = new System.Windows.Forms.Timer { Interval = 2000 };
        parentWatch.Tick += (_, __) =>
        {
            if (parentPid <= 0) return;
            try
            {
                Process.GetProcessById(parentPid);
            }
            catch
            {
                Application.Exit();
            }
        };
        parentWatch.Start();
        Application.Run(form);
        parentWatch.Stop();
        if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook);
    }

    private static void TrySendPipeMessage(string pipeName, string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(200);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(message);
        }
        catch
        {
            // best effort
        }
    }
}
