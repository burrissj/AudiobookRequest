import type { ListenItem } from '../api/types'
import { formatDuration } from '../player/playerMath'
import { ListenTile } from './ListenTile'

/**
 * One book of the person's Jellyfin library as a tile: how far along they are, or how long it is, under the title.
 * `showNumber` puts its place in the series ("Book 2") in front, for the lists that are about a series or an author.
 */
export function BookTile({ item, active, onOpen, showNumber = false }: { item: ListenItem; active: boolean; onOpen: () => void; showNumber?: boolean }) {
  const fraction = item.durationSeconds > 0 ? Math.min(1, item.positionSeconds / item.durationSeconds) : 0
  const left = Math.max(0, item.durationSeconds - item.positionSeconds)
  const time = item.played ? 'Finished' : item.positionSeconds > 0 ? `${formatDuration(left)} left` : formatDuration(item.durationSeconds)
  const number = showNumber && item.seriesNumber != null ? `Book ${item.seriesNumber} · ` : ''
  return (
    <ListenTile
      id={item.id}
      hasCover={item.hasCover}
      title={item.title}
      author={item.author}
      meta={`${number}${time}`}
      fraction={fraction}
      finished={item.played}
      rating={item.myRating}
      active={active}
      onOpen={onOpen}
    />
  )
}
