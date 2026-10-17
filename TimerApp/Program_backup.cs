using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace TimerApp
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        private Label remainLabel;
        private TextBox timeEditBox;
        private Button startPauseResumeButton;
        private Button resetButton;

        private readonly System.Windows.Forms.Timer timer;
        private DateTime endTime;
        private TimeSpan remaining;
        private TimeSpan initialDuration;
        private TimeSpan totalDuration;
        private TaskbarList? taskbar;

        private bool editing = false;
        private bool running = false;
        private bool paused = false;
        private bool warned = false;

        private readonly List<Form> alerts = new();
        private SoundPlayer? player;

        public MainForm()
        {
            Text = "Timer";
            ClientSize = new Size(600, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimumSize = this.Size;
            MaximumSize = this.Size;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Yu Gothic UI", 12);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            HandleCreated += (_, __) => EnableDarkTitleBar(this.Handle);

            // 残り時間（主役）
            remainLabel = new Label
            {
                Text = "01:00:00",
                Location = new Point(20, 20),
                Size = new Size(560, 140),
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Consolas", 36, FontStyle.Bold)
            };
            remainLabel.Click += (_, __) => BeginTimeEdit();
            Controls.Add(remainLabel);

            timeEditBox = new TextBox
            {
                Location = remainLabel.Location,
                Size = remainLabel.Size,
                Font = remainLabel.Font,
                TextAlign = HorizontalAlignment.Center,
                Visible = false
            };
            timeEditBox.KeyDown += TimeEditBox_KeyDown;
            timeEditBox.LostFocus += (_, __) => EndTimeEdit(true);
            Controls.Add(timeEditBox);
            timeEditBox.BringToFront();

            // 開始 / 再開 / 一時停止
            startPauseResumeButton = new Button
            {
                Text = "開始",
                Location = new Point(20, 180),
                Size = new Size(260, 60),
                Font = new Font("Yu Gothic UI", 16, FontStyle.Bold),
                BackColor = Color.FromArgb(36, 116, 255),
                ForeColor = Color.White
            };
            startPauseResumeButton.Click += (_, __) => ToggleStartPauseResume();
            Controls.Add(startPauseResumeButton);

            // リセット
            resetButton = new Button
            {
                Text = "リセット",
                Location = new Point(320, 180),
                Size = new Size(260, 60),
                Font = new Font("Yu Gothic UI", 16, FontStyle.Bold),
                BackColor = Color.FromArgb(238, 155, 0),
                ForeColor = Color.White
            };
            resetButton.Click += (_, __) => ResetTimer();
            Controls.Add(resetButton);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, __) => Tick();

            remaining = TimeSpan.FromMinutes(60);
            UpdateLabel();

            totalDuration = remaining;
            this.Shown += (_, __) =>
            {
                try
                {
                    taskbar = new TaskbarList();
                    ClearTaskbarProgress();
                }
                catch
                {
                    taskbar = null;
                }
            };

            ApplyDarkTheme(this);
        }

        private void BeginTimeEdit()
        {
            if (running || editing) return;

            editing = true;
            timeEditBox.Text = remainLabel.Text;
            remainLabel.Visible = false;
            timeEditBox.Visible = true;
            timeEditBox.Focus();
            timeEditBox.SelectAll();
        }

        private void EndTimeEdit(bool apply)
        {
            if (!editing) return;

            if (apply && TryParseTime(timeEditBox.Text, out var parsed) && parsed > TimeSpan.Zero)
            {
                remaining = parsed;
            }

            editing = false;
            timeEditBox.Visible = false;
            remainLabel.Visible = true;
            UpdateLabel();
        }

        private void UpdateUiState()
        {
            startPauseResumeButton.Text = !running
                ? "開始"
                : paused ? "再開" : "一時停止";
        }

        private void TimeEditBox_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                EndTimeEdit(true);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Escape)
            {
                EndTimeEdit(false);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private bool TryParseTime(string text, out TimeSpan result)
        {
            text = text.Trim();

            if (TimeSpan.TryParse(text, out result))
                return true;

            if (int.TryParse(text, out int minutesOnly) && minutesOnly > 0)
            {
                result = TimeSpan.FromMinutes(minutesOnly);
                return true;
            }

            result = TimeSpan.Zero;
            return false;
        }

        private void ToggleStartPauseResume()
        {
            if (!running)
            {
                if (remaining <= TimeSpan.Zero)
                    remaining = TimeSpan.FromMinutes(60);

                initialDuration = remaining;
                endTime = DateTime.Now.Add(remaining);
                running = true;
                paused = false;
                warned = false;

                UpdateLabel();
                UpdateTaskbarProgress();
                timer.Start();

                UpdateUiState();
            }
            else
            {
                TogglePause();
            }
        }

        private void TogglePause()
        {
            if (!running) return;

            if (!paused)
            {
                remaining = endTime - DateTime.Now;
                timer.Stop();
                paused = true;
                UpdateUiState();
                UpdateTaskbarProgress();
            }
            else
            {
                endTime = DateTime.Now.Add(remaining);
                timer.Start();
                paused = false;
                UpdateUiState();
                UpdateTaskbarProgress();
            }
        }

        private void ResetTimer()
        {
            timer.Stop();
            running = false;
            paused = false;

            StopSound();
            CloseAlerts();

            remaining = initialDuration > TimeSpan.Zero ? initialDuration : TimeSpan.FromMinutes(60);
            UpdateLabel();
            ClearTaskbarProgress();
            UpdateUiState();
        }

        private void Tick()
        {
            if (!running || paused) return;

            remaining = endTime - DateTime.Now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            UpdateLabel();
            UpdateTaskbarProgress();

            if (!warned && remaining <= TimeSpan.FromMinutes(5) && remaining > TimeSpan.Zero)
            {
                warned = true;
                new NotifyIcon
                {
                    Visible = true
                }.ShowBalloonTip(2000, "通知", "あと5分", ToolTipIcon.Info);
            }

            if (remaining == TimeSpan.Zero)
            {
                timer.Stop();
                running = false;

                ShowTaskbarFinished();
                StartSound();
                ShowAlert();

                UpdateUiState();
            }
        }

        private void UpdateLabel()
        {
            remainLabel.Text =
                $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        }

        private void ShowAlert()
        {
            CloseAlerts(); // 念のため既存を消す

            StartSound(); // wavあれば再生

            foreach (var screen in Screen.AllScreens)
            {
                var f = new AlertForm(screen.Bounds, () =>
                {
                    CloseAlerts();
                    StopSound();
                });

                alerts.Add(f);

                f.Show();

                // 最前面を確実に取る
                f.TopMost = false;
                f.TopMost = true;
                f.BringToFront();
                f.Activate();
            }
        }

        private void CloseAlerts()
        {
            foreach (var f in alerts)
            {
                try { f.Close(); } catch { }
            }
            alerts.Clear();
        }

        private void StartSound()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "timer.wav");
            if (!File.Exists(path)) return;

            try
            {
                player = new SoundPlayer(path);
                player.PlayLooping();
            }
            catch { }
        }

        private void StopSound()
        {
            try { player?.Stop(); } catch { }
        }
        
        private void ApplyDarkTheme(Control control)
        {
            Color back = Color.FromArgb(32, 32, 32);
            Color fore = Color.FromArgb(230, 230, 230);
            Color accent = Color.FromArgb(36, 116, 255);

            control.BackColor = back;
            control.ForeColor = fore;

            foreach (Control c in control.Controls)
            {
                if (c is Button btn)
                {
                    btn.BackColor = accent;
                    btn.ForeColor = Color.White;
                    btn.FlatStyle = FlatStyle.Flat;
                    btn.FlatAppearance.BorderSize = 0;
                }
                else if (c is Label)
                {
                    c.ForeColor = fore;
                    c.BackColor = back;
                }
                else if (c is NumericUpDown nud)
                {
                    nud.BackColor = Color.FromArgb(45, 45, 45);
                    nud.ForeColor = fore;
                    nud.BorderStyle = BorderStyle.FixedSingle;
                }
                else
                {
                    c.BackColor = back;
                    c.ForeColor = fore;
                }

                ApplyDarkTheme(c);
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize);

        private void EnableDarkTitleBar(IntPtr handle)
        {
            int useDark = 1;
            DwmSetWindowAttribute(
                handle,
                DWMWA_USE_IMMERSIVE_DARK_MODE,
                ref useDark,
                sizeof(int));
        }

        private void UpdateTaskbarProgress()
        {
            if (taskbar == null || !IsHandleCreated || !Visible)
                return;

            if (!running)
            {
                taskbar.SetState(this.Handle, TBPFLAG.TBPF_NOPROGRESS);
                return;
            }

            taskbar.SetState(this.Handle, paused ? TBPFLAG.TBPF_PAUSED : TBPFLAG.TBPF_NORMAL);

            ulong totalMs = (ulong)Math.Max(1, totalDuration.TotalMilliseconds);
            ulong elapsedMs = (ulong)Math.Max(0, (totalDuration - remaining).TotalMilliseconds);
            if (elapsedMs > totalMs) elapsedMs = totalMs;

            taskbar.SetValue(this.Handle, elapsedMs, totalMs);
        }

        private void ClearTaskbarProgress()
        {
            if (taskbar == null || !IsHandleCreated || !Visible)
                return;

            taskbar.SetState(this.Handle, TBPFLAG.TBPF_NOPROGRESS);
            taskbar.SetValue(this.Handle, 0, 1);
        }

        private void ShowTaskbarFinished()
        {
            if (taskbar == null || !IsHandleCreated)
                return;

            taskbar.SetState(this.Handle, TBPFLAG.TBPF_ERROR);
            taskbar.SetValue(this.Handle, 1, 1);
        }
    }

    public class AlertForm : Form
    {
        private readonly Action onClose;

        public AlertForm(Rectangle bounds, Action onClose)
        {
            this.onClose = onClose;

            Bounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            BackColor = Color.DarkRed;
            StartPosition = FormStartPosition.Manual;

            var label = new Label
            {
                Text = "時間です",
                Dock = DockStyle.Top,
                Height = 200,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.White,
                Font = new Font("Yu Gothic UI", 48, FontStyle.Bold)
            };

            var button = new Button
            {
                Text = "OK",
                Width = 200,
                Height = 80,
                Font = new Font("Yu Gothic UI", 24, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = Color.DarkRed,
                Top = 240,
                Left = (bounds.Width - 200) / 2
            };

            button.Click += (_, __) => onClose();

            Controls.Add(label);
            Controls.Add(button);
        }
    }

    // progress bar on task bar
    [ComImport]
    [Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class CTaskbarList
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("56FDF342-FD6D-11d0-958A-006097C9A090")]
    internal interface ITaskbarList
    {
        void HrInit();
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("602D4995-B13A-429b-A66E-1935E44F4317")]
    internal interface ITaskbarList2 : ITaskbarList
    {
        new void HrInit();
        new void AddTab(IntPtr hwnd);
        new void DeleteTab(IntPtr hwnd);
        new void ActivateTab(IntPtr hwnd);
        new void SetActiveAlt(IntPtr hwnd);

        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("EA1AFB91-9E28-4B86-90E9-9E9F8A5EEA84")]
    internal interface ITaskbarList3 : ITaskbarList2
    {
        new void HrInit();
        new void AddTab(IntPtr hwnd);
        new void DeleteTab(IntPtr hwnd);
        new void ActivateTab(IntPtr hwnd);
        new void SetActiveAlt(IntPtr hwnd);

        new void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fFullscreen);

        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG tbpFlags);

        // 以降は未使用でも vtable を合わせるために必要
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI, uint dwReserved);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr hIcon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    internal enum TBPFLAG
    {
        TBPF_NOPROGRESS = 0,
        TBPF_INDETERMINATE = 0x1,
        TBPF_NORMAL = 0x2,
        TBPF_ERROR = 0x4,
        TBPF_PAUSED = 0x8
    }

    internal sealed class TaskbarList
    {
        private readonly ITaskbarList3 taskbar = (ITaskbarList3)new CTaskbarList();

        public TaskbarList()
        {
            taskbar.HrInit();
        }

        public void SetState(IntPtr hwnd, TBPFLAG state)
        {
            taskbar.SetProgressState(hwnd, state);
        }

        public void SetValue(IntPtr hwnd, ulong current, ulong total)
        {
            taskbar.SetProgressValue(hwnd, current, total);
        }
    }
}
