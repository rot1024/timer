//! 時間になったときの音。`timer.wav` があればループ再生する。
//!
//! 探す場所は、実行ファイルと同じフォルダ（WinForms 版と同じ）と、設定ファイルと同じフォルダ
//! （macOS の .app の中には置きにくいので）。

use std::path::PathBuf;

use tauri::{AppHandle, Manager};

const FILE_NAME: &str = "timer.wav";

fn find(app: &AppHandle) -> Option<PathBuf> {
    let beside_exe = std::env::current_exe()
        .ok()
        .and_then(|exe| exe.parent().map(|dir| dir.join(FILE_NAME)));
    let in_config = app
        .path()
        .app_config_dir()
        .ok()
        .map(|dir| dir.join(FILE_NAME));
    [beside_exe, in_config]
        .into_iter()
        .flatten()
        .find(|path| path.is_file())
}

pub fn start(app: &AppHandle) {
    stop();
    if let Some(path) = find(app) {
        crate::platform::play_looping(&path);
    }
}

pub fn stop() {
    crate::platform::stop_sound();
}
