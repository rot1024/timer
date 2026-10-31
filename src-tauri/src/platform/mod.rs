//! OS ごとの処理。Windows と macOS 以外では何もしない。

use std::path::Path;

#[cfg(target_os = "macos")]
mod macos;
#[cfg(target_os = "windows")]
mod windows;

#[cfg(target_os = "macos")]
use macos as imp;
#[cfg(target_os = "windows")]
use windows as imp;

/// OS のアクセントカラー（`#rrggbb`）。
/// macOS ではページの CSS の `AccentColor` で取れるので、ここでは返さない
#[cfg(target_os = "windows")]
pub fn accent_color() -> Option<String> {
    imp::accent_color()
}

#[cfg(not(target_os = "windows"))]
pub fn accent_color() -> Option<String> {
    None
}

/// 全画面のアプリより上に出す。Windows は `always_on_top` だけで足りるが、
/// macOS は全画面のアプリのレベルより上にウィンドウのレベルを上げる必要がある
#[cfg(target_os = "macos")]
pub fn raise_above_fullscreen(window: &tauri::WebviewWindow) -> Result<(), String> {
    imp::raise_above_fullscreen(window)
}

#[cfg(not(target_os = "macos"))]
pub fn raise_above_fullscreen(_window: &tauri::WebviewWindow) -> Result<(), String> {
    Ok(())
}

#[cfg(any(target_os = "windows", target_os = "macos"))]
pub fn play_looping(path: &Path) {
    imp::play_looping(path);
}

#[cfg(not(any(target_os = "windows", target_os = "macos")))]
pub fn play_looping(_path: &Path) {}

#[cfg(any(target_os = "windows", target_os = "macos"))]
pub fn stop_sound() {
    imp::stop_sound();
}

#[cfg(not(any(target_os = "windows", target_os = "macos")))]
pub fn stop_sound() {}
