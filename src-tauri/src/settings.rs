//! 設定ファイルの読み書き。置き場所は OS の設定フォルダの `rot1024.Timer/settings.json`
//! （Windows では `%APPDATA%\rot1024.Timer\settings.json` で、WinForms 版と同じ）。
//! 読み書きに失敗しても、アプリは止めない。

use std::path::PathBuf;

use tauri::{AppHandle, Manager};
use timer_core::Settings;

fn path(app: &AppHandle) -> Option<PathBuf> {
    app.path()
        .app_config_dir()
        .ok()
        .map(|dir| dir.join("settings.json"))
}

pub fn load(app: &AppHandle) -> Option<Settings> {
    let text = std::fs::read_to_string(path(app)?).ok()?;
    // WinForms 版は BOM 付きで書いていないが、手で編集されたときのために外しておく
    serde_json::from_str(text.trim_start_matches('\u{feff}')).ok()
}

pub fn save(app: &AppHandle, settings: &Settings) {
    let Some(path) = path(app) else {
        return;
    };
    if let Some(dir) = path.parent() {
        let _ = std::fs::create_dir_all(dir);
    }
    if let Ok(json) = serde_json::to_string_pretty(settings) {
        let _ = std::fs::write(path, json);
    }
}
