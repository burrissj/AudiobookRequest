import type { ListenChapter } from '../api/types'

export const MIN_SPEED = 0.25
export const MAX_SPEED = 3
/** How far the - and + buttons move the speed. */
export const SPEED_STEP = 0.25
/** The smallest change the fine-tune slider makes. */
export const FINE_SPEED_STEP = 0.05

/** The speeds offered as one-tap presets. */
export const SPEED_PRESETS = [0.5, 0.75, 1, 1.25, 1.5, 1.75, 2, 2.5, 3]

/** Snaps to the nearest 0.05 and keeps it inside 0.25x-3x. */
export function clampSpeed(speed: number): number {
  if (!Number.isFinite(speed)) return 1
  const snapped = Math.round(speed / FINE_SPEED_STEP) * FINE_SPEED_STEP
  // Multiplying by 0.05 leaves tiny binary-float residue (1.1500000000000001); keep two decimals.
  return Number(Math.min(MAX_SPEED, Math.max(MIN_SPEED, snapped)).toFixed(2))
}

/**
 * The next quarter-speed notch up (+1) or down (-1). From a fine-tuned 1.15x, + goes to 1.25x and - to 1x, so the
 * buttons always land on the familiar speeds. Stops at the ends rather than wrapping.
 */
export function stepSpeed(speed: number, direction: 1 | -1): number {
  const hundredths = Math.round(clampSpeed(speed) * 100)
  const quarter = SPEED_STEP * 100
  const next = direction > 0 ? (Math.floor(hundredths / quarter) + 1) * quarter : (Math.ceil(hundredths / quarter) - 1) * quarter
  return clampSpeed(next / 100)
}

/** Where a speed sits along the slider, 0 (slowest) to 1 (fastest). */
export function fractionFromSpeed(speed: number): number {
  return (clampSpeed(speed) - MIN_SPEED) / (MAX_SPEED - MIN_SPEED)
}

/** The speed at a point along the slider, 0 (slowest) to 1 (fastest), snapped to the fine steps. */
export function speedFromFraction(fraction: number): number {
  const f = Number.isFinite(fraction) ? Math.min(1, Math.max(0, fraction)) : 0
  return clampSpeed(MIN_SPEED + f * (MAX_SPEED - MIN_SPEED))
}

/** "1.15×" -- always two decimals, as in the speed panel. */
export function formatSpeedExact(speed: number): string {
  return `${clampSpeed(speed).toFixed(2)}×`
}

/** "1x", "1.25x", "0.5x" -- no trailing zeros. */
export function formatSpeed(speed: number): string {
  return `${Number(speed.toFixed(2))}x`
}

/** "1:02:03" for long books, "4:05" under an hour. */
export function formatClock(totalSeconds: number): string {
  const s = Math.max(0, Math.floor(Number.isFinite(totalSeconds) ? totalSeconds : 0))
  const h = Math.floor(s / 3600)
  const m = Math.floor((s % 3600) / 60)
  const sec = s % 60
  const mm = h > 0 ? String(m).padStart(2, '0') : String(m)
  return `${h > 0 ? `${h}:` : ''}${mm}:${String(sec).padStart(2, '0')}`
}

/** "5h 12m" / "42m" -- for lengths in lists. */
export function formatDuration(totalSeconds: number): string {
  const minutes = Math.round(Math.max(0, totalSeconds) / 60)
  const h = Math.floor(minutes / 60)
  const m = minutes % 60
  if (h === 0) return `${m}m`
  return m === 0 ? `${h}h` : `${h}h ${m}m`
}

/** Index of the chapter playing at `position`, or -1 when the book has none. */
export function chapterIndexAt(chapters: ListenChapter[], position: number): number {
  let index = -1
  for (let i = 0; i < chapters.length; i++) {
    if (chapters[i].startSeconds <= position + 0.25) index = i
    else break
  }
  return index
}

/** How long you'll actually listen: remaining time divided by speed. */
export function remainingAtSpeed(duration: number, position: number, speed: number): number {
  return Math.max(0, duration - position) / clampSpeed(speed)
}

const SPEED_KEY = 'abr.player.speed'
const SPEED_BOOK_PREFIX = 'abr.player.speed.'

/** The last speed used for this book, else the last speed used anywhere, else 1x. */
export function loadSpeed(itemId: string): number {
  try {
    const own = localStorage.getItem(SPEED_BOOK_PREFIX + itemId)
    const last = localStorage.getItem(SPEED_KEY)
    const raw = own ?? last
    return raw === null ? 1 : clampSpeed(Number(raw))
  } catch {
    return 1
  }
}

export function saveSpeed(itemId: string, speed: number) {
  try {
    localStorage.setItem(SPEED_BOOK_PREFIX + itemId, String(speed))
    localStorage.setItem(SPEED_KEY, String(speed))
  } catch {
    // Private mode or blocked storage: the speed just isn't remembered.
  }
}
