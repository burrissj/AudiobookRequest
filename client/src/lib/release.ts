/**
 * Release dates arrive as "2026-11-05" (the catalog) or "2026-11-05T00:00:00+00:00" (Chaptarr and the server). Either way the
 * calendar day is the first ten characters, so that is what is compared and shown -- going through a local-time Date would
 * show the day before for anyone west of Greenwich. The server decides the same way: a book is out from midnight UTC on its day.
 */
export function releaseDay(iso: string | null | undefined): string | null {
  const day = iso?.slice(0, 10)
  return day && /^\d{4}-\d{2}-\d{2}$/.test(day) ? day : null
}

/** The release day as the instant the server expects (midnight UTC), or null when there is none. */
export function releaseInstant(iso: string | null | undefined): string | null {
  const day = releaseDay(iso)
  return day ? `${day}T00:00:00Z` : null
}

/** True while the book's release day is still ahead. No date, or a garbled one, counts as released. */
export function isUnreleased(iso: string | null | undefined, now: Date = new Date()): boolean {
  const day = releaseDay(iso)
  return day !== null && day > now.toISOString().slice(0, 10)
}

/** The later of two release dates -- an audiobook can come out after the print edition, so whichever is further off wins. */
export function laterRelease(a: string | null | undefined, b: string | null | undefined): string | null {
  const x = releaseDay(a)
  const y = releaseDay(b)
  if (x && y) return x >= y ? x : y
  return x ?? y
}

/** "Nov 5", or "Nov 5, 2027" when it isn't this year. */
export function formatReleaseDay(iso: string | null | undefined, now: Date = new Date()): string {
  const day = releaseDay(iso)
  if (!day) return ''
  const [y, m, d] = day.split('-').map(Number)
  const date = new Date(y, m - 1, d)
  return date.toLocaleDateString(undefined, y === now.getFullYear() ? { month: 'short', day: 'numeric' } : { month: 'short', day: 'numeric', year: 'numeric' })
}

/** "today at 3:20 PM" / "tomorrow" / "Nov 3" -- for when an upcoming book is next looked for. */
export function formatNextCheck(iso: string | null | undefined, now: Date = new Date()): string {
  if (!iso) return ''
  const when = new Date(iso)
  if (Number.isNaN(when.getTime())) return ''
  if (when.getTime() <= now.getTime()) return 'shortly'
  const sameDay = when.toDateString() === now.toDateString()
  const time = when.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
  if (sameDay) return `today around ${time}`
  const tomorrow = new Date(now)
  tomorrow.setDate(now.getDate() + 1)
  if (when.toDateString() === tomorrow.toDateString()) return `tomorrow around ${time}`
  return when.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
}
