// リリースビルドでは、後ろにコンソールのウィンドウを出さない
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

fn main() {
    timer_app::run();
}
