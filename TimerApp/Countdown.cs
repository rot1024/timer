namespace Timer;

internal enum TimerState
{
    Idle,
    Running,
    Paused,
    Finished
}

// 残り時間と状態だけを持つ（UI には依存しない）
internal sealed class Countdown
{
    private TimeSpan remaining;
    private DateTime endTime;

    public Countdown(TimeSpan resetDuration)
    {
        SetDuration(resetDuration);
    }

    public TimerState State { get; private set; } = TimerState.Idle;

    // リセットしたときに戻る時間
    public TimeSpan ResetDuration { get; private set; }

    // 今回の計測の全体の長さ（進み具合の分母）
    public TimeSpan TotalDuration { get; private set; }

    public TimeSpan Remaining
    {
        get
        {
            if (State != TimerState.Running) return remaining;

            TimeSpan left = endTime - DateTime.Now;
            return left > TimeSpan.Zero ? left : TimeSpan.Zero;
        }
    }

    // 0 から 1 までの進み具合
    public double Progress
    {
        get
        {
            double totalSeconds = Math.Max(1, TotalDuration.TotalSeconds);
            double elapsedSeconds = Math.Max(0, totalSeconds - Remaining.TotalSeconds);
            return Math.Min(1, elapsedSeconds / totalSeconds);
        }
    }

    public void SetDuration(TimeSpan duration)
    {
        remaining = duration;
        ResetDuration = duration;
        TotalDuration = duration;
        State = TimerState.Idle;
    }

    // 保存しておいた状態から戻す。動いていた場合は一時停止の状態にする
    public void Restore(TimeSpan resetDuration, TimeSpan remaining, bool wasRunning)
    {
        ResetDuration = resetDuration;
        this.remaining = remaining;
        TotalDuration = resetDuration > remaining ? resetDuration : remaining;
        State = wasRunning ? TimerState.Paused : TimerState.Idle;
    }

    public void Start()
    {
        if (remaining <= TimeSpan.Zero)
            remaining = ResetDuration;

        TotalDuration = remaining;
        endTime = DateTime.Now.Add(remaining);
        State = TimerState.Running;
    }

    public void Pause()
    {
        if (State != TimerState.Running) return;

        remaining = Remaining;
        State = TimerState.Paused;
    }

    public void Resume()
    {
        if (State != TimerState.Paused) return;

        endTime = DateTime.Now.Add(remaining);
        State = TimerState.Running;
    }

    public void Reset()
    {
        remaining = ResetDuration;
        TotalDuration = ResetDuration;
        State = TimerState.Idle;
    }

    // 時間切れになったら Finished にして true を返す
    public bool CheckFinished()
    {
        if (State != TimerState.Running || Remaining > TimeSpan.Zero) return false;

        remaining = TimeSpan.Zero;
        State = TimerState.Finished;
        return true;
    }
}
