import { invoke } from "@tauri-apps/api/core";
import { lang, text } from "./i18n";

// アプリがウィンドウを作るときに入れておく
type Overlay = { kind: "warning"; minutes: number } | { kind: "alert" };
declare global {
  interface Window {
    __OVERLAY__?: Overlay;
  }
}

const FADE_MS = 180;
// 「あと N 分」を出しておく時間
const WARNING_MS = 2000;

const overlay: Overlay = window.__OVERLAY__ ?? { kind: "alert" };
const message = document.getElementById("message") as HTMLParagraphElement;
const ok = document.getElementById("ok") as HTMLButtonElement;

const wait = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

async function fadeOut() {
  document.body.classList.remove("shown");
  await wait(FADE_MS);
}

document.documentElement.lang = lang;

if (overlay.kind === "warning") {
  message.textContent = text.minutesLeft(overlay.minutes);
  document.body.style.setProperty("--backdrop-opacity", "0.55");
} else {
  message.textContent = text.timeUp;
  document.body.classList.add("alert");
  document.body.style.setProperty("--backdrop-opacity", "0.7");
  ok.textContent = text.ok;
  ok.hidden = false;

  let dismissed = false;
  const dismiss = async () => {
    if (dismissed) return;
    dismissed = true;
    await fadeOut();
    await invoke("dismiss_alert");
  };
  ok.addEventListener("click", () => void dismiss());
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") void dismiss();
  });
}

document.addEventListener("contextmenu", (event) => event.preventDefault());

// 描き終わってからウィンドウを出してもらい、出てからフェードインする
requestAnimationFrame(async () => {
  await invoke("page_ready");
  document.body.classList.add("shown");

  if (overlay.kind === "warning") {
    await wait(FADE_MS + WARNING_MS);
    await fadeOut();
    await invoke("overlay_done");
  } else {
    ok.focus();
  }
});
