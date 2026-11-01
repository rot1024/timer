//! アプリ本体。カウントダウンはこのプロセスのスレッドで回し、ページは表示するだけにする。
//! 裏に回ったウィンドウでは、WebView の中のタイマーが遅れることがあるため。

mod overlay;
mod platform;
mod settings;
mod sound;

use std::sync::Mutex;
use std::time::{Duration, SystemTime};

use serde::Serialize;
use tauri::menu::{CheckMenuItem, Menu};
use tauri::window::{ProgressBarState, ProgressBarStatus};
use tauri::{
    AppHandle, Emitter, Manager, PhysicalPosition, RunEvent, State, WebviewUrl,
    WebviewWindowBuilder, WindowEvent,
};
use timer_core::countdown::{ceil_secs, Session};
use timer_core::{Countdown, Settings, TimerState};

pub const MAIN: &str = "main";
const MENU_TOP_MOST: &str = "top-most";

/// WebView2 の起動オプション（Windows だけで効く）。全部のウィンドウで同じにする（違うと同じプロセスで動かせない）。
///
/// 前半は wry の既定。`--force-color-profile=srgb` は、Chromium がディスプレイの色プロファイルに合わせて
/// 色を変換しないようにする。変換されると、アクセントカラーが WinForms 版（GDI はそのまま出す）よりくすんで見える
pub const BROWSER_ARGS: &str =
    "--disable-features=msWebOOUI,msPdfOOUI,msSmartScreenProtection --force-color-profile=srgb";

/// 1 秒ちょうどだと遅れで表示の秒が飛ぶので、細かく回す
const TICK_INTERVAL: Duration = Duration::from_millis(200);

/// ウィンドウの中身の大きさ（論理ピクセル）。WinForms 版の ClientSize（600x284、物理ピクセルのままだった）の半分
const MAIN_WIDTH: f64 = 300.0;
const MAIN_HEIGHT: f64 = 142.0;

struct Inner {
    session: Session,
    settings: Settings,
    /// 最後にページへ送った表示。変わったときだけ送る
    shown: Option<(TimerState, u64)>,
}

struct AppData(Mutex<Inner>);

/// ページに渡す今の状態
#[derive(Debug, Clone, Serialize)]
#[serde(rename_all = "camelCase")]
struct Snapshot {
    state: TimerState,
    /// 残りの秒数（切り上げ）
    remaining: u64,
    progress: f64,
    can_edit: bool,
}

impl Inner {
    fn snapshot(&self, now: SystemTime) -> Snapshot {
        let countdown = &self.session.countdown;
        Snapshot {
            state: countdown.state(),
            remaining: ceil_secs(countdown.remaining(now)),
            progress: countdown.progress(now),
            can_edit: countdown.can_edit(),
        }
    }
}

/// セッションを変えて、表示が変わっていたらメインのウィンドウとタスクバーの進み具合を更新する。
/// ウィンドウの操作は、ロックを放してからする（メインスレッドを待つ操作がロックを待って止まらないように）
fn update<T>(app: &AppHandle, f: impl FnOnce(&mut Session, SystemTime) -> T) -> (T, Snapshot) {
    let now = SystemTime::now();
    let (value, snapshot, changed) = with_inner(app, |inner| {
        let value = f(&mut inner.session, now);
        let snapshot = inner.snapshot(now);
        let key = (snapshot.state, snapshot.remaining);
        let changed = inner.shown != Some(key);
        inner.shown = Some(key);
        (value, snapshot, changed)
    });

    if changed {
        let _ = app.emit_to(MAIN, "timer", snapshot.clone());
        if let Some(window) = app.get_webview_window(MAIN) {
            let _ = window.set_progress_bar(progress_bar(&snapshot));
        }
    }
    (value, snapshot)
}

fn progress_bar(snapshot: &Snapshot) -> ProgressBarState {
    let percent = (snapshot.progress * 100.0).round() as u64;
    let (status, progress) = match snapshot.state {
        TimerState::Running => (ProgressBarStatus::Normal, Some(percent)),
        TimerState::Paused => (ProgressBarStatus::Paused, Some(percent)),
        TimerState::Finished => (ProgressBarStatus::Error, Some(100)),
        TimerState::Idle => (ProgressBarStatus::None, None),
    };
    ProgressBarState {
        status: Some(status),
        progress,
    }
}

fn with_inner<T>(app: &AppHandle, f: impl FnOnce(&mut Inner) -> T) -> T {
    let data = app.state::<AppData>();
    let mut inner = data.0.lock().unwrap_or_else(|e| e.into_inner());
    f(&mut inner)
}

#[tauri::command]
fn snapshot(data: State<AppData>) -> Snapshot {
    let inner = data.0.lock().unwrap_or_else(|e| e.into_inner());
    inner.snapshot(SystemTime::now())
}

/// 開始 / 一時停止 / 再開
#[tauri::command]
fn toggle(app: AppHandle) -> Snapshot {
    update(&app, |session, now| session.toggle(now)).1
}

#[tauri::command]
fn reset(app: AppHandle) -> Snapshot {
    sound::stop();
    overlay::close(&app, overlay::Class::Alert);
    update(&app, |session, _| session.reset()).1
}

#[tauri::command]
fn set_duration(app: AppHandle, seconds: u64) -> Snapshot {
    update(&app, |session, _| {
        if seconds > 0 && session.countdown.can_edit() {
            session.countdown.set_duration(Duration::from_secs(seconds));
        }
    })
    .1
}

/// 右クリックのメニュー。WinForms 版ではタイトルバーのメニューにあった「常に最前面」
#[tauri::command]
fn show_menu(app: AppHandle, window: tauri::Window) -> Result<(), String> {
    let checked = with_inner(&app, |inner| inner.settings.top_most);
    let item = CheckMenuItem::with_id(
        &app,
        MENU_TOP_MOST,
        text::top_most(),
        true,
        checked,
        None::<&str>,
    )
    .map_err(|e| e.to_string())?;
    let menu = Menu::with_items(&app, &[&item]).map_err(|e| e.to_string())?;
    window.popup_menu(&menu).map_err(|e| e.to_string())
}

/// ページが描き終わったら呼ぶ。ウィンドウは描き終わるまで隠しておく（白く光らないように）
#[tauri::command]
fn page_ready(window: tauri::WebviewWindow) {
    overlay::reveal(&window);
}

/// 「あと N 分」がフェードアウトし終わったら呼ぶ
#[tauri::command]
fn overlay_done(window: tauri::WebviewWindow) {
    let _ = window.close();
}

/// 「時間です」の OK。全部のディスプレイの表示を閉じて、音を止める
#[tauri::command]
fn dismiss_alert(app: AppHandle) {
    overlay::close(&app, overlay::Class::Alert);
    sound::stop();
}

/// OS のアクセントカラー（`#rrggbb`）。取れない OS では null で、ページの既定の色を使う
#[tauri::command]
fn accent_color() -> Option<String> {
    platform::accent_color()
}

fn toggle_top_most(app: &AppHandle) {
    let top_most = with_inner(app, |inner| {
        inner.settings.top_most = !inner.settings.top_most;
        inner.settings.top_most
    });
    if let Some(window) = app.get_webview_window(MAIN) {
        let _ = window.set_always_on_top(top_most);
    }
}

fn spawn_ticker(app: AppHandle) {
    std::thread::spawn(move || loop {
        std::thread::sleep(TICK_INTERVAL);

        let (tick, _) = update(&app, |session, now| session.tick(now));

        if tick.finished {
            let app2 = app.clone();
            let _ = app.run_on_main_thread(move || {
                overlay::close(&app2, overlay::Class::Warning);
                sound::start(&app2);
                overlay::show(&app2, overlay::Kind::Alert);
            });
        } else if let Some(minutes) = tick.warning {
            let app2 = app.clone();
            let _ = app.run_on_main_thread(move || {
                overlay::show(&app2, overlay::Kind::Warning { minutes });
            });
        }
    });
}

fn build_main_window(app: &AppHandle, settings: &Settings) -> tauri::Result<()> {
    let window = WebviewWindowBuilder::new(app, MAIN, WebviewUrl::App("index.html".into()))
        .title(text::app_title())
        .additional_browser_args(BROWSER_ARGS)
        .inner_size(MAIN_WIDTH, MAIN_HEIGHT)
        .resizable(false)
        .maximizable(false)
        .always_on_top(settings.top_most)
        .center()
        .visible(false)
        .build()?;

    if let (Some(x), Some(y)) = (settings.window_x, settings.window_y) {
        let position = PhysicalPosition::new(x, y);
        if is_on_screen(app, position) {
            window.set_position(position)?;
        }
    }

    overlay::reveal_later(&window, true);
    Ok(())
}

/// 前回の位置が、今つながっているディスプレイの作業領域にかかっているか。
/// ディスプレイを外したあとで、見えないところに出てしまわないようにする
fn is_on_screen(app: &AppHandle, position: PhysicalPosition<i32>) -> bool {
    let Ok(monitors) = app.available_monitors() else {
        return false;
    };
    monitors.iter().any(|monitor| {
        let area = monitor.work_area();
        let scale = monitor.scale_factor();
        let (left, top) = (position.x, position.y);
        let right = left + (MAIN_WIDTH * scale) as i32;
        let bottom = top + (MAIN_HEIGHT * scale) as i32;
        let area_right = area.position.x + area.size.width as i32;
        let area_bottom = area.position.y + area.size.height as i32;
        left < area_right
            && right > area.position.x
            && top < area_bottom
            && bottom > area.position.y
    })
}

pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            snapshot,
            toggle,
            reset,
            set_duration,
            show_menu,
            page_ready,
            overlay_done,
            dismiss_alert,
            accent_color,
        ])
        .on_menu_event(|app, event| {
            if event.id() == MENU_TOP_MOST {
                toggle_top_most(app);
            }
        })
        .on_window_event(|window, event| {
            if window.label() != MAIN {
                return;
            }
            match event {
                // 最小化したときにも動いたことになる（Windows では -32000 に動く）ので、それは覚えない
                WindowEvent::Moved(position) if !window.is_minimized().unwrap_or(false) => {
                    with_inner(window.app_handle(), |inner| {
                        inner.settings.window_x = Some(position.x);
                        inner.settings.window_y = Some(position.y);
                    });
                }
                // 全画面の表示が残っていても、メインのウィンドウを閉じたら終わる
                WindowEvent::Destroyed => window.app_handle().exit(0),
                _ => {}
            }
        })
        .setup(|app| {
            let handle = app.handle();
            let settings = settings::load(handle);
            let countdown = match &settings {
                Some(settings) => settings.countdown(),
                None => Countdown::new(timer_core::settings::DEFAULT_DURATION),
            };
            let settings = settings.unwrap_or_default();

            // ウィンドウのイベントが状態を読むので、ウィンドウより先に置く
            app.manage(AppData(Mutex::new(Inner {
                session: Session::new(countdown),
                settings: settings.clone(),
                shown: None,
            })));
            build_main_window(handle, &settings)?;
            spawn_ticker(handle.clone());
            Ok(())
        })
        .build(tauri::generate_context!())
        .expect("failed to build the app")
        .run(|app, event| {
            if let RunEvent::Exit = event {
                let settings = with_inner(app, |inner| {
                    inner
                        .settings
                        .record(&inner.session.countdown, SystemTime::now());
                    inner.settings.clone()
                });
                settings::save(app, &settings);
                sound::stop();
            }
        });
}

/// Rust 側で出す文字。ページの文字は `src/i18n.ts`
mod text {
    fn is_japanese() -> bool {
        sys_locale::get_locale().is_some_and(|locale| locale.starts_with("ja"))
    }

    pub fn app_title() -> &'static str {
        "Timer"
    }

    pub fn top_most() -> &'static str {
        if is_japanese() {
            "常に最前面"
        } else {
            "Always on top"
        }
    }
}
