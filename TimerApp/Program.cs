using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.Json;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Timer
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

    internal static class AppText
    {
        private static readonly Dictionary<string, string> Ja = new()
        {
            ["AppTitle"] = "Timer",
            ["Start"] = "開始",
            ["Resume"] = "再開",
            ["Pause"] = "一時停止",
            ["Reset"] = "リセット",
            ["TopMost"] = "常に最前面",
            ["TimeUp"] = "時間です",
            ["Ok"] = "OK",
            ["MinutesLeftFormat"] = "あと{0}分"
        };

        private static readonly Dictionary<string, string> En = new()
        {
            ["AppTitle"] = "Timer",
            ["Start"] = "Start",
            ["Resume"] = "Resume",
            ["Pause"] = "Pause",
            ["Reset"] = "Reset",
            ["TopMost"] = "Always on top",
            ["TimeUp"] = "Time's up",
            ["Ok"] = "OK",
            ["MinutesLeftFormat"] = "{0} min left"
        };

        private static Dictionary<string, string> Current =>
            CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ja", StringComparison.OrdinalIgnoreCase)
                ? Ja
                : En;

        public static string Get(string key) => Current.TryGetValue(key, out var value) ? value : key;

        public static string Format(string key, params object[] args) =>
            string.Format(CultureInfo.CurrentCulture, Get(key), args);
    }

    internal static class AppFonts
    {
        private static FontFamily UiFontFamily => SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;

        public static Font Ui(float size, FontStyle style = FontStyle.Regular) =>
            new(UiFontFamily, size, style);

        public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
            new(FontFamily.GenericMonospace, size, style);

        public static Font TimerDisplay(float size, FontStyle style = FontStyle.Regular) =>
            new("Consolas", size, style);
    }

    public class MainForm : Form
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SYSTEM_MENU_TOPMOST = 0x1F10;
        private const uint MF_STRING = 0x00000000;
        private const uint MF_SEPARATOR = 0x00000800;
        private const uint MF_CHECKED = 0x00000008;
        private const uint MF_UNCHECKED = 0x00000000;

        private TimerDisplayLabel remainLabel;
        private TimerProgressBar progressBar;
        private TextBox timeEditBox;
        private Button startPauseResumeButton;
        private Button resetButton;

        private readonly System.Windows.Forms.Timer timer;
        private DateTime endTime;
        private TimeSpan remaining;
        private TimeSpan initialDuration;
        private TimeSpan resetDuration;
        private TimeSpan totalDuration;
        private TaskbarList? taskbar;

        private bool editing = false;
        private bool running = false;
        private bool paused = false;
        private int? previousRemainingSeconds;

        private readonly List<Form> alerts = new();
        private SoundPlayer? player;
        private readonly string settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "rot1024.Timer",
            "settings.json");

        public MainForm()
        {
            Text = AppText.Get("AppTitle");
            ClientSize = new Size(600, 284);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimumSize = this.Size;
            MaximumSize = this.Size;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = AppFonts.Ui(12f);
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            HandleCreated += (_, __) =>
            {
                EnableTitleBarTheme(this.Handle);
                InitializeSystemMenu();
            };
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

            // 残り時間（主役）
            remainLabel = new TimerDisplayLabel
            {
                Text = "01:00:00",
                Location = new Point(24, 24),
                Size = new Size(552, 124),
                TextAlign = ContentAlignment.MiddleCenter,
                Font = AppFonts.TimerDisplay(38f)
            };
            remainLabel.Click += (_, __) => BeginTimeEdit();
            Controls.Add(remainLabel);

            progressBar = new TimerProgressBar
            {
                Location = new Point(24, 152),
                Size = new Size(552, 6)
            };
            Controls.Add(progressBar);

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
            startPauseResumeButton = new RoundedButton
            {
                Text = AppText.Get("Start"),
                Location = new Point(24, 172),
                Size = new Size(264, 64),
                Font = AppFonts.Ui(12f),
                BackColor = Color.FromArgb(36, 116, 255),
                ForeColor = Color.White
            };
            startPauseResumeButton.Click += (_, __) => ToggleStartPauseResume();
            Controls.Add(startPauseResumeButton);

            // リセット
            resetButton = new RoundedButton
            {
                Text = AppText.Get("Reset"),
                Location = new Point(312, 172),
                Size = new Size(264, 64),
                Font = AppFonts.Ui(12f),
                BackColor = Color.FromArgb(238, 155, 0),
                ForeColor = Color.White
            };
            resetButton.Click += (_, __) => ResetTimer();
            Controls.Add(resetButton);

            timer = new System.Windows.Forms.Timer { Interval = 1000 };
            timer.Tick += (_, __) => Tick();

            resetDuration = TimeSpan.FromMinutes(60);
            initialDuration = resetDuration;
            remaining = resetDuration;
            LoadAppState();
            UpdateLabel();

            totalDuration = resetDuration;
            this.Shown += (_, __) =>
            {
                try
                {
                    taskbar = new TaskbarList();
                    if (running)
                        UpdateTaskbarProgress();
                    else
                        ClearTaskbarProgress();
                }
                catch
                {
                    taskbar = null;
                }
            };

            ApplyTheme();
            UpdateUiState();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            SaveAppState();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
            base.OnFormClosed(e);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SYSTEM_MENU_TOPMOST)
            {
                ToggleTopMost();
                return;
            }

            base.WndProc(ref m);
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color)
                ApplyTheme();
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
                resetDuration = parsed;
                initialDuration = parsed;
                totalDuration = parsed;
            }

            editing = false;
            timeEditBox.Visible = false;
            remainLabel.Visible = true;
            UpdateLabel();
        }

        private void UpdateUiState()
        {
            startPauseResumeButton.Text = !running
                ? AppText.Get("Start")
                : paused ? AppText.Get("Resume") : AppText.Get("Pause");
            ApplyButtonTheme();
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
            else if (e.KeyCode == Keys.Up)
            {
                AdjustEditedTime(1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            else if (e.KeyCode == Keys.Down)
            {
                AdjustEditedTime(-1);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void AdjustEditedTime(int direction)
        {
            if (!TryParseTime(timeEditBox.Text, out var parsed))
                return;

            int caret = timeEditBox.SelectionStart;
            int firstColon = timeEditBox.Text.IndexOf(':');
            int secondColon = timeEditBox.Text.IndexOf(':', firstColon + 1);
            TimeSpan step = GetTimeEditStep(caret, firstColon, secondColon);

             if (direction < 0 && step == TimeSpan.FromHours(1) && parsed.TotalHours < 1)
                return;

            TimeSpan updated = parsed.Add(TimeSpan.FromTicks(step.Ticks * direction));

            if (updated < TimeSpan.FromSeconds(1))
                updated = TimeSpan.FromSeconds(1);

            timeEditBox.Text =
                $"{(int)updated.TotalHours:00}:{updated.Minutes:00}:{updated.Seconds:00}";
            timeEditBox.SelectionStart = Math.Min(caret, timeEditBox.Text.Length);
            timeEditBox.SelectionLength = 0;
        }

        private static TimeSpan GetTimeEditStep(int caret, int firstColon, int secondColon)
        {
            if (firstColon < 0 || secondColon < 0 || caret <= firstColon)
                return TimeSpan.FromHours(1);

            if (caret <= secondColon)
                return TimeSpan.FromMinutes(1);

            return TimeSpan.FromSeconds(1);
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
                    remaining = resetDuration;

                initialDuration = remaining;
                totalDuration = initialDuration;
                endTime = DateTime.Now.Add(remaining);
                running = true;
                paused = false;
                previousRemainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);

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
                previousRemainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
                UpdateUiState();
                UpdateTaskbarProgress();
            }
        }

        private void ResetTimer()
        {
            timer.Stop();
            running = false;
            paused = false;
            previousRemainingSeconds = null;

            StopSound();
            CloseAlerts();

            remaining = resetDuration > TimeSpan.Zero ? resetDuration : TimeSpan.FromMinutes(60);
            initialDuration = remaining;
            totalDuration = remaining;
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

            ShowCrossedWarningOverlay();
            previousRemainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);

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
            UpdateProgressBar();
        }

        private void UpdateProgressBar()
        {
            double totalSeconds = Math.Max(1, totalDuration.TotalSeconds);
            double elapsedSeconds = Math.Max(0, totalSeconds - remaining.TotalSeconds);
            progressBar.Value = Math.Min(1, elapsedSeconds / totalSeconds);
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

        private void ShowCrossedWarningOverlay()
        {
            if (!previousRemainingSeconds.HasValue || remaining <= TimeSpan.Zero) return;

            int remainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
            foreach (int minutes in GetWarningThresholdMinutes())
            {
                int thresholdSeconds = minutes * 60;
                if (previousRemainingSeconds.Value > thresholdSeconds && remainingSeconds <= thresholdSeconds)
                {
                    ShowWarningOverlay(AppText.Format("MinutesLeftFormat", minutes));
                    return;
                }
            }
        }

        private IEnumerable<int> GetWarningThresholdMinutes()
        {
            int totalMinutes = (int)Math.Floor(totalDuration.TotalMinutes);
            var thresholds = new List<int>();

            if (totalMinutes >= 10)
            {
                for (int minutes = (totalMinutes / 10) * 10; minutes >= 10; minutes -= 10)
                {
                    if (minutes < totalDuration.TotalMinutes)
                        thresholds.Add(minutes);
                }
            }

            thresholds.Add(5);
            thresholds.Add(1);
            return thresholds.Distinct().OrderByDescending(x => x);
        }

        private void ShowWarningOverlay(string message)
        {
            foreach (var screen in Screen.AllScreens)
            {
                var overlay = new WarningOverlayForm(screen.Bounds, message);
                overlay.Show();
                overlay.TopMost = false;
                overlay.TopMost = true;
                overlay.BringToFront();
                overlay.Activate();
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

        private void LoadAppState()
        {
            try
            {
                if (!File.Exists(settingsPath)) return;

                string json = File.ReadAllText(settingsPath);
                AppState? state = JsonSerializer.Deserialize<AppState>(json);
                if (state == null) return;

                resetDuration = TimeSpan.FromSeconds(Math.Max(1, state.ResetDurationSeconds));
                initialDuration = resetDuration;
                remaining = TimeSpan.FromSeconds(Math.Max(1, state.RemainingSeconds));
                totalDuration = resetDuration > remaining ? resetDuration : remaining;
                TopMost = state.TopMost;
                if (state.WindowX.HasValue && state.WindowY.HasValue)
                    RestoreWindowLocation(new Point(state.WindowX.Value, state.WindowY.Value));

                if (state.Running)
                {
                    running = true;
                    paused = true;
                    timer.Stop();
                    previousRemainingSeconds = (int)Math.Ceiling(remaining.TotalSeconds);
                }
            }
            catch
            {
            }
        }

        private void SaveAppState()
        {
            try
            {
                if (running && !paused)
                {
                    remaining = endTime - DateTime.Now;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
                }

                var state = new AppState
                {
                    ResetDurationSeconds = Math.Max(1, (int)resetDuration.TotalSeconds),
                    RemainingSeconds = Math.Max(0, (int)remaining.TotalSeconds),
                    Running = running,
                    TopMost = TopMost,
                    WindowX = Location.X,
                    WindowY = Location.Y
                };

                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
                File.WriteAllText(settingsPath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
            }
        }

        private void ToggleTopMost()
        {
            TopMost = !TopMost;
            UpdateSystemMenuTopMost();
        }

        private void RestoreWindowLocation(Point location)
        {
            var windowBounds = new Rectangle(location, Size);
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.WorkingArea.IntersectsWith(windowBounds))
                {
                    StartPosition = FormStartPosition.Manual;
                    Location = location;
                    return;
                }
            }
        }

        private void InitializeSystemMenu()
        {
            IntPtr menu = GetSystemMenu(Handle, false);
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, string.Empty);
            AppendMenu(menu, TopMost ? MF_STRING | MF_CHECKED : MF_STRING | MF_UNCHECKED, (UIntPtr)SYSTEM_MENU_TOPMOST, AppText.Get("TopMost"));
        }

        private void UpdateSystemMenuTopMost()
        {
            IntPtr menu = GetSystemMenu(Handle, false);
            CheckMenuItem(menu, SYSTEM_MENU_TOPMOST, TopMost ? MF_CHECKED : MF_UNCHECKED);
        }
        
        private void ApplyTheme()
        {
            bool dark = IsSystemDarkTheme();
            Color accent = GetSystemAccentColor();
            Color back = dark ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);
            Color surface = dark ? Color.FromArgb(43, 43, 43) : Color.FromArgb(251, 251, 251);
            Color border = dark ? Color.FromArgb(72, 72, 72) : Color.FromArgb(224, 224, 224);
            Color fore = dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(32, 32, 32);
            Color subtleFore = dark ? Color.FromArgb(220, 220, 220) : Color.FromArgb(55, 55, 55);

            BackColor = back;
            ForeColor = fore;
            EnableTitleBarTheme(Handle);

            remainLabel.BackColor = back;
            remainLabel.ForeColor = fore;
            remainLabel.FillColor = surface;
            remainLabel.BorderColor = border;
            remainLabel.Invalidate();

            progressBar.BackColor = dark ? Color.FromArgb(50, 50, 50) : Color.FromArgb(228, 228, 228);
            progressBar.ProgressColor = accent;
            progressBar.Invalidate();

            timeEditBox.BackColor = surface;
            timeEditBox.ForeColor = fore;
            timeEditBox.BorderStyle = BorderStyle.FixedSingle;

            ApplyButtonTheme();
            resetButton.ForeColor = subtleFore;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(
            IntPtr hwnd,
            int attr,
            ref int attrValue,
            int attrSize);

        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool AppendMenu(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

        [DllImport("user32.dll")]
        private static extern uint CheckMenuItem(IntPtr hMenu, int uIDCheckItem, uint uCheck);

        private void ApplyButtonTheme()
        {
            bool dark = IsSystemDarkTheme();
            Color accent = GetSystemAccentColor();
            Color accentHover = AdjustColorBrightness(accent, dark ? 0.12f : 0.08f);
            Color accentPressed = AdjustColorBrightness(accent, dark ? -0.16f : -0.18f);
            Color secondary = dark ? Color.FromArgb(59, 59, 59) : Color.FromArgb(255, 255, 255);
            Color secondaryHover = dark ? Color.FromArgb(69, 69, 69) : Color.FromArgb(246, 246, 246);
            Color secondaryPressed = dark ? Color.FromArgb(52, 52, 52) : Color.FromArgb(238, 238, 238);
            Color secondaryText = dark ? Color.FromArgb(245, 245, 245) : Color.FromArgb(32, 32, 32);

            if (startPauseResumeButton is RoundedButton actionButton)
            {
                actionButton.BackColor = accent;
                actionButton.HoverColor = accentHover;
                actionButton.PressedColor = accentPressed;
                actionButton.BorderColor = accent;
                actionButton.ForeColor = Color.White;
            }

            if (resetButton is RoundedButton secondaryButton)
            {
                secondaryButton.BackColor = secondary;
                secondaryButton.HoverColor = secondaryHover;
                secondaryButton.PressedColor = secondaryPressed;
                secondaryButton.BorderColor = dark ? Color.FromArgb(76, 76, 76) : Color.FromArgb(215, 215, 215);
                secondaryButton.ForeColor = secondaryText;
            }
        }

        private static bool IsSystemDarkTheme()
        {
            try
            {
                object? value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme",
                    1);
                return value is int intValue && intValue == 0;
            }
            catch
            {
                return true;
            }
        }

        private static Color GetSystemAccentColor()
        {
            Color fallback = Color.FromArgb(0, 95, 184);

            try
            {
                object? value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\DWM",
                    "ColorizationColor",
                    null);

                if (value is int intValue)
                {
                    uint argb = unchecked((uint)intValue);
                    byte red = (byte)((argb >> 16) & 0xFF);
                    byte green = (byte)((argb >> 8) & 0xFF);
                    byte blue = (byte)(argb & 0xFF);
                    return Color.FromArgb(red, green, blue);
                }
            }
            catch
            {
            }

            return fallback;
        }

        private static Color AdjustColorBrightness(Color color, float amount)
        {
            int Adjust(int component) =>
                amount >= 0
                    ? component + (int)Math.Round((255 - component) * amount)
                    : component + (int)Math.Round(component * amount);

            return Color.FromArgb(
                Math.Clamp(Adjust(color.R), 0, 255),
                Math.Clamp(Adjust(color.G), 0, 255),
                Math.Clamp(Adjust(color.B), 0, 255));
        }

        private void EnableTitleBarTheme(IntPtr handle)
        {
            int useDark = IsSystemDarkTheme() ? 1 : 0;
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

    public class AppState
    {
        public int ResetDurationSeconds { get; set; }
        public int RemainingSeconds { get; set; }
        public bool Running { get; set; }
        public bool TopMost { get; set; }
        public int? WindowX { get; set; }
        public int? WindowY { get; set; }
    }

    public class AlertForm : Form
    {
        private readonly Action onClose;
        private readonly System.Windows.Forms.Timer fadeTimer;
        private bool closingWithFade;

        public AlertForm(Rectangle bounds, Action onClose)
        {
            this.onClose = onClose;

            Bounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            BackColor = Color.FromArgb(4, 42, 80);
            StartPosition = FormStartPosition.Manual;
            Opacity = 0;

            var label = new Label
            {
                Text = AppText.Get("TimeUp"),
                Location = new Point(0, bounds.Height / 2 - 120),
                Size = new Size(bounds.Width, 120),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(245, 245, 245),
                BackColor = Color.Transparent,
                Font = AppFonts.Ui(38f)
            };

            var button = new RoundedButton
            {
                Text = AppText.Get("Ok"),
                Size = new Size(180, 56),
                Location = new Point((bounds.Width - 180) / 2, bounds.Height / 2 + 36),
                Font = AppFonts.Ui(13f),
                BackColor = Color.FromArgb(60, 150, 255),
                HoverColor = Color.FromArgb(82, 166, 255),
                PressedColor = Color.FromArgb(25, 118, 210),
                BorderColor = Color.FromArgb(120, 190, 255),
                ForeColor = Color.White
            };

            button.Click += (_, __) => CloseWithFade();

            Controls.Add(label);
            Controls.Add(button);

            fadeTimer = new System.Windows.Forms.Timer { Interval = 15 };
            fadeTimer.Tick += (_, __) =>
            {
                if (closingWithFade)
                {
                    Opacity -= 0.08;
                    if (Opacity <= 0)
                    {
                        fadeTimer.Stop();
                        onClose();
                    }
                }
                else
                {
                    Opacity += 0.08;
                    if (Opacity >= 0.96)
                    {
                        Opacity = 0.96;
                        fadeTimer.Stop();
                    }
                }
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            fadeTimer.Start();
        }

        private void CloseWithFade()
        {
            closingWithFade = true;
            fadeTimer.Start();
        }
    }

    public class WarningOverlayForm : Form
    {
        private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 2000 };
        private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 15 };
        private bool fadingOut;

        public WarningOverlayForm(Rectangle bounds, string message)
        {
            Bounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(4, 42, 80);
            StartPosition = FormStartPosition.Manual;
            Opacity = 0;

            var label = new Label
            {
                Text = message,
                Location = new Point(0, bounds.Height / 2 - 64),
                Size = new Size(bounds.Width, 128),
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(245, 245, 245),
                BackColor = Color.Transparent,
                Font = AppFonts.Ui(38f)
            };
            Controls.Add(label);

            fadeTimer.Tick += (_, __) =>
            {
                if (fadingOut)
                {
                    Opacity -= 0.08;
                    if (Opacity <= 0)
                    {
                        fadeTimer.Stop();
                        Close();
                    }
                }
                else
                {
                    Opacity += 0.08;
                    if (Opacity >= 0.92)
                    {
                        Opacity = 0.92;
                        fadeTimer.Stop();
                        displayTimer.Start();
                    }
                }
            };

            displayTimer.Tick += (_, __) =>
            {
                displayTimer.Stop();
                fadingOut = true;
                fadeTimer.Start();
            };
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            fadeTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            displayTimer.Dispose();
            fadeTimer.Dispose();
            base.OnFormClosed(e);
        }
    }

    public class TimerDisplayLabel : Label
    {
        public Color FillColor { get; set; } = Color.FromArgb(43, 43, 43);
        public Color BorderColor { get; set; } = Color.FromArgb(72, 72, 72);
        public int CornerRadius { get; set; } = 16;

        public TimerDisplayLabel()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var path = CreateRoundRectPath(ClientRectangle, CornerRadius);
            using var fill = new SolidBrush(FillColor);
            using var border = new Pen(BorderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath CreateRoundRectPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter - 1;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter - 1;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class TimerProgressBar : Control
    {
        private double currentValue;

        public double Value
        {
            get => currentValue;
            set
            {
                currentValue = Math.Max(0, Math.Min(1, value));
                Invalidate();
            }
        }

        public Color ProgressColor { get; set; } = Color.FromArgb(0, 95, 184);

        public TimerProgressBar()
        {
            DoubleBuffered = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            using var trackPath = CreateRoundRectPath(ClientRectangle, Height / 2);
            using var trackBrush = new SolidBrush(BackColor);
            e.Graphics.FillPath(trackBrush, trackPath);

            int progressWidth = (int)Math.Round(Width * currentValue);
            if (progressWidth <= 0) return;

            var progressBounds = new Rectangle(0, 0, progressWidth, Height);
            using var progressPath = CreateRoundRectPath(progressBounds, Height / 2);
            using var progressBrush = new SolidBrush(ProgressColor);
            e.Graphics.FillPath(progressBrush, progressPath);
        }

        private static GraphicsPath CreateRoundRectPath(Rectangle bounds, int radius)
        {
            int diameter = Math.Max(1, radius * 2);
            var path = new GraphicsPath();
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter - 1;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter - 1;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class RoundedButton : Button
    {
        private bool hovered;
        private bool pressed;

        public Color HoverColor { get; set; } = Color.FromArgb(0, 103, 192);
        public Color PressedColor { get; set; } = Color.FromArgb(0, 77, 153);
        public Color BorderColor { get; set; } = Color.FromArgb(0, 95, 184);
        public int CornerRadius { get; set; } = 8;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            pressed = true;
            Invalidate();
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fillColor = pressed ? PressedColor : hovered ? HoverColor : BackColor;

            using var path = CreateRoundRectPath(ClientRectangle, CornerRadius);
            using var fill = new SolidBrush(fillColor);
            using var border = new Pen(BorderColor);
            e.Graphics.FillPath(fill, path);
            e.Graphics.DrawPath(border, path);

            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        private static GraphicsPath CreateRoundRectPath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter - 1;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter - 1;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
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
