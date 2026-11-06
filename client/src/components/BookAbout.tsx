import { useState } from 'react'
import type { AudiobookMetadata } from '../api/types'
import { capitalize, formatRuntime, htmlToText } from '../lib/text'

interface BookAboutProps {
  metadata: AudiobookMetadata | null
  loading: boolean
  /** A description already known (Jellyfin's own, say): shown at once, and kept unless the looked-up one is fuller. */
  knownOverview?: string | null
  /** Show a long description cut short, with "Read more" to open it out. */
  collapsible?: boolean
}

// About this much (a few lines on a phone) before "Read more" is worth offering.
const LONG_TEXT = 320

/** What the book is about: the description, then narrators, length, genres and the like -- the same facts the Browse and For You pages show. */
export function BookAbout({ metadata, loading, knownOverview, collapsible = false }: BookAboutProps) {
  const [expanded, setExpanded] = useState(false)
  const known = knownOverview ? htmlToText(knownOverview) : null
  const looked = metadata?.description ? htmlToText(metadata.description) : null
  const text = (looked?.length ?? 0) > (known?.length ?? 0) ? looked : (known ?? looked)
  const cut = collapsible && !expanded && text !== null && text.length > LONG_TEXT
  const rows: [string, string][] = []
  if (metadata) {
    if (metadata.narrators.length > 0) rows.push(['Narrated by', metadata.narrators.join(', ')])
    if (metadata.runtimeMinutes != null) rows.push(['Listening time', formatRuntime(metadata.runtimeMinutes)])
    if (metadata.formatType) rows.push(['Edition', capitalize(metadata.formatType)])
    if (metadata.publisherName) rows.push(['Publisher', metadata.publisherName])
    if (metadata.language) rows.push(['Language', capitalize(metadata.language)])
    if (metadata.genres.length > 0) rows.push(['Genres', metadata.genres.join(', ')])
  }

  return (
    <div className="book-about">
      {text ? (
        <>
          <p className={`detail-overview${cut ? ' detail-overview-cut' : ''}`}>{text}</p>
          {collapsible && text.length > LONG_TEXT && (
            <button className="link-btn" onClick={() => setExpanded(!expanded)}>
              {expanded ? 'Show less' : 'Read more'}
            </button>
          )}
        </>
      ) : (
        <p className="subtitle">{loading ? 'Looking up the description…' : 'No description found for this book.'}</p>
      )}
      {rows.length > 0 && (
        <dl className="detail-meta">
          {rows.map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>
  )
}
