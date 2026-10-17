namespace Timer;

// 全画面に重ねて、フェードイン・フェードアウトするウィンドウ
public abstract class OverlayForm : Form
{
    private readonly System.Windows.Forms.Timer fadeTimer = new() { Interval = 15 };
    private readonly double maxOpacity;
    private bool fadingOut;

    protected OverlayForm(Rectangle bounds, double maxOpacity)
    {
        this.maxOpacity = maxOpacity;

        Bounds = bounds;
        FormBorderStyle = FormBorderStyle.None;
        TopMost = true;
        BackColor = Color.FromArgb(4, 42, 80);
        StartPosition = FormStartPosition.Manual;
        Opacity = 0;

        fadeTimer.Tick += (_, __) =>
        {
            if (fadingOut)
            {
                Opacity -= 0.08;
                if (Opacity <= 0)
                {
                    fadeTimer.Stop();
                    OnFadedOut();
                }
            }
            else
            {
                Opacity += 0.08;
                if (Opacity >= maxOpacity)
                {
                    Opacity = maxOpacity;
                    fadeTimer.Stop();
                    OnFadedIn();
                }
            }
        };
    }

    // 最前面を確実に取って表示する
    public void ShowOnTop()
    {
        Show();
        TopMost = false;
        TopMost = true;
        BringToFront();
        Activate();
    }

    protected void AddMessageLabel(string text, int top, int height)
    {
        Controls.Add(new Label
        {
            Text = text,
            Location = new Point(0, top),
            Size = new Size(Width, height),
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(245, 245, 245),
            BackColor = Color.Transparent,
            Font = AppFonts.Ui(38f)
        });
    }

    protected void FadeOut()
    {
        fadingOut = true;
        fadeTimer.Start();
    }

    protected virtual void OnFadedIn()
    {
    }

    protected virtual void OnFadedOut() => Close();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        fadeTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        fadeTimer.Dispose();
        base.OnFormClosed(e);
    }
}
