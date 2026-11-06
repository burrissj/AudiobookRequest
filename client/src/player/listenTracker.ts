export interface ListenDelta {
  content: number
  wall: number
}

/**
 * Decides whether the move from one playback sample to the next was ordinary listening, and how much of it.
 * Anything else -- a seek, a chapter jump, a long gap while the tab was frozen -- returns null so it isn't
 * counted as hours listened. Content time can run up to the playback speed times real time (with slack).
 */
export function listenDelta(prevPosition: number, prevAtMs: number, position: number, atMs: number, speed: number): ListenDelta | null {
  const wall = (atMs - prevAtMs) / 1000
  const content = position - prevPosition
  if (!(wall > 0) || wall > 15 || content < 0) return null
  if (content > wall * Math.max(speed, 1) * 1.5 + 1) return null
  return { content, wall }
}

/** Listening that hasn't yet been delivered to the server; kept until a report carrying it succeeds. */
export class ListenTracker {
  content = 0
  wall = 0
  private lastPosition: number | null = null
  private lastAt = 0

  /** Call on every timeupdate while playing. */
  sample(position: number, atMs: number, speed: number) {
    if (this.lastPosition !== null) {
      const d = listenDelta(this.lastPosition, this.lastAt, position, atMs, speed)
      if (d) {
        this.content += d.content
        this.wall += d.wall
      }
    }
    this.lastPosition = position
    this.lastAt = atMs
  }

  /** Forget the last sample (seek, pause, new book) so the jump isn't counted. */
  breakSpan() {
    this.lastPosition = null
  }

  snapshot(): ListenDelta {
    return { content: this.content, wall: this.wall }
  }

  /** Remove what a successful report delivered, keeping anything accumulated while it was in flight. */
  subtract(sent: ListenDelta) {
    this.content = Math.max(0, this.content - sent.content)
    this.wall = Math.max(0, this.wall - sent.wall)
  }

  reset() {
    this.content = 0
    this.wall = 0
    this.lastPosition = null
  }
}

export interface PendingEntry {
  content: number
  wall: number
  position: number
  title: string
  author: string | null
  mediaSourceId: string | null
  localDate: string
}

const PENDING_KEY = 'abr.listen.pending'

export function loadPending(): Record<string, PendingEntry> {
  try {
    const raw = localStorage.getItem(PENDING_KEY)
    return raw ? (JSON.parse(raw) as Record<string, PendingEntry>) : {}
  } catch {
    return {}
  }
}

/** Saves (or, with null, clears) what hasn't reached the server for one book, so it survives a closed tab or no signal. */
export function updatePending(itemId: string, entry: PendingEntry | null) {
  try {
    const all = loadPending()
    if (entry) all[itemId] = entry
    else delete all[itemId]
    localStorage.setItem(PENDING_KEY, JSON.stringify(all))
  } catch {
    // Storage unavailable: undelivered listening is simply lost, never wrong.
  }
}

/** The listener's own calendar day as yyyy-MM-dd. */
export function localDateString(d = new Date()): string {
  const mm = String(d.getMonth() + 1).padStart(2, '0')
  const dd = String(d.getDate()).padStart(2, '0')
  return `${d.getFullYear()}-${mm}-${dd}`
}
