using System.Drawing.Drawing2D;

namespace Timer;

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

        using var trackPath = RoundRect.CreatePath(ClientRectangle, Height / 2);
        using var trackBrush = new SolidBrush(BackColor);
        e.Graphics.FillPath(trackBrush, trackPath);

        int progressWidth = (int)Math.Round(Width * currentValue);
        if (progressWidth <= 0) return;

        var progressBounds = new Rectangle(0, 0, progressWidth, Height);
        using var progressPath = RoundRect.CreatePath(progressBounds, Height / 2);
        using var progressBrush = new SolidBrush(ProgressColor);
        e.Graphics.FillPath(progressBrush, progressPath);
    }
}
