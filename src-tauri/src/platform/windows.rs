use std::ffi::c_void;
use std::os::windows::ffi::OsStrExt;
use std::path::Path;

use windows::core::{w, PCWSTR};
use windows::Win32::Media::Audio::{
    PlaySoundW, SND_ASYNC, SND_FILENAME, SND_FLAGS, SND_LOOP, SND_NODEFAULT,
};
use windows::Win32::System::Registry::{RegGetValueW, HKEY_CURRENT_USER, RRF_RT_REG_DWORD};

/// WinForms 版と同じく、DWM の `ColorizationColor`（0xAARRGGBB）を読む
pub fn accent_color() -> Option<String> {
    let mut value: u32 = 0;
    let mut size = std::mem::size_of::<u32>() as u32;
    // SAFETY: `value` は DWORD 1 つ分の書き込み先で、その大きさを `size` で渡している
    let result = unsafe {
        RegGetValueW(
            HKEY_CURRENT_USER,
            w!(r"Software\Microsoft\Windows\DWM"),
            w!("ColorizationColor"),
            RRF_RT_REG_DWORD,
            None,
            Some(&mut value as *mut u32 as *mut c_void),
            Some(&mut size),
        )
    };
    if result.is_err() {
        return None;
    }
    Some(format!("#{:06x}", value & 0x00ff_ffff))
}

/// `SoundPlayer.PlayLooping` と同じ `PlaySound` を使う。止めるまで鳴り続ける
pub fn play_looping(path: &Path) {
    let wide: Vec<u16> = path
        .as_os_str()
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();
    // SAFETY: `wide` は NUL で終わるパスで、SND_ASYNC でも PlaySound が呼び出しの間にファイルを開く
    unsafe {
        let _ = PlaySoundW(
            PCWSTR(wide.as_ptr()),
            None,
            SND_FILENAME | SND_ASYNC | SND_LOOP | SND_NODEFAULT,
        );
    }
}

pub fn stop_sound() {
    // SAFETY: NULL を渡すと、鳴っている音を止めるだけ
    unsafe {
        let _ = PlaySoundW(PCWSTR::null(), None, SND_FLAGS(0));
    }
}
