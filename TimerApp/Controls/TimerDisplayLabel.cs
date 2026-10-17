using System.Drawing.Drawing2D;

namespace Timer;

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
        using var path = RoundRect.CreatePath(ClientRectangle, CornerRadius);
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
}
