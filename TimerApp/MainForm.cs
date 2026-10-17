using System.Globalization;
using System.Media;
using Microsoft.Win32;

namespace Timer;

public class MainForm : Form
{
    private const int SYSTEM_MENU_TOPMOST = 0x1F10;
    private static readonly TimeSpan DefaultDuration = TimeSpan.FromMinutes(60);

    private readonly TimerDisplayLabel remainLabel;
    private readonly TimerProgressBar progressBar;
    private readonly TextBox timeEditBox;
    private readonly RoundedButton startPauseResumeButton;
    private readonly RoundedButton resetButton;

    private readonly System.Windows.Forms.Timer timer;
    private readonly Countdown countdown = new(DefaultDuration);
    private TaskbarList? taskbar;
    private ThemePalette theme = ThemePalette.FromSystem();

    private bool editing = false;
    private int? previousRemainingSeconds;

    private readonly List<AlertForm> alerts = new();
    private SoundPlayer? player;

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
            EnableTitleBarTheme();
            InitializeSystemMenu();
        };
        SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

        // 残り時間（主役）
        remainLabel = new TimerDisplayLabel
        {
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
            BorderStyle = BorderStyle.FixedSingle,
            Visible = false
        };
        timeEditBox.KeyDown += TimeEditBox_KeyDown;
        timeEditBox.LostFocus += (_, __) => EndTimeEdit(true);
        Controls.Add(timeEditBox);
        timeEditBox.BringToFront();

        // 開始 / 再開 / 一時停止
        startPauseResumeButton = new RoundedButton
        {
            Location = new Point(24, 172),
            Size = new Size(264, 64),
            Font = AppFonts.Ui(12f)
        };
        startPauseResumeButton.Click += (_, __) => ToggleStartPauseResume();
        Controls.Add(startPauseResumeButton);

        // リセット
        resetButton = new RoundedButton
        {
            Text = AppText.Get("Reset"),
            Location = new Point(312, 172),
            Size = new Size(264, 64),
            Font = AppFonts.Ui(12f)
        };
        resetButton.Click += (_, __) => ResetTimer();
        Controls.Add(resetButton);

        // 1 秒ちょうどだと Tick の遅れで表示の秒が飛ぶので、細かく回す
        timer = new System.Windows.Forms.Timer { Interval = 200 };
        timer.Tick += (_, __) => Tick();

        LoadAppState();
        UpdateLabel();

        this.Shown += (_, __) =>
        {
            try
            {
                taskbar = new TaskbarList();
                UpdateTaskbarProgress();
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
        if (m.Msg == NativeMethods.WM_SYSCOMMAND && (m.WParam.ToInt32() & 0xFFF0) == SYSTEM_MENU_TOPMOST)
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

    private bool CanEditTime => countdown.State is TimerState.Idle or TimerState.Finished;

    private void BeginTimeEdit()
    {
        if (!CanEditTime || editing) return;

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
            countdown.SetDuration(parsed);
            UpdateTaskbarProgress();
        }

        editing = false;
        timeEditBox.Visible = false;
        remainLabel.Visible = true;
        UpdateLabel();
    }

    private void UpdateUiState()
    {
        startPauseResumeButton.Text = countdown.State switch
        {
            TimerState.Running => AppText.Get("Pause"),
            TimerState.Paused => AppText.Get("Resume"),
            _ => AppText.Get("Start")
        };
    }

    private void TimeEditBox_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter:
                EndTimeEdit(true);
                break;
            case Keys.Escape:
                EndTimeEdit(false);
                break;
            case Keys.Up:
                AdjustEditedTime(1);
                break;
            case Keys.Down:
                AdjustEditedTime(-1);
                break;
            default:
                return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;
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

        timeEditBox.Text = FormatTime(updated);
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

    private static bool TryParseTime(string text, out TimeSpan result)
    {
        text = text.Trim();

        // TimeSpan.TryParse は "25" を 25 日と解釈するので、数字だけなら先に分として扱う
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out int minutesOnly))
        {
            result = TimeSpan.FromMinutes(minutesOnly);
            return minutesOnly > 0;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out result);
    }

    private static string FormatTime(TimeSpan time) =>
        $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";

    private static int CeilingSeconds(TimeSpan time) => (int)Math.Ceiling(time.TotalSeconds);

    private void ToggleStartPauseResume()
    {
        switch (countdown.State)
        {
            case TimerState.Running:
                countdown.Pause();
                timer.Stop();
                break;
            case TimerState.Paused:
                countdown.Resume();
                previousRemainingSeconds = CeilingSeconds(countdown.Remaining);
                timer.Start();
                break;
            default:
                countdown.Start();
                previousRemainingSeconds = CeilingSeconds(countdown.Remaining);
                timer.Start();
                break;
        }

        UpdateLabel();
        UpdateTaskbarProgress();
        UpdateUiState();
    }

    private void ResetTimer()
    {
        timer.Stop();
        countdown.Reset();
        previousRemainingSeconds = null;

        StopSound();
        CloseAlerts();

        UpdateLabel();
        UpdateTaskbarProgress();
        UpdateUiState();
    }

    private void Tick()
    {
        if (countdown.State != TimerState.Running) return;

        bool finished = countdown.CheckFinished();

        UpdateLabel();
        UpdateTaskbarProgress();

        ShowCrossedWarningOverlay();
        previousRemainingSeconds = CeilingSeconds(countdown.Remaining);

        if (finished)
        {
            timer.Stop();
            ShowAlert();
            UpdateUiState();
        }
    }

    private void UpdateLabel()
    {
        // 切り上げて表示する（00:00:00 になるのは時間切れのときだけ）
        remainLabel.Text = FormatTime(TimeSpan.FromSeconds(CeilingSeconds(countdown.Remaining)));
        progressBar.Value = countdown.Progress;
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
            f.ShowOnTop();
        }
    }

    private void ShowCrossedWarningOverlay()
    {
        TimeSpan remaining = countdown.Remaining;
        if (!previousRemainingSeconds.HasValue || remaining <= TimeSpan.Zero) return;

        int remainingSeconds = CeilingSeconds(remaining);
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

    // 10 分刻み（全体の長さ未満のもの）と、5 分・1 分
    private IEnumerable<int> GetWarningThresholdMinutes()
    {
        double totalMinutes = countdown.TotalDuration.TotalMinutes;
        var thresholds = new List<int>();

        for (int minutes = (int)Math.Floor(totalMinutes) / 10 * 10; minutes >= 10; minutes -= 10)
        {
            if (minutes < totalMinutes)
                thresholds.Add(minutes);
        }

        thresholds.Add(5);
        thresholds.Add(1);
        return thresholds.Distinct().OrderByDescending(x => x);
    }

    private static void ShowWarningOverlay(string message)
    {
        foreach (var screen in Screen.AllScreens)
            new WarningOverlayForm(screen.Bounds, message).ShowOnTop();
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

        StopSound();

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
        AppState? state = AppStateStore.Load();
        if (state == null) return;

        var resetDuration = TimeSpan.FromSeconds(Math.Max(1, state.ResetDurationSeconds));
        // 時間切れのまま閉じた場合は、リセットしたときの時間から始める
        var remaining = state.RemainingSeconds > 0
            ? TimeSpan.FromSeconds(state.RemainingSeconds)
            : resetDuration;
        countdown.Restore(resetDuration, remaining, state.Running);

        TopMost = state.TopMost;
        if (state.WindowX.HasValue && state.WindowY.HasValue)
            RestoreWindowLocation(new Point(state.WindowX.Value, state.WindowY.Value));
    }

    private void SaveAppState()
    {
        AppStateStore.Save(new AppState
        {
            ResetDurationSeconds = Math.Max(1, (int)countdown.ResetDuration.TotalSeconds),
            RemainingSeconds = CeilingSeconds(countdown.Remaining),
            Running = countdown.State is TimerState.Running or TimerState.Paused,
            TopMost = TopMost,
            WindowX = Location.X,
            WindowY = Location.Y
        });
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
        IntPtr menu = NativeMethods.GetSystemMenu(Handle, false);
        NativeMethods.AppendMenu(menu, NativeMethods.MF_SEPARATOR, UIntPtr.Zero, string.Empty);
        NativeMethods.AppendMenu(
            menu,
            NativeMethods.MF_STRING | (TopMost ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED),
            (UIntPtr)SYSTEM_MENU_TOPMOST,
            AppText.Get("TopMost"));
    }

    private void UpdateSystemMenuTopMost()
    {
        IntPtr menu = NativeMethods.GetSystemMenu(Handle, false);
        NativeMethods.CheckMenuItem(menu, SYSTEM_MENU_TOPMOST, TopMost ? NativeMethods.MF_CHECKED : NativeMethods.MF_UNCHECKED);
    }

    private void ApplyTheme()
    {
        theme = ThemePalette.FromSystem();

        BackColor = theme.Back;
        ForeColor = theme.Fore;
        if (IsHandleCreated)
            EnableTitleBarTheme();

        remainLabel.BackColor = theme.Back;
        remainLabel.ForeColor = theme.Fore;
        remainLabel.FillColor = theme.Surface;
        remainLabel.BorderColor = theme.Border;
        remainLabel.Invalidate();

        progressBar.BackColor = theme.Track;
        progressBar.ProgressColor = theme.Accent;
        progressBar.Invalidate();

        timeEditBox.BackColor = theme.Surface;
        timeEditBox.ForeColor = theme.Fore;

        startPauseResumeButton.BackColor = theme.Accent;
        startPauseResumeButton.HoverColor = theme.AccentHover;
        startPauseResumeButton.PressedColor = theme.AccentPressed;
        startPauseResumeButton.BorderColor = theme.Accent;
        startPauseResumeButton.ForeColor = Color.White;

        resetButton.BackColor = theme.Secondary;
        resetButton.HoverColor = theme.SecondaryHover;
        resetButton.PressedColor = theme.SecondaryPressed;
        resetButton.BorderColor = theme.SecondaryBorder;
        resetButton.ForeColor = theme.Fore;
    }

    private void EnableTitleBarTheme()
    {
        int useDark = theme.Dark ? 1 : 0;
        NativeMethods.DwmSetWindowAttribute(
            Handle,
            NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
            ref useDark,
            sizeof(int));
    }

    private void UpdateTaskbarProgress()
    {
        if (taskbar == null || !IsHandleCreated)
            return;

        const ulong total = 10000;
        ulong completed = (ulong)Math.Round(countdown.Progress * total);

        switch (countdown.State)
        {
            case TimerState.Running:
                taskbar.SetState(Handle, TBPFLAG.TBPF_NORMAL);
                taskbar.SetValue(Handle, completed, total);
                break;
            case TimerState.Paused:
                taskbar.SetState(Handle, TBPFLAG.TBPF_PAUSED);
                taskbar.SetValue(Handle, completed, total);
                break;
            case TimerState.Finished:
                taskbar.SetState(Handle, TBPFLAG.TBPF_ERROR);
                taskbar.SetValue(Handle, 1, 1);
                break;
            default:
                // NOPROGRESS のあとに SetValue を呼ぶと NORMAL に戻ってしまうので、状態だけ設定する
                taskbar.SetState(Handle, TBPFLAG.TBPF_NOPROGRESS);
                break;
        }
    }
}
