use std::ffi::CString;
use std::path::Path;
use std::sync::Mutex;

use objc2::class;
use objc2::msg_send;
use objc2::rc::Retained;
use objc2::runtime::{AnyObject, Bool};

/// `NSScreenSaverWindowLevel`。全画面のアプリは `NSMainMenuWindowLevel`（24）以下にいるので、
/// これで上に出る（hatto と同じ）
const NS_SCREEN_SAVER_WINDOW_LEVEL: isize = 1000;

pub fn raise_above_fullscreen(window: &tauri::WebviewWindow) -> Result<(), String> {
    let ns_window = window.ns_window().map_err(|e| e.to_string())? as *mut AnyObject;
    if ns_window.is_null() {
        return Err("window has no NSWindow yet".into());
    }
    // SAFETY: `ns_window()` はこのウィンドウの生きている NSWindow を返す。`setLevel:` は NSInteger を 1 つ取る
    unsafe {
        let _: () = msg_send![ns_window, setLevel: NS_SCREEN_SAVER_WINDOW_LEVEL];
    }
    Ok(())
}

/// 鳴っている NSSound。止めるときまで持っておく
struct Sound(Retained<AnyObject>);

// SAFETY: NSSound はどのスレッドから再生・停止してもよい
unsafe impl Send for Sound {}

static SOUND: Mutex<Option<Sound>> = Mutex::new(None);

pub fn play_looping(path: &Path) {
    let Some(path) = path.to_str().and_then(|p| CString::new(p).ok()) else {
        return;
    };
    // SAFETY: NSString と NSSound を作って、ループを指定して鳴らすだけ。
    // `initWithContentsOfFile:byReference:` は読めないファイルなら nil を返す
    let sound: Option<Retained<AnyObject>> = unsafe {
        let ns_path: *mut AnyObject =
            msg_send![class!(NSString), stringWithUTF8String: path.as_ptr()];
        let sound: Option<Retained<AnyObject>> = msg_send![
            msg_send![class!(NSSound), alloc],
            initWithContentsOfFile: ns_path,
            byReference: Bool::YES
        ];
        if let Some(sound) = &sound {
            let _: () = msg_send![sound, setLoops: Bool::YES];
            let _: Bool = msg_send![sound, play];
        }
        sound
    };
    if let (Some(sound), Ok(mut slot)) = (sound, SOUND.lock()) {
        *slot = Some(Sound(sound));
    }
}

pub fn stop_sound() {
    let Some(Sound(sound)) = SOUND.lock().ok().and_then(|mut slot| slot.take()) else {
        return;
    };
    // SAFETY: 鳴らしている NSSound を止める
    unsafe {
        let _: Bool = msg_send![&sound, stop];
    }
}
