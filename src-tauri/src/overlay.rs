//! 全部のディスプレイに重ねる「あと N 分」「時間です」の表示（作り方は rot1024/hatto の overlay.rs と同じ）。
//!
//! ディスプレイごとに 1 枚ずつウィンドウを出す。`fullscreen` にはせず、ディスプレイと同じ位置と大きさにする。
//! macOS で本当の全画面にすると、専用の Space に切り替わるアニメーションが入るため。

use std::collections::HashSet;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::Mutex;
use std::time::Duration;

use tauri::{AppHandle, Manager, Monitor, PhysicalPosition, WebviewUrl, WebviewWindowBuilder};

/// ページが描き終わったと言ってこなくても、この時間がたったら出す。出ないよりは、ちらつくほうがよい
const READY_TIMEOUT: Duration = Duration::from_secs(2);

/// 表示したときにキーボードのフォーカスを渡すウィンドウ
static FOCUS_ON_REVEAL: Mutex<Option<HashSet<String>>> = Mutex::new(None);

/// 同じラベルのウィンドウが閉じきる前に、次を作ることがあるので、ラベルに通し番号を付ける
static SEQUENCE: AtomicU64 = AtomicU64::new(0);

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Kind {
    /// 「あと N 分」。少し出て、自分で消える。フォーカスを奪わず、クリックも下に通す
    Warning { minutes: u64 },
    /// 「時間です」。OK を押すまで消えない
    Alert,
}

/// 閉じるときに使う、表示の種類（「あと N 分」の分を除いたもの）
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Class {
    Warning,
    Alert,
}

impl Class {
    fn prefix(self) -> &'static str {
        match self {
            Class::Warning => "warning-",
            Class::Alert => "alert-",
        }
    }
}

impl Kind {
    fn class(self) -> Class {
        match self {
            Kind::Warning { .. } => Class::Warning,
            Kind::Alert => Class::Alert,
        }
    }

    /// ページに渡す内容。`window.__OVERLAY__` に入る
    fn init_script(self) -> String {
        let json = match self {
            Kind::Warning { minutes } => format!(r#"{{"kind":"warning","minutes":{minutes}}}"#),
            Kind::Alert => r#"{"kind":"alert"}"#.to_string(),
        };
        format!("window.__OVERLAY__ = {json};")
    }
}

/// 全部のディスプレイに出す。同じ種類のものが出ていたら、先に閉じる
pub fn show(app: &AppHandle, kind: Kind) {
    close(app, kind.class());

    let monitors = match app.available_monitors() {
        Ok(monitors) if !monitors.is_empty() => monitors,
        _ => return,
    };
    let focus = focused_monitor_index(app, &monitors);
    let sequence = SEQUENCE.fetch_add(1, Ordering::Relaxed);

    for (index, monitor) in monitors.iter().enumerate() {
        let takes_focus = kind == Kind::Alert && index == focus;

        let scale = monitor.scale_factor();
        let position = monitor.position().to_logical::<f64>(scale);
        let size = monitor.size().to_logical::<f64>(scale);

        let built = WebviewWindowBuilder::new(
            app,
            format!("{}{sequence}-{index}", kind.class().prefix()),
            WebviewUrl::App("overlay.html".into()),
        )
        .title("Timer")
        .initialization_script(kind.init_script())
        .decorations(false)
        .shadow(false)
        .always_on_top(true)
        .skip_taskbar(true)
        .visible_on_all_workspaces(true)
        .resizable(false)
        .maximizable(false)
        .minimizable(false)
        .position(position.x, position.y)
        .inner_size(size.width, size.height)
        // 透明にしておく。不透明だと、ページを読み込むまで白く光る
        .transparent(true)
        .focused(takes_focus)
        // 起動したままのアプリは自分を前に出せないことが多く、macOS では最初のクリックが
        // ウィンドウを前に出すだけで終わってしまう。OK の 1 回目のクリックを効かせる
        .accept_first_mouse(true)
        .visible(false)
        .build();

        let window = match built {
            Ok(window) => window,
            Err(_) => continue,
        };

        let _ = crate::platform::raise_above_fullscreen(&window);
        if let Kind::Warning { .. } = kind {
            let _ = window.set_ignore_cursor_events(true);
        }

        reveal_later(&window, takes_focus);
    }
}

pub fn close(app: &AppHandle, class: Class) {
    for (label, window) in app.webview_windows() {
        if label.starts_with(class.prefix()) {
            let _ = window.close();
        }
    }
}

/// 隠して作ったウィンドウを、ページが描き終わったら（[`reveal`]）、遅くとも [`READY_TIMEOUT`] たったら出す
pub fn reveal_later(window: &tauri::WebviewWindow, focus: bool) {
    if focus {
        if let Ok(mut labels) = FOCUS_ON_REVEAL.lock() {
            labels
                .get_or_insert_with(HashSet::new)
                .insert(window.label().to_string());
        }
    }

    let app = window.app_handle().clone();
    let label = window.label().to_string();
    std::thread::spawn(move || {
        std::thread::sleep(READY_TIMEOUT);
        let _ = app.clone().run_on_main_thread(move || {
            if let Some(window) = app.get_webview_window(&label) {
                reveal(&window);
            }
        });
    });
}

/// 隠しているウィンドウを出す。2 回目以降の呼び出しでは何もしない
pub fn reveal(window: &tauri::WebviewWindow) {
    if window.is_visible().unwrap_or(true) {
        return;
    }
    if window.show().is_err() {
        return;
    }

    let focus = FOCUS_ON_REVEAL
        .lock()
        .ok()
        .and_then(|mut labels| labels.as_mut().map(|l| l.remove(window.label())))
        .unwrap_or(false);
    if focus {
        let _ = window.set_focus();
    }
}

/// フォーカスを渡すディスプレイ。いま見ているはずの、マウスポインタがあるもの
fn focused_monitor_index(app: &AppHandle, monitors: &[Monitor]) -> usize {
    if let Ok(cursor) = app.cursor_position() {
        if let Some(index) = monitors.iter().position(|m| contains(m, &cursor)) {
            return index;
        }
    }
    app.primary_monitor()
        .ok()
        .flatten()
        .and_then(|primary| {
            monitors
                .iter()
                .position(|m| m.position() == primary.position())
        })
        .unwrap_or(0)
}

fn contains(monitor: &Monitor, point: &PhysicalPosition<f64>) -> bool {
    let position = monitor.position();
    let size = monitor.size();
    let left = f64::from(position.x);
    let top = f64::from(position.y);
    point.x >= left
        && point.x < left + f64::from(size.width)
        && point.y >= top
        && point.y < top + f64::from(size.height)
}
