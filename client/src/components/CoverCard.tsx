import type { ReactNode } from 'react'
import { formatHours } from '../lib/format'

function HeadphonesIcon() {
  return (
    <svg viewBox="0 0 24 24" width="14" height="14" fill="currentColor" aria-hidden="true">
      <path d="M12 3a8 8 0 0 0-8 8v6a3 3 0 0 0 3 3h1v-8H6v-1a6 6 0 0 1 12 0v1h-2v8h1a3 3 0 0 0 3-3v-6a8 8 0 0 0-8-8z" />
    </svg>
  )
}

interface CoverCardProps {
  coverUrl: string | null
  title: string
  author?: string
  runtimeMinutes?: number | null
  /** A small pill over the cover, e.g. "In library". */
  badge?: string | null
  /** Colours the badge: green for good news, red for trouble. Plain dark when left out. */
  badgeTone?: 'ok' | 'fail' | 'pending' | 'upcoming'
  /** Draw the title and author under the cover (category pages, shelves) instead of just the cover. */
  showText?: boolean
  /** Draw "11 HOURS" under the cover (grid sections) or over it (tilted mosaics). */
  hoursPlacement?: 'below' | 'overlay' | 'none'
  busy?: boolean
  onClick: () => void
  children?: ReactNode
}

/** One tappable book cover, shared by every browse layout so they all read as the same kind of thing. */
export function CoverCard({
  coverUrl,
  title,
  author,
  runtimeMinutes,
  badge,
  badgeTone,
  showText = false,
  hoursPlacement = 'below',
  busy = false,
  onClick,
  children,
}: CoverCardProps) {
  const hours = runtimeMinutes ? (
    <span className={`cover-hours cover-hours-${hoursPlacement}`}>
      <HeadphonesIcon />
      {formatHours(runtimeMinutes)}
    </span>
  ) : null

  return (
    <button type="button" className={`cover-card${busy ? ' cover-busy' : ''}`} onClick={onClick} aria-label={`${title}${author ? ` by ${author}` : ''}`}>
      <span className="cover-frame">
        {coverUrl ? <img src={coverUrl} alt="" loading="lazy" /> : <span className="cover-placeholder">{title}</span>}
        {badge && <span className={`cover-badge${badgeTone ? ` cover-badge-${badgeTone}` : ''}`}>{badge}</span>}
        {hoursPlacement === 'overlay' && hours}
      </span>
      {hoursPlacement === 'below' && hours}
      {showText && (
        <span className="cover-text">
          <strong>{title}</strong>
          {author && <span>{author}</span>}
        </span>
      )}
      {children}
    </button>
  )
}
