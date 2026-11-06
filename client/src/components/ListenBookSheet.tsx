import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { ListenDetail, ListenItem } from '../api/types'
import { useAudiobookMetadata } from '../hooks/useAudiobookMetadata'
import { usePersonalBook } from '../hooks/usePersonalBook'
import { progressLine } from '../lib/listenProgress'
import { announcePersonalChange } from '../player/bookmarks'
import { usePlayer } from '../state/usePlayer'
import { BookAbout } from './BookAbout'
import { BookDetailsEditor } from './BookDetailsEditor'
import { BookPersonal } from './BookPersonal'
import { ListenCover } from './ListenCover'
import { StarRating } from './StarRating'

interface ListenBookSheetProps {
  item: ListenItem
  onClose: () => void
  /** An admin may correct the book's author and series from here. */
  canEdit?: boolean
}

/**
 * A book from the Listen or Library tab, opened: what it is about, the person's own rating, tags and bookmark notes,
 * and the button that plays it. Tapping a cover opens this rather than starting playback.
 */
export function ListenBookSheet({ item, onClose, canEdit = false }: ListenBookSheetProps) {
  const player = usePlayer()
  const mine = usePersonalBook(item.id)
  // What the server says about the book now: Jellyfin's description (there almost at once, from the home server), and the
  // author and series as the Library shows them -- which an admin's correction can change while this page is open.
  const [own, setOwn] = useState<{ id: string; text: string | null; author?: string | null; series?: string | null; number?: number | null } | null>(null)
  const [reload, setReload] = useState(0)
  const [editing, setEditing] = useState(false)
  const shown = own?.id === item.id ? own : null
  const jellyfinOverview = shown?.text ?? null
  // The server always says (null means none); until it has, or if it leaves them out, what the list showed stands.
  const author = shown?.author !== undefined ? shown.author : item.author
  const series = shown?.series !== undefined ? shown.series : (item.series ?? null)
  const seriesNumber = shown?.number !== undefined ? shown.number : (item.seriesNumber ?? null)
  const { metadata, loading } = useAudiobookMetadata(item.title, author ?? undefined)
  const playing = player.current?.id === item.id

  useEffect(() => {
    let current = true
    api
      .get<ListenDetail>(`/api/listen/items/${item.id}`)
      .then(
        (d) =>
          current &&
          setOwn({ id: item.id, text: d.overview ?? null, author: d.author, series: d.series, number: d.seriesNumber }),
      )
      .catch(() => {})
    return () => {
      current = false
    }
  }, [item.id, reload])

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  function play() {
    void player.play(item.id)
    onClose()
  }

  return (
    <div className="sheet-backdrop" onClick={onClose}>
      <div className="request-sheet listen-book-sheet" role="dialog" aria-modal="true" aria-label={item.title} onClick={(e) => e.stopPropagation()}>
        <button className="sheet-close" onClick={onClose} aria-label="Close">
          ✕
        </button>

        <div className="detail-header listen-book-head">
          <ListenCover id={item.id} hasCover={item.hasCover} title={item.title} width={300} className="detail-cover listen-book-cover" />
          <div>
            <h2>{item.title}</h2>
            {author && <p className="subtitle">{author}</p>}
            <p className="subtitle">{progressLine(item)}</p>
            {series ? (
              <p className="subtitle">
                {series}
                {seriesNumber != null ? ` · Book ${seriesNumber}` : ''}
              </p>
            ) : (
              metadata?.seriesTitle && <p className="subtitle">{metadata.seriesTitle}</p>
            )}
            {canEdit && !editing && (
              <button type="button" className="link-btn" onClick={() => setEditing(true)}>
                Edit details
              </button>
            )}
          </div>
        </div>

        {editing && (
          <BookDetailsEditor
            item={{ ...item, author, series, seriesNumber }}
            onSaved={() => {
              setReload((n) => n + 1)
              announcePersonalChange() // the Library's lists refetch, so the book moves to its new author and series
            }}
            onClose={() => setEditing(false)}
          />
        )}

        <button className="primary listen-book-play" onClick={play}>
          {playing ? 'Open player' : item.played ? 'Listen again' : item.positionSeconds > 0 ? 'Resume' : 'Play'}
        </button>

        <div className="listen-book-rating">
          <span>Your rating</span>
          <StarRating value={mine.personal.rating} onChange={(n) => void mine.setRating(n)} disabled={!mine.loaded} size={26} />
        </div>

        <BookPersonal state={mine} browsing only="tags" />

        <BookAbout metadata={metadata} loading={loading} knownOverview={jellyfinOverview} collapsible />

        <BookPersonal state={mine} browsing only="bookmarks" />
      </div>
    </div>
  )
}
