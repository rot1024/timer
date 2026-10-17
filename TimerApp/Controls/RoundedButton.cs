using System.Drawing.Drawing2D;

namespace Timer;

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

        using var path = RoundRect.CreatePath(ClientRectangle, CornerRadius);
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
}
