import { invoke } from "@tauri-apps/api/core";
import { listen } from "@tauri-apps/api/event";
import { lang, text } from "./i18n";
import { adjustTime, formatTime, parseTime } from "./time";

type Snapshot = {
  state: "idle" | "running" | "paused" | "finished";
  remaining: number;
  progress: number;
  canEdit: boolean;
};

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

const remain = $<HTMLButtonElement>("remain");
const edit = $<HTMLInputElement>("edit");
const progress = $<HTMLDivElement>("progress-value");
const toggle = $<HTMLButtonElement>("toggle");
const reset = $<HTMLButtonElement>("reset");

let current: Snapshot | null = null;
let editing = false;

document.documentElement.lang = lang;
reset.textContent = text.reset;

function render(snapshot: Snapshot) {
  current = snapshot;
  remain.textContent = formatTime(snapshot.remaining);
  remain.classList.toggle("editable", snapshot.canEdit);
  progress.style.width = `${snapshot.progress * 100}%`;
  toggle.textContent =
    snapshot.state === "running"
      ? text.pause
      : snapshot.state === "paused"
        ? text.resume
        : text.start;
}

function beginEdit() {
  if (!current?.canEdit || editing) return;
  editing = true;
  edit.value = remain.textContent ?? "";
  remain.hidden = true;
  edit.hidden = false;
  edit.focus();
  edit.select();
}

async function endEdit(apply: boolean) {
  if (!editing) return;
  editing = false;
  const seconds = apply ? parseTime(edit.value) : null;
  edit.hidden = true;
  remain.hidden = false;
  if (seconds !== null && seconds > 0) {
    render(await invoke<Snapshot>("set_duration", { seconds }));
  }
}

remain.addEventListener("click", beginEdit);
edit.addEventListener("blur", () => void endEdit(true));
edit.addEventListener("keydown", (event) => {
  switch (event.key) {
    case "Enter":
      void endEdit(true);
      break;
    case "Escape":
      void endEdit(false);
      break;
    case "ArrowUp":
    case "ArrowDown": {
      const caret = edit.selectionStart ?? 0;
      const updated = adjustTime(edit.value, caret, event.key === "ArrowUp" ? 1 : -1);
      if (updated !== null) {
        edit.value = updated;
        const position = Math.min(caret, updated.length);
        edit.setSelectionRange(position, position);
      }
      break;
    }
    default:
      return;
  }
  event.preventDefault();
});

toggle.addEventListener("click", async () => render(await invoke<Snapshot>("toggle")));
reset.addEventListener("click", async () => render(await invoke<Snapshot>("reset")));

// WebView の右クリックメニューの代わりに、「常に最前面」のメニューを出す
document.addEventListener("contextmenu", (event) => {
  if (event.target === edit) return;
  event.preventDefault();
  void invoke("show_menu");
});

// Windows のアクセントカラーはアプリから読む。設定を変えたあとに戻ってきたら読み直す
async function applyAccent() {
  const accent = await invoke<string | null>("accent_color");
  if (accent) document.documentElement.style.setProperty("--accent", accent);
}
window.addEventListener("focus", () => void applyAccent());

await listen<Snapshot>("timer", (event) => render(event.payload));
await applyAccent();
render(await invoke<Snapshot>("snapshot"));

// 描き終わってから、ウィンドウを出してもらう
requestAnimationFrame(() => void invoke("page_ready"));
