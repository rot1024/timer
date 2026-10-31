// 時間の編集で使う、文字と秒数の変換

const HOUR = 3600;
const MINUTE = 60;

export function formatTime(totalSeconds: number): string {
  const hours = Math.floor(totalSeconds / HOUR);
  const minutes = Math.floor((totalSeconds % HOUR) / MINUTE);
  const seconds = totalSeconds % MINUTE;
  return [hours, minutes, seconds].map((n) => String(n).padStart(2, "0")).join(":");
}

/**
 * `HH:MM:SS` か `HH:MM`、または分だけの数値（例: `25`）を秒数にする。読めないときは null
 */
export function parseTime(input: string): number | null {
  const text = input.trim();

  if (/^\d+$/.test(text)) {
    const minutes = Number(text);
    return minutes > 0 ? minutes * MINUTE : null;
  }

  const match = /^(\d+):(\d{1,2})(?::(\d{1,2}))?$/.exec(text);
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  const seconds = Number(match[3] ?? 0);
  if (minutes >= 60 || seconds >= 60) return null;
  return hours * HOUR + minutes * MINUTE + seconds;
}

/**
 * ↑ / ↓ で増減したあとの文字。カーソルがある位置で、時・分・秒のどれを動かすかを決める。
 * 変えられないときは null
 */
export function adjustTime(input: string, caret: number, direction: 1 | -1): string | null {
  const parsed = parseTime(input);
  if (parsed === null) return null;

  const firstColon = input.indexOf(":");
  const secondColon = firstColon < 0 ? -1 : input.indexOf(":", firstColon + 1);
  const step =
    firstColon < 0 || secondColon < 0 || caret <= firstColon
      ? HOUR
      : caret <= secondColon
        ? MINUTE
        : 1;

  if (direction < 0 && step === HOUR && parsed < HOUR) return null;

  return formatTime(Math.max(1, parsed + step * direction));
}
