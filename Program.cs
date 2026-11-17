using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MouseJigglerUI
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new JigglerForm());
        }
    }

    public class JigglerForm : Form
    {
        // --- Win32 API Declarations ---
        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public MOUSEINPUT mi; }

        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        const uint INPUT_MOUSE = 0;
        const uint MOUSEEVENTF_MOVE = 0x0001        ;

        // --- UI Controls & State ---
        private NumericUpDown numMinutes = null!;
        private Button btnToggle = null!;
        private Label lblStatus = null!;
        private CheckBox chkMinimizeToTray = null!;
        private NotifyIcon trayIcon = null!;
        private System.Windows.Forms.Timer tickTimer;

        private readonly Random rng = new Random();
        private bool isEnabled = true;
        private bool isJiggling = false;
        private DateTime nextJiggleTime = DateTime.MinValue;
        private long lastUserActivityTick;
        private uint lastSeenInputTick;
        private uint lastJiggleInputTick;
        private Point lastCursorPos;

        public JigglerForm()
        {
            BuildUI();
            SetupTrayIcon();
            ResetIdleTimer();

            tickTimer = new System.Windows.Forms.Timer { Interval = 500 };
            tickTimer.Tick += OnTimerTick;
            tickTimer.Start();
        }

        private void ResetIdleTimer()
        {
            isJiggling = false;
            lastUserActivityTick = Environment.TickCount64;
            lastSeenInputTick = GetLastInputTick();
            lastCursorPos = Cursor.Position;
        }

        private void BuildUI()
        {
            Text = "Mouse Jiggler";
            Size = new Size(340, 220);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lblTimeout = new Label
            {
                Text = "Idle timeout (minutes):",
                Location = new Point(20, 24),
                AutoSize = true
            };

            numMinutes = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 120,
                Value = 5,
                Location = new Point(180, 20),
                Width = 110
            };
            numMinutes.ValueChanged += (s, e) => ResetIdleTimer();

            lblStatus = new Label
            {
                Text = "Status: Monitoring...",
                Location = new Point(20, 62),
                Size = new Size(280, 25),
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.DarkGreen
            };

            btnToggle = new Button
            {
                Text = "Pause",
                Location = new Point(20, 98),
                Size = new Size(270, 34)
            };
            btnToggle.Click += (s, e) =>
            {
                isEnabled = !isEnabled;
                ResetIdleTimer();
                btnToggle.Text = isEnabled ? "Pause" : "Resume";
                lblStatus.Text = isEnabled ? "Status: Monitoring..." : "Status: Paused";
                lblStatus.ForeColor = isEnabled ? Color.DarkGreen : Color.DimGray;
            };

            chkMinimizeToTray = new CheckBox
            {
                Text = "Minimize to system tray instead of taskbar",
                Location = new Point(20, 145),
                AutoSize = true,
                Checked = true
            };

            Controls.AddRange(new Control[] { lblTimeout, numMinutes, lblStatus, btnToggle, chkMinimizeToTray });
        }

        private void SetupTrayIcon()
        {
            var trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Open", null, (s, e) => RestoreFromTray());
            trayMenu.Items.Add("Exit", null, (s, e) =>
            {
                trayIcon.Visible = false;
                Application.Exit();
            });

            trayIcon = new NotifyIcon
            {
                Text = "Mouse Jiggler",
                Icon = SystemIcons.Application,
                ContextMenuStrip = trayMenu,
                Visible = false
            };
            trayIcon.DoubleClick += (s, e) => RestoreFromTray();

            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized && chkMinimizeToTray.Checked)
                {
                    Hide();
                    trayIcon.Visible = true;
                }
            };
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            trayIcon.Visible = false;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!isEnabled) return;

            Point currentCursorPos = Cursor.Position;
            uint currentInputTick = GetLastInputTick();

            bool cursorMoved = currentCursorPos != lastCursorPos;
            bool inputChanged = currentInputTick != lastSeenInputTick;

            if (isJiggling)
            {
                // Ignore the GetLastInputInfo timestamp generated by our own SendInput call
                if (inputChanged && !cursorMoved && unchecked(currentInputTick - lastJiggleInputTick) < 200)
                {
                    lastSeenInputTick = currentInputTick;
                    inputChanged = false;
                }

                // If user physically moved the mouse or pressed a key, stop jiggling and re-engage the idle timer
                if (cursorMoved || inputChanged)
                {
                    ResetIdleTimer();
                }
            }
            else if (cursorMoved || inputChanged)
            {
                lastUserActivityTick = Environment.TickCount64;
                lastSeenInputTick = currentInputTick;
                lastCursorPos = currentCursorPos;
            }

            uint idleMs = GetIdleTimeMs();
            uint thresholdMs = (uint)numMinutes.Value * 60 * 1000;

            if (!isJiggling)
            {
                if (idleMs >= thresholdMs)
                {
                    isJiggling = true;
                    nextJiggleTime = DateTime.Now;
                    lblStatus.Text = "Status: Jiggling (move mouse to stop)";
                    lblStatus.ForeColor = Color.DarkOrange;
                }
                else
                {
                    uint remainingSec = (thresholdMs - idleMs) / 1000;
                    lblStatus.Text = $"Status: Idle ({remainingSec}s until jiggle)";
                    lblStatus.ForeColor = Color.DarkGreen;
                }
            }

            // Perform a subtle random move at random 1.5s - 3.5s intervals while jiggling
            if (isJiggling && DateTime.Now >= nextJiggleTime)
            {
                int dx = rng.Next(-12, 13);
                int dy = rng.Next(-12, 13);
                if (dx == 0 && dy == 0) dx = 1;
                MoveMouseRelative(dx, dy);
                nextJiggleTime = DateTime.Now.AddMilliseconds(rng.Next(1500, 3500));
            }
        }

        private uint GetIdleTimeMs()
        {
            long elapsedMs = Environment.TickCount64 - lastUserActivityTick;
            return elapsedMs > 0 ? (uint)Math.Min(elapsedMs, uint.MaxValue) : 0u;
        }

        private static uint GetLastInputTick()
        {
            LASTINPUTINFO lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            return GetLastInputInfo(ref lii) ? lii.dwTime : unchecked((uint)Environment.TickCount);
        }

        private void MoveMouseRelative(int dx, int dy)
        {
            lastJiggleInputTick = unchecked((uint)Environment.TickCount);
            INPUT[] inputs = new INPUT[1];
            inputs[0].type = INPUT_MOUSE;
            inputs[0].mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = MOUSEEVENTF_MOVE };
            SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT)));
            lastCursorPos = Cursor.Position;
            lastSeenInputTick = GetLastInputTick();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.OnFormClosing(e);
        }
    }
}