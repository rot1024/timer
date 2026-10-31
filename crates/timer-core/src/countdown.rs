use std::time::{Duration, SystemTime};

use serde::Serialize;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize)]
#[serde(rename_all = "lowercase")]
pub enum TimerState {
    Idle,
    Running,
    Paused,
    Finished,
}

/// 残り時間と状態だけを持つ。時刻は呼び出す側から渡す（テストで時計を進めるため）。
///
/// 終わる時刻は `SystemTime` で持つ。`Instant` は macOS ではスリープ中に進まないので、
/// スリープから戻ったときに残り時間がずれてしまう。
#[derive(Debug, Clone)]
pub struct Countdown {
    state: TimerState,
    remaining: Duration,
    end_time: SystemTime,
    reset_duration: Duration,
    total_duration: Duration,
}

impl Countdown {
    pub fn new(reset_duration: Duration) -> Self {
        let mut countdown = Self {
            state: TimerState::Idle,
            remaining: Duration::ZERO,
            end_time: SystemTime::UNIX_EPOCH,
            reset_duration: Duration::ZERO,
            total_duration: Duration::ZERO,
        };
        countdown.set_duration(reset_duration);
        countdown
    }

    pub fn state(&self) -> TimerState {
        self.state
    }

    /// リセットしたときに戻る時間
    pub fn reset_duration(&self) -> Duration {
        self.reset_duration
    }

    /// 今回の計測の全体の長さ（進み具合の分母）
    pub fn total_duration(&self) -> Duration {
        self.total_duration
    }

    pub fn remaining(&self, now: SystemTime) -> Duration {
        if self.state != TimerState::Running {
            return self.remaining;
        }
        self.end_time.duration_since(now).unwrap_or(Duration::ZERO)
    }

    /// 0 から 1 までの進み具合
    pub fn progress(&self, now: SystemTime) -> f64 {
        let total = self.total_duration.as_secs_f64().max(1.0);
        let elapsed = (total - self.remaining(now).as_secs_f64()).max(0.0);
        (elapsed / total).min(1.0)
    }

    pub fn can_edit(&self) -> bool {
        matches!(self.state, TimerState::Idle | TimerState::Finished)
    }

    pub fn set_duration(&mut self, duration: Duration) {
        self.remaining = duration;
        self.reset_duration = duration;
        self.total_duration = duration;
        self.state = TimerState::Idle;
    }

    /// 保存しておいた状態から戻す。動いていた場合は一時停止の状態にする
    pub fn restore(&mut self, reset_duration: Duration, remaining: Duration, was_running: bool) {
        self.reset_duration = reset_duration;
        self.remaining = remaining;
        self.total_duration = reset_duration.max(remaining);
        self.state = if was_running {
            TimerState::Paused
        } else {
            TimerState::Idle
        };
    }

    pub fn start(&mut self, now: SystemTime) {
        if self.remaining.is_zero() {
            self.remaining = self.reset_duration;
        }
        self.total_duration = self.remaining;
        self.end_time = now + self.remaining;
        self.state = TimerState::Running;
    }

    pub fn pause(&mut self, now: SystemTime) {
        if self.state != TimerState::Running {
            return;
        }
        self.remaining = self.remaining(now);
        self.state = TimerState::Paused;
    }

    pub fn resume(&mut self, now: SystemTime) {
        if self.state != TimerState::Paused {
            return;
        }
        self.end_time = now + self.remaining;
        self.state = TimerState::Running;
    }

    pub fn reset(&mut self) {
        self.remaining = self.reset_duration;
        self.total_duration = self.reset_duration;
        self.state = TimerState::Idle;
    }

    /// 時間切れになったら Finished にして true を返す
    pub fn check_finished(&mut self, now: SystemTime) -> bool {
        if self.state != TimerState::Running || !self.remaining(now).is_zero() {
            return false;
        }
        self.remaining = Duration::ZERO;
        self.state = TimerState::Finished;
        true
    }
}

/// 秒の切り上げ。表示が 00:00:00 になるのは時間切れのときだけにするため。
pub fn ceil_secs(duration: Duration) -> u64 {
    duration.as_secs() + u64::from(duration.subsec_nanos() > 0)
}

/// 「あと N 分」を出す分。10 分刻み（全体の長さ未満のもの）と、5 分・1 分を、大きい順に返す。
pub fn warning_minutes(total: Duration) -> Vec<u64> {
    let total_minutes = total.as_secs_f64() / 60.0;
    let mut minutes: Vec<u64> = (1..=(total_minutes as u64) / 10)
        .rev()
        .map(|tens| tens * 10)
        .filter(|&m| (m as f64) < total_minutes)
        .collect();
    minutes.extend([5, 1]);
    minutes.dedup();
    minutes
}

/// 1 回の [`Session::tick`] で起きたこと
#[derive(Debug, Default, PartialEq, Eq)]
pub struct Tick {
    /// 前回からこの tick までの間に切った「あと N 分」
    pub warning: Option<u64>,
    pub finished: bool,
}

/// カウントダウンに、前回の tick の残り秒数を足したもの。「あと N 分」を切った瞬間を見つけるのに使う。
#[derive(Debug, Clone)]
pub struct Session {
    pub countdown: Countdown,
    previous_remaining: Option<u64>,
}

impl Session {
    pub fn new(countdown: Countdown) -> Self {
        Self {
            countdown,
            previous_remaining: None,
        }
    }

    /// 開始 / 一時停止 / 再開
    pub fn toggle(&mut self, now: SystemTime) {
        match self.countdown.state() {
            TimerState::Running => self.countdown.pause(now),
            TimerState::Paused => {
                self.countdown.resume(now);
                self.previous_remaining = Some(ceil_secs(self.countdown.remaining(now)));
            }
            TimerState::Idle | TimerState::Finished => {
                self.countdown.start(now);
                self.previous_remaining = Some(ceil_secs(self.countdown.remaining(now)));
            }
        }
    }

    pub fn reset(&mut self) {
        self.countdown.reset();
        self.previous_remaining = None;
    }

    pub fn tick(&mut self, now: SystemTime) -> Tick {
        if self.countdown.state() != TimerState::Running {
            return Tick::default();
        }

        let finished = self.countdown.check_finished(now);
        let remaining = ceil_secs(self.countdown.remaining(now));
        let warning = match self.previous_remaining {
            Some(previous) if remaining > 0 => warning_minutes(self.countdown.total_duration())
                .into_iter()
                .find(|&m| previous > m * 60 && remaining <= m * 60),
            _ => None,
        };
        self.previous_remaining = Some(remaining);

        Tick { warning, finished }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    const fn mins(m: u64) -> Duration {
        Duration::from_secs(m * 60)
    }

    fn at(secs: u64) -> SystemTime {
        SystemTime::UNIX_EPOCH + Duration::from_secs(1_000_000 + secs)
    }

    #[test]
    fn counts_down_and_finishes() {
        let mut c = Countdown::new(Duration::from_secs(10));
        c.start(at(0));
        assert_eq!(c.remaining(at(4)), Duration::from_secs(6));
        assert!(!c.check_finished(at(9)));
        assert!(c.check_finished(at(10)));
        assert_eq!(c.state(), TimerState::Finished);
        assert_eq!(c.progress(at(10)), 1.0);
    }

    #[test]
    fn pause_keeps_remaining() {
        let mut c = Countdown::new(Duration::from_secs(10));
        c.start(at(0));
        c.pause(at(3));
        assert_eq!(c.remaining(at(100)), Duration::from_secs(7));
        c.resume(at(100));
        assert_eq!(c.remaining(at(102)), Duration::from_secs(5));
    }

    #[test]
    fn start_after_finish_uses_reset_duration() {
        let mut c = Countdown::new(Duration::from_secs(10));
        c.start(at(0));
        c.check_finished(at(10));
        c.start(at(20));
        assert_eq!(c.remaining(at(20)), Duration::from_secs(10));
    }

    #[test]
    fn restore_running_becomes_paused() {
        let mut c = Countdown::new(mins(60));
        c.restore(mins(30), mins(12), true);
        assert_eq!(c.state(), TimerState::Paused);
        assert_eq!(c.total_duration(), mins(30));
        assert_eq!(c.remaining(at(0)), mins(12));
    }

    #[test]
    fn ceil_secs_rounds_up() {
        assert_eq!(ceil_secs(Duration::from_millis(1)), 1);
        assert_eq!(ceil_secs(Duration::from_secs(2)), 2);
        assert_eq!(ceil_secs(Duration::ZERO), 0);
    }

    #[test]
    fn warning_minutes_by_total() {
        assert_eq!(warning_minutes(mins(60)), vec![50, 40, 30, 20, 10, 5, 1]);
        assert_eq!(
            warning_minutes(Duration::from_secs(25 * 60 + 30)),
            vec![20, 10, 5, 1]
        );
        assert_eq!(warning_minutes(mins(10)), vec![5, 1]);
        assert_eq!(warning_minutes(mins(3)), vec![5, 1]);
    }

    #[test]
    fn session_warns_once_when_crossing() {
        let mut s = Session::new(Countdown::new(mins(15)));
        s.toggle(at(0));
        assert_eq!(s.tick(at(299)).warning, None);
        assert_eq!(s.tick(at(300)).warning, Some(10));
        assert_eq!(s.tick(at(301)).warning, None);
        assert_eq!(s.tick(at(600)).warning, Some(5));
        assert_eq!(s.tick(at(840)).warning, Some(1));
        let last = s.tick(at(900));
        assert_eq!(
            last,
            Tick {
                warning: None,
                finished: true
            }
        );
    }

    #[test]
    fn session_skips_warning_when_paused_across_it() {
        let mut s = Session::new(Countdown::new(mins(6)));
        s.toggle(at(0));
        s.tick(at(30));
        s.toggle(at(30)); // 一時停止
        assert_eq!(s.tick(at(500)), Tick::default());
        s.toggle(at(500)); // 再開。残り 5:30
        assert_eq!(s.tick(at(531)).warning, Some(5));
    }

    #[test]
    fn session_does_not_warn_for_threshold_at_start() {
        // ちょうど 5 分で始めたときは、5 分の表示を出さない
        let mut s = Session::new(Countdown::new(mins(5)));
        s.toggle(at(0));
        assert_eq!(s.tick(at(1)).warning, None);
    }
}
