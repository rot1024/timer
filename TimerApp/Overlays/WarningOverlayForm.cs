namespace Timer;

public class WarningOverlayForm : OverlayForm
{
    private readonly System.Windows.Forms.Timer displayTimer = new() { Interval = 2000 };

    public WarningOverlayForm(Rectangle bounds, string message) : base(bounds, 0.92)
    {
        ShowInTaskbar = false;
        AddMessageLabel(message, bounds.Height / 2 - 64, 128);

        displayTimer.Tick += (_, __) =>
        {
            displayTimer.Stop();
            FadeOut();
        };
    }

    protected override void OnFadedIn() => displayTimer.Start();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        displayTimer.Dispose();
        base.OnFormClosed(e);
    }
}
