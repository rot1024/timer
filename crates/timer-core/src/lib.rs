//! UI に依存しない部分。OS の API を使わないので、どの OS でもテストできる。

pub mod countdown;
pub mod settings;

pub use countdown::{Countdown, TimerState};
pub use settings::Settings;
