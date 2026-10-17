using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Media;
using System.Windows.Forms;

namespace TimerApp
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public class MainForm : Form
    {
        private NumericUpDown minutesInput;
        private Button startButton;
        private Button resetButton;
        private Button pauseButton;
        private Button resumeButton;
        private Label statusLabel;
        private Label remainLabel;
        private TextBox wavPathTextBox;
        private Button browseWavButton;
        private CheckBox loopWavCheckBox;

        private readonly Timer uiTimer;
        private readonly NotifyIcon notifyIcon;

        private DateTime endTime;
        private TimeSpan remaining;
        private bool isRunning = false;
        private bool isPaused = false;
        private bool warnedFiveMinutes = false;

        private readonly List<AlertForm> alertForms = new List<AlertForm>();
        private SoundPlayer soundPlayer;

        public MainForm()
        {
            Text = "Timer";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            ClientSize = new Size(460, 260);

            var minutesLabel = new Label
            {
                Text = "時間（分）",
                Location = new Point(20, 20),
                AutoSize = true
            };
            Controls.Add(minutesLabel);

            minutesInput = new NumericUpDown
            {
                Location = new Point(100, 16),
                Width = 90,
                Minimum = 1,
                Maximum = 1440,
                Value = 60
            };
            Controls.Add(minutesInput);

            startButton = new Button
            {
                Text = "開始",
                Location = new Point(20, 60),
                Size = new Size(90, 32)
            };
            startButton.Click += (_, __) => StartTimer();
            Controls.Add(startButton);

            resetButton = new Button
            {
                Text = "リセット",
                Location = new Point(120, 60),
                Size = new Size(90, 32)
            };
            resetButton.Click += (_, __) => ResetTimer();
            Controls.Add(resetButton);

            pauseButton = new Button
            {
                Text = "一時停止",
                Location = new Point(220, 60),
                Size = new Size(90, 32),
                Enabled = false
            };
            pauseButton.Click += (_, __) => PauseTimer();
            Controls.Add(pauseButton);

            resumeButton = new Button
            {
                Text = "再開",
                Location = new Point(320, 60),
                Size = new Size(90, 32),
                Enabled = false
            };
            resumeButton.Click += (_, __) => ResumeTimer();
            Controls.Add(resumeButton);

            statusLabel = new Label
            {
                Text = "待機中",
                Location = new Point(20, 110),
                AutoSize = true
            };
            Controls.Add(statusLabel);

            remainLabel = new Label
            {
                Text = "残り 60:00",
                Location = new Point(20, 140),
                AutoSize = true,
                Font = new Font("Segoe UI", 18, FontStyle.Bold)
            };
            Controls.Add(remainLabel);

            var wavLabel = new Label
            {
                Text = "WAV（任意）",
                Location = new Point(20, 190),
                AutoSize = true
            };
            Controls.Add(wavLabel);

            wavPathTextBox = new TextBox
            {
                Location = new Point(100, 186),
                Size = new Size(250, 23)
            };
            Controls.Add(wavPathTextBox);

            browseWavButton = new Button
            {
                Text = "参照",
                Location = new Point(360, 184),
                Size = new Size(70, 27)
            };
            browseWavButton.Click += (_, __) => BrowseWav();
            Controls.Add(browseWavButton);

            loopWavCheckBox = new CheckBox
            {
                Text = "時間切れでWAVをループ再生",
                Location = new Point(100, 220),
                AutoSize = true
            };
            Controls.Add(loopWavCheckBox);

            uiTimer = new Timer { Interval = 1000 };
            uiTimer.Tick += (_, __) => OnTick();

            notifyIcon = new NotifyIcon
            {
                Visible = true,
                Icon = File.Exists("timer.ico") ? new Icon("timer.ico") : SystemIcons.Information,
                Text = "Timer"
            };

            FormClosing += (_, __) =>
            {
                StopSound();
                notifyIcon.Visible = false;
                notifyIcon.Dispose();
            };
        }

        private void StartTimer()
        {
            if (isRunning && !isPaused) return;

            int minutes = (int)minutesInput.Value;
            remaining = TimeSpan.FromMinutes(minutes);
            endTime = DateTime.Now.Add(remaining);

            isRunning = true;
            isPaused = false;
            warnedFiveMinutes = false;

            uiTimer.Start();
            UpdateUiState();
            UpdateRemainingLabel();

            statusLabel.Text = "実行中";
        }

        private void ResetTimer()
        {
            uiTimer.Stop();
            isRunning = false;
            isPaused = false;
            warnedFiveMinutes = false;
            remaining = TimeSpan.FromMinutes((double)minutesInput.Value);

            CloseAlerts();
            StopSound();

            statusLabel.Text = "待機中";
            UpdateRemainingLabel();
            UpdateUiState();
        }

        private void PauseTimer()
        {
            if (!isRunning || isPaused) return;

            remaining = endTime - DateTime.Now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            uiTimer.Stop();
            isPaused = true;
            statusLabel.Text = "一時停止中";
            UpdateUiState();
        }

        private void ResumeTimer()
        {
            if (!isRunning || !isPaused) return;

            endTime = DateTime.Now.Add(remaining);
            uiTimer.Start();
            isPaused = false;
            statusLabel.Text = "実行中";
            UpdateUiState();
        }

        private void OnTick()
        {
            if (!isRunning || isPaused) return;

            remaining = endTime - DateTime.Now;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

            UpdateRemainingLabel();

            if (!warnedFiveMinutes && remaining <= TimeSpan.FromMinutes(5) && remaining > TimeSpan.Zero)
            {
                warnedFiveMinutes = true;
                ShowFiveMinuteToast();
            }

            if (remaining <= TimeSpan.Zero)
            {
                uiTimer.Stop();
                isRunning = false;
                isPaused = false;
                statusLabel.Text = "時間です";
                UpdateUiState();
                ShowFinalAlert();
            }
        }

        private void UpdateRemainingLabel()
        {
            remainLabel.Text = $"残り {remaining:mm\\:ss}";
        }

        private void UpdateUiState()
        {
            startButton.Enabled = !isRunning || isPaused;
            resetButton.Enabled = true;
            pauseButton.Enabled = isRunning && !isPaused;
            resumeButton.Enabled = isRunning && isPaused;
            minutesInput.Enabled = !isRunning;
        }

        private void BrowseWav()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*";
                dialog.Title = "WAVファイルを選択";
                if (dialog.ShowDialog() == DialogResult.OK)
                {
                    wavPathTextBox.Text = dialog.FileName;
                }
            }
        }

        private void ShowFiveMinuteToast()
        {
            notifyIcon.BalloonTipTitle = "タイマー通知";
            notifyIcon.BalloonTipText = "あと5分です";
            notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            notifyIcon.ShowBalloonTip(3000);
        }

        private void ShowFinalAlert()
        {
            CloseAlerts();

            if (loopWavCheckBox.Checked)
            {
                StartSoundLoop();
            }

            foreach (var screen in Screen.AllScreens)
            {
                var alert = new AlertForm(screen.Bounds, DismissAlertsAndStopSound);
                alertForms.Add(alert);
                alert.Show();
            }
        }

        private void DismissAlertsAndStopSound()
        {
            CloseAlerts();
            StopSound();
        }

        private void CloseAlerts()
        {
            foreach (var form in alertForms)
            {
                try
                {
                    if (!form.IsDisposed) form.Close();
                }
                catch { }
            }
            alertForms.Clear();
        }

        private void StartSoundLoop()
        {
            StopSound();

            string path = wavPathTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return;
            }

            try
            {
                soundPlayer = new SoundPlayer(path);
                soundPlayer.Load();
                soundPlayer.PlayLooping();
            }
            catch
            {
                soundPlayer = null;
            }
        }

        private void StopSound()
        {
            try
            {
                soundPlayer?.Stop();
                soundPlayer?.Dispose();
            }
            catch { }
            finally
            {
                soundPlayer = null;
            }
        }
    }

    public class AlertForm : Form
    {
        private readonly Action onDismiss;

        public AlertForm(Rectangle bounds, Action onDismiss)
        {
            this.onDismiss = onDismiss;

            Bounds = bounds;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(180, 0, 20);

            var titleLabel = new Label
            {
                Text = "時間です",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 42, FontStyle.Bold),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            var messageLabel = new Label
            {
                Text = "次の工程へ進んでください",
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 24, FontStyle.Regular),
                AutoSize = true,
                BackColor = Color.Transparent
            };

            var okButton = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI", 20, FontStyle.Bold),
                Size = new Size(180, 70)
            };
            okButton.Click += (_, __) => this.onDismiss?.Invoke();

            Controls.Add(titleLabel);
            Controls.Add(messageLabel);
            Controls.Add(okButton);

            Load += (_, __) =>
            {
                titleLabel.Left = (ClientSize.Width - titleLabel.Width) / 2;
                titleLabel.Top = ClientSize.Height / 2 - 180;

                messageLabel.Left = (ClientSize.Width - messageLabel.Width) / 2;
                messageLabel.Top = titleLabel.Bottom + 30;

                okButton.Left = (ClientSize.Width - okButton.Width) / 2;
                okButton.Top = messageLabel.Bottom + 40;
            };
        }

        protected override bool ShowWithoutActivation => false;
    }
}