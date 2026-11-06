import type { BrowseItem } from '../api/types'
import { formatReleaseDay, isUnreleased } from './release'

/** "24298" -> "24,000": Audible's counts are fuzzy, so say "over" a rounded figure rather than a false-precise one. */
export function roundedCount(total: number): string {
  if (total < 100) return String(total)
  const digits = Math.floor(Math.log10(total)) + 1
  const step = digits <= 3 ? 10 : 10 ** (digits - 2)
  return (Math.floor(total / step) * step).toLocaleString()
}

export function badgeFor(item: BrowseItem, isRequested: boolean): string | null {
  if (item.alreadyInLibrary) return 'In library'
  if (isRequested) return 'Requested'
  if (item.similarInLibraryTitle) return 'Similar owned'
  if (isUnreleased(item.releaseDate)) return `Out ${formatReleaseDay(item.releaseDate)}`
  return null
}

/** Announced-but-not-out books get their own colour so they stand apart from the "in library" and "requested" badges. */
export function badgeToneFor(item: BrowseItem, isRequested: boolean): 'upcoming' | undefined {
  return !item.alreadyInLibrary && !isRequested && !item.similarInLibraryTitle && isUnreleased(item.releaseDate) ? 'upcoming' : undefined
}
