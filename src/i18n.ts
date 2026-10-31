// 表示の言語は、OS が日本語なら日本語、それ以外なら英語にする

const ja = {
  start: "開始",
  resume: "再開",
  pause: "一時停止",
  reset: "リセット",
  timeUp: "時間です",
  ok: "OK",
  minutesLeft: (minutes: number) => `あと${minutes}分`,
};

const en: typeof ja = {
  start: "Start",
  resume: "Resume",
  pause: "Pause",
  reset: "Reset",
  timeUp: "Time's up",
  ok: "OK",
  minutesLeft: (minutes: number) => `${minutes} min left`,
};

export const text = navigator.language.toLowerCase().startsWith("ja") ? ja : en;
export const lang = text === ja ? "ja" : "en";
