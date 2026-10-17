using System.Drawing.Drawing2D;

namespace Timer;

internal static class RoundRect
{
    public static GraphicsPath CreatePath(Rectangle bounds, int radius)
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
