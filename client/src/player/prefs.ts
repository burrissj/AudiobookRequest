export interface PlayerPrefs {
  /** Seconds the back button jumps. */
  skipBack: number
  /** Seconds the forward button jumps. */
  skipForward: number
  /** Seconds to step back when picking a book up again, so you hear the sentence you stopped in. */
  resumeRewind: number
  /** Start the next book of the series when one ends. */
  autoPlayNext: boolean
}

export const SKIP_CHOICES = [10, 15, 30, 45, 60]
export const REWIND_CHOICES = [0, 5, 10, 20, 30]

export const DEFAULT_PREFS: PlayerPrefs = { skipBack: 15, skipForward: 30, resumeRewind: 10, autoPlayNext: true }

const KEY = 'abr.player.prefs'

function pick(value: unknown, choices: number[], fallback: number): number {
  return typeof value === 'number' && choices.includes(value) ? value : fallback
}

/** Reads saved preferences, ignoring anything that isn't one of the offered choices. */
export function parsePrefs(raw: string | null): PlayerPrefs {
  if (!raw) return DEFAULT_PREFS
  try {
    const v = JSON.parse(raw) as Partial<PlayerPrefs>
    return {
      skipBack: pick(v.skipBack, SKIP_CHOICES, DEFAULT_PREFS.skipBack),
      skipForward: pick(v.skipForward, SKIP_CHOICES, DEFAULT_PREFS.skipForward),
      resumeRewind: pick(v.resumeRewind, REWIND_CHOICES, DEFAULT_PREFS.resumeRewind),
      autoPlayNext: typeof v.autoPlayNext === 'boolean' ? v.autoPlayNext : DEFAULT_PREFS.autoPlayNext,
    }
  } catch {
    return DEFAULT_PREFS
  }
}

export function loadPrefs(): PlayerPrefs {
  try {
    return parsePrefs(localStorage.getItem(KEY))
  } catch {
    return DEFAULT_PREFS
  }
}

export function savePrefs(prefs: PlayerPrefs) {
  try {
    localStorage.setItem(KEY, JSON.stringify(prefs))
  } catch {
    // Private mode: the choice just lasts for this visit.
  }
}

/**
 * How far to step back when playback resumes. A pause of a few seconds needs none; a coffee break gets the usual
 * amount; a long absence gets three times that (up to a minute), because it takes longer to pick the thread back up.
 */
export function smartRewindSeconds(preference: number, pausedForMs: number): number {
  if (preference <= 0 || pausedForMs < 15_000) return 0
  if (pausedForMs < 5 * 60_000) return preference
  return Math.min(60, preference * 3)
}
