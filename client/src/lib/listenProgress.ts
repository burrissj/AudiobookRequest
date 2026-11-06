import type { ListenItem } from '../api/types'
import { formatDuration } from '../player/playerMath'

/** Where the person is in the book, in words: "Finished", "3h 20m left of 10h", or just its length. */
export function progressLine(item: Pick<ListenItem, 'played' | 'positionSeconds' | 'durationSeconds'>): string {
  if (item.played) return 'Finished'
  if (item.positionSeconds > 0) return `${formatDuration(Math.max(0, item.durationSeconds - item.positionSeconds))} left of ${formatDuration(item.durationSeconds)}`
  return formatDuration(item.durationSeconds)
}
