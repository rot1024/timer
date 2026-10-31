use std::time::Duration;

use serde::{Deserialize, Serialize};

use crate::countdown::{ceil_secs, Countdown, TimerState};

pub const DEFAULT_DURATION: Duration = Duration::from_secs(60 * 60);

/// `settings.json` の中身。項目名は WinForms 版（System.Text.Json の既定の PascalCase）と同じにしてあるので、
/// 前の版の設定ファイルをそのまま読める。
#[derive(Debug, Clone, Default, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase", default)]
pub struct Settings {
    pub reset_duration_seconds: u64,
    pub remaining_seconds: u64,
    pub running: bool,
    pub top_most: bool,
    pub window_x: Option<i32>,
    pub window_y: Option<i32>,
}

impl Settings {
    /// 保存しておいた状態からカウントダウンを作る。動いていた場合は一時停止の状態にする
    pub fn countdown(&self) -> Countdown {
        let reset = Duration::from_secs(self.reset_duration_seconds.max(1));
        // 時間切れのまま閉じた場合は、リセットしたときの時間から始める
        let remaining = if self.remaining_seconds > 0 {
            Duration::from_secs(self.remaining_seconds)
        } else {
            reset
        };
        let mut countdown = Countdown::new(reset);
        countdown.restore(reset, remaining, self.running);
        countdown
    }

    /// カウントダウンの今の状態を書き込む（ウィンドウの位置と最前面はそのまま）
    pub fn record(&mut self, countdown: &Countdown, now: std::time::SystemTime) {
        self.reset_duration_seconds = countdown.reset_duration().as_secs().max(1);
        self.remaining_seconds = ceil_secs(countdown.remaining(now));
        self.running = matches!(countdown.state(), TimerState::Running | TimerState::Paused);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_winforms_settings() {
        let json = r#"{
  "ResetDurationSeconds": 1500,
  "RemainingSeconds": 600,
  "Running": true,
  "TopMost": true,
  "WindowX": -1200,
  "WindowY": null
}"#;
        let settings: Settings = serde_json::from_str(json).unwrap();
        assert_eq!(settings.reset_duration_seconds, 1500);
        assert_eq!(settings.window_x, Some(-1200));
        assert_eq!(settings.window_y, None);

        let countdown = settings.countdown();
        assert_eq!(countdown.state(), TimerState::Paused);
        assert_eq!(
            countdown.remaining(std::time::SystemTime::now()),
            Duration::from_secs(600)
        );
    }

    #[test]
    fn missing_fields_fall_back() {
        let settings: Settings = serde_json::from_str("{}").unwrap();
        let countdown = settings.countdown();
        assert_eq!(countdown.state(), TimerState::Idle);
        assert_eq!(countdown.reset_duration(), Duration::from_secs(1));
    }

    #[test]
    fn finished_restores_reset_duration() {
        let settings = Settings {
            reset_duration_seconds: 300,
            remaining_seconds: 0,
            ..Settings::default()
        };
        assert_eq!(
            settings.countdown().remaining(std::time::SystemTime::now()),
            Duration::from_secs(300)
        );
    }
}
