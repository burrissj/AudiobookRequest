import type { ReactNode } from 'react'
import { prefetchAudiobookMetadata } from '../hooks/useAudiobookMetadata'
import { DownloadButton } from './DownloadButton'
import { ListenCover } from './ListenCover'
import { MarqueeText } from './MarqueeText'

interface ListenTileProps {
  id: string
  hasCover: boolean
  title: string
  author: string | null
  /** The line under the author: "3h 20m left", "Finished", or the book's length. */
  meta: string
  /** An extra small line, e.g. how much space a saved copy takes. */
  detail?: string
  /** How far through the book, 0 to 1. */
  fraction?: number
  finished?: boolean
  rating?: number | null
  /** This is the book currently loaded in the player. */
  active: boolean
  onOpen: () => void
  children?: ReactNode
}

/** One book in the Listen grid: cover with progress and a small save-for-offline button on it, then title, author and time left. */
export function ListenTile({ id, hasCover, title, author, meta, detail, fraction = 0, finished = false, rating, active, onOpen, children }: ListenTileProps) {
  return (
    <li className="listen-tile">
      <button
        type="button"
        className={`listen-tile-btn${active ? ' active' : ''}`}
        title={title}
        onClick={onOpen}
        // The description is looked up as soon as a finger lands, so it is often there by the time the page opens.
        onPointerDown={() => prefetchAudiobookMetadata(title, author)}
        aria-label={`Open ${title}${author ? ` by ${author}` : ''}`}
      >
        <span className="listen-tile-frame">
          <ListenCover id={id} hasCover={hasCover} title={title} width={240} className="listen-tile-cover" />
          {rating ? <span className="listen-tile-badge">★ {rating}</span> : null}
          {finished && (
            <span className="listen-tile-done" title="Finished">
              ✓
            </span>
          )}
          {fraction > 0 && !finished && (
            <span className="listen-tile-bar" aria-hidden="true">
              <span style={{ width: `${Math.min(1, fraction) * 100}%` }} />
            </span>
          )}
        </span>
        <MarqueeText as="strong" text={title} />
        {author && <small className="listen-tile-author">{author}</small>}
      </button>
      <span className="listen-tile-dl">
        <DownloadButton itemId={id} title={title} />
      </span>
      <small className="listen-tile-meta">{meta}</small>
      {detail && <small className="listen-tile-detail">{detail}</small>}
      {children}
    </li>
  )
}
