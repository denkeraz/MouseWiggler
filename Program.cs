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

        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint esFlags);

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
        const uint MOUSEEVENTF_MOVE = 0x0001;
        const uint ES_CONTINUOUS = 0x80000000;
        const uint ES_SYSTEM_REQUIRED = 0x00000001;
        const uint ES_DISPLAY_REQUIRED = 0x00000002;

        private enum JigglerState
        {
            Monitoring,
            Active,
            OnBreak
        }

        // --- UI Controls & State ---
        private NumericUpDown numMinutes = null!;
        private CheckBox chkEnableCycles = null!;
        private NumericUpDown numActiveMin = null!;
        private NumericUpDown numActiveMax = null!;
        private NumericUpDown numBreakMin = null!;
        private NumericUpDown numBreakMax = null!;
        private CheckBox chkEnableCutoff = null!;
        private DateTimePicker dtpCutoff = null!;
        private NumericUpDown numCutoffJitter = null!;
        private Button btnToggle = null!;
        private Label lblStatus = null!;
        private CheckBox chkMinimizeToTray = null!;
        private NotifyIcon trayIcon = null!;
        private System.Windows.Forms.Timer tickTimer;

        private readonly Random rng = new Random();
        private bool isEnabled = true;
        private JigglerState state = JigglerState.Monitoring;
        private DateTime nextJiggleTime = DateTime.MinValue;
        private DateTime phaseEndTime = DateTime.MaxValue;
        private DateTime effectiveCutoffTime = DateTime.MaxValue;
        private int burstMovesRemaining;
        private long lastUserActivityTick;
        private uint lastSeenInputTick;
        private uint lastJiggleInputTick;
        private Point lastCursorPos;

        public JigglerForm()
        {
            BuildUI();
            SetupTrayIcon();
            ResetIdleTimer();
            ScheduleEffectiveCutoff();

            tickTimer = new System.Windows.Forms.Timer { Interval = 500 };
            tickTimer.Tick += OnTimerTick;
            tickTimer.Start();
        }

        private void ResetIdleTimer()
        {
            SetSystemAwake(false);
            state = JigglerState.Monitoring;
            lastUserActivityTick = Environment.TickCount64;
            lastSeenInputTick = GetLastInputTick();
            lastCursorPos = Cursor.Position;
        }

        private void ScheduleEffectiveCutoff()
        {
            DateTime baseCutoff = DateTime.Today.Add(dtpCutoff.Value.TimeOfDay);
            if (baseCutoff <= DateTime.Now)
            {
                baseCutoff = baseCutoff.AddDays(1);
            }

            int jitter = (int)numCutoffJitter.Value;
            int offsetMinutes = jitter > 0 ? rng.Next(-jitter, jitter + 1) : 0;
            DateTime candidate = baseCutoff.AddMinutes(offsetMinutes);

            effectiveCutoffTime = candidate <= DateTime.Now ? baseCutoff : candidate;
        }

        private void BuildUI()
        {
            Text = "Mouse Jiggler";
            ClientSize = new Size(340, 340);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var lblTimeout = new Label
            {
                Text = "Idle timeout (minutes):",
                Location = new Point(20, 22),
                AutoSize = true
            };

            numMinutes = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 120,
                Value = 5,
                Location = new Point(210, 18),
                Width = 110
            };
            numMinutes.ValueChanged += (s, e) => ResetIdleTimer();

            chkEnableCycles = new CheckBox
            {
                Text = "Enable active / break cycles",
                Location = new Point(20, 54),
                AutoSize = true,
                Checked = true
            };

            var lblActive = new Label
            {
                Text = "Active (min):",
                Location = new Point(38, 86),
                AutoSize = true
            };

            numActiveMin = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 480,
                Value = 45,
                Location = new Point(140, 82),
                Width = 65
            };

            var lblActiveTo = new Label
            {
                Text = "to",
                Location = new Point(213, 86),
                AutoSize = true
            };

            numActiveMax = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 480,
                Value = 90,
                Location = new Point(238, 82),
                Width = 65
            };

            var lblBreak = new Label
            {
                Text = "Break (min):",
                Location = new Point(38, 118),
                AutoSize = true
            };

            numBreakMin = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 240,
                Value = 10,
                Location = new Point(140, 114),
                Width = 65
            };

            var lblBreakTo = new Label
            {
                Text = "to",
                Location = new Point(213, 118),
                AutoSize = true
            };

            numBreakMax = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 240,
                Value = 20,
                Location = new Point(238, 114),
                Width = 65
            };

            numActiveMin.ValueChanged += (s, e) =>
            {
                if (numActiveMin.Value > numActiveMax.Value) numActiveMax.Value = numActiveMin.Value;
            };
            numActiveMax.ValueChanged += (s, e) =>
            {
                if (numActiveMax.Value < numActiveMin.Value) numActiveMin.Value = numActiveMax.Value;
            };
            numBreakMin.ValueChanged += (s, e) =>
            {
                if (numBreakMin.Value > numBreakMax.Value) numBreakMax.Value = numBreakMin.Value;
            };
            numBreakMax.ValueChanged += (s, e) =>
            {
                if (numBreakMax.Value < numBreakMin.Value) numBreakMin.Value = numBreakMax.Value;
            };

            chkEnableCycles.CheckedChanged += (s, e) =>
            {
                bool enabled = chkEnableCycles.Checked;
                numActiveMin.Enabled = enabled;
                numActiveMax.Enabled = enabled;
                numBreakMin.Enabled = enabled;
                numBreakMax.Enabled = enabled;

                if (enabled && state == JigglerState.Active)
                {
                    int durationSec = rng.Next((int)numActiveMin.Value * 60, (int)numActiveMax.Value * 60 + 1);
                    phaseEndTime = DateTime.Now.AddSeconds(durationSec);
                }
                else if (!enabled && state == JigglerState.OnBreak)
                {
                    StartActiveSession();
                }
            };

            chkEnableCutoff = new CheckBox
            {
                Text = "Stop after cutoff time",
                Location = new Point(20, 152),
                AutoSize = true,
                Checked = true
            };

            var lblCutoff = new Label
            {
                Text = "Cutoff:",
                Location = new Point(38, 184),
                AutoSize = true
            };

            dtpCutoff = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "h:mm tt",
                ShowUpDown = true,
                Value = DateTime.Today.AddHours(16),
                Location = new Point(92, 180),
                Width = 95
            };

            var lblJitter = new Label
            {
                Text = "± min:",
                Location = new Point(198, 184),
                AutoSize = true
            };

            numCutoffJitter = new NumericUpDown
            {
                Minimum = 0,
                Maximum = 60,
                Value = 15,
                Location = new Point(248, 180),
                Width = 55
            };

            chkEnableCutoff.CheckedChanged += (s, e) =>
            {
                dtpCutoff.Enabled = chkEnableCutoff.Checked;
                numCutoffJitter.Enabled = chkEnableCutoff.Checked;
                ScheduleEffectiveCutoff();
            };
            dtpCutoff.ValueChanged += (s, e) => ScheduleEffectiveCutoff();
            numCutoffJitter.ValueChanged += (s, e) => ScheduleEffectiveCutoff();

            lblStatus = new Label
            {
                Text = "Status: Monitoring...",
                Location = new Point(20, 222),
                Size = new Size(300, 24),
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.DarkGreen
            };

            btnToggle = new Button
            {
                Text = "Pause",
                Location = new Point(20, 256),
                Size = new Size(300, 34)
            };
            btnToggle.Click += (s, e) =>
            {
                isEnabled = !isEnabled;
                ResetIdleTimer();
                if (isEnabled)
                {
                    ScheduleEffectiveCutoff();
                }
                btnToggle.Text = isEnabled ? "Pause" : "Resume";
                lblStatus.Text = isEnabled ? "Status: Monitoring..." : "Status: Paused";
                lblStatus.ForeColor = isEnabled ? Color.DarkGreen : Color.DimGray;
            };

            chkMinimizeToTray = new CheckBox
            {
                Text = "Minimize to system tray instead of taskbar",
                Location = new Point(20, 302),
                AutoSize = true,
                Checked = true
            };

            Controls.AddRange(new Control[]
            {
                lblTimeout, numMinutes,
                chkEnableCycles, lblActive, numActiveMin, lblActiveTo, numActiveMax,
                lblBreak, numBreakMin, lblBreakTo, numBreakMax,
                chkEnableCutoff, lblCutoff, dtpCutoff, lblJitter, numCutoffJitter,
                lblStatus, btnToggle, chkMinimizeToTray
            });
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

        private void StartActiveSession()
        {
            SetSystemAwake(false);
            state = JigglerState.Active;
            burstMovesRemaining = rng.Next(2, 7);
            nextJiggleTime = DateTime.Now;

            if (chkEnableCycles.Checked)
            {
                int durationSec = rng.Next((int)numActiveMin.Value * 60, (int)numActiveMax.Value * 60 + 1);
                phaseEndTime = DateTime.Now.AddSeconds(durationSec);
            }

            lblStatus.Text = "Status: Jiggling (move mouse to stop)";
            lblStatus.ForeColor = Color.DarkOrange;
        }

        private void StartBreakSession()
        {
            state = JigglerState.OnBreak;
            SetSystemAwake(true);

            int durationSec = rng.Next((int)numBreakMin.Value * 60, (int)numBreakMax.Value * 60 + 1);
            phaseEndTime = DateTime.Now.AddSeconds(durationSec);
            lastCursorPos = Cursor.Position;
            lastSeenInputTick = GetLastInputTick();

            lblStatus.Text = "Status: On break";
            lblStatus.ForeColor = Color.SteelBlue;
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!isEnabled) return;

            if (chkEnableCutoff.Checked && DateTime.Now >= effectiveCutoffTime)
            {
                isEnabled = false;
                ResetIdleTimer();
                btnToggle.Text = "Resume";
                lblStatus.Text = "Status: Paused (cutoff reached)";
                lblStatus.ForeColor = Color.DimGray;
                return;
            }

            Point currentCursorPos = Cursor.Position;
            uint currentInputTick = GetLastInputTick();

            bool cursorMoved = currentCursorPos != lastCursorPos;
            bool inputChanged = currentInputTick != lastSeenInputTick;

            if (state == JigglerState.Active)
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
            else if (state == JigglerState.OnBreak)
            {
                // User returned during a break: cancel break and return to idle monitoring
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

            if (state == JigglerState.Monitoring)
            {
                uint idleMs = GetIdleTimeMs();
                uint thresholdMs = (uint)numMinutes.Value * 60 * 1000;

                if (idleMs >= thresholdMs)
                {
                    StartActiveSession();
                }
                else
                {
                    uint remainingSec = (thresholdMs - idleMs) / 1000;
                    lblStatus.Text = $"Status: Idle ({remainingSec}s until jiggle)";
                    lblStatus.ForeColor = Color.DarkGreen;
                }
            }
            else if (state == JigglerState.OnBreak)
            {
                if (!chkEnableCycles.Checked || DateTime.Now >= phaseEndTime)
                {
                    StartActiveSession();
                }
            }

            // Perform natural bursts of subtle relative movements with short pauses while active
            if (state == JigglerState.Active)
            {
                if (chkEnableCycles.Checked && DateTime.Now >= phaseEndTime)
                {
                    StartBreakSession();
                    return;
                }

                if (DateTime.Now >= nextJiggleTime)
                {
                    int dx = rng.Next(-12, 13);
                    int dy = rng.Next(-12, 13);
                    if (dx == 0 && dy == 0) dx = 1;
                    MoveMouseRelative(dx, dy);

                    burstMovesRemaining--;
                    if (burstMovesRemaining > 0)
                    {
                        nextJiggleTime = DateTime.Now.AddMilliseconds(rng.Next(800, 2500));
                    }
                    else
                    {
                        burstMovesRemaining = rng.Next(2, 7);
                        nextJiggleTime = DateTime.Now.AddMilliseconds(rng.Next(10000, 40000));
                    }
                }
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

        private static void SetSystemAwake(bool keepAwake)
        {
            SetThreadExecutionState(keepAwake
                ? (ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED)
                : ES_CONTINUOUS);
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
            SetSystemAwake(false);
            trayIcon.Visible = false;
            trayIcon.Dispose();
            base.OnFormClosing(e);
        }
    }
}