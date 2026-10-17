namespace Timer;

public class AlertForm : OverlayForm
{
    private readonly Action onClose;

    public AlertForm(Rectangle bounds, Action onClose) : base(bounds, 0.96)
    {
        this.onClose = onClose;

        AddMessageLabel(AppText.Get("TimeUp"), bounds.Height / 2 - 120, 120);

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
        button.Click += (_, __) => FadeOut();
        Controls.Add(button);
    }

    protected override void OnFadedOut() => onClose();
}
