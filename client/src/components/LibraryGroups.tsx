import { useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'
import type { ListenGroup, ListenGroups, ListenItem, ListenLibrary } from '../api/types'
import type { ListenView } from '../hooks/useListenView'
import { BookTile } from './BookTile'
import { ListenCover } from './ListenCover'

export type GroupBy = 'author' | 'series'

const LABEL: Record<GroupBy, { plural: string }> = {
  author: { plural: 'Authors' },
  series: { plural: 'Series' },
}

interface LibraryGroupsProps {
  by: GroupBy
  /** The pile that is open, or null for the list of piles. */
  groupKey: string | null
  /** Narrows the list of piles by name; ignored while one is open. */
  search: string
  view: ListenView
  playingId: string | undefined
  /** Bumped when ratings or tags change, so the open pile shows them. */
  version: number
  onOpenBook: (item: ListenItem) => void
  onPick: (key: string | null) => void
}

function describe(err: unknown): string {
  return err instanceof ApiError ? err.message : "Couldn't load your books."
}

// How often, and how many times, the list is asked again while the server is still looking series up.
const POLL_MS = 20_000
const MAX_POLLS = 30

const countText = (n: number) => `${n} ${n === 1 ? 'book' : 'books'}`

/**
 * The Library sorted into piles, one per author or one per series, so a big collection can be walked through instead of
 * scrolled. Tapping a pile opens its books in reading order (the open pile is part of the page address, so the phone's
 * back button returns to the list).
 */
export function LibraryGroups({ by, groupKey, search, view, playingId, version, onOpenBook, onPick }: LibraryGroupsProps) {
  // What was fetched is kept with what it was fetched for, so switching piles shows "loading" without resetting state in an effect.
  const [listed, setListed] = useState<{ by: GroupBy; groups: ListenGroup[]; pending: number } | { by: GroupBy; error: string } | null>(null)
  const [opened, setOpened] = useState<{ id: string; items: ListenItem[] } | { id: string; error: string } | null>(null)
  // Bumped by a timer while the server is still looking series up, so the list fills in by itself.
  const [refresh, setRefresh] = useState(0)
  const openId = groupKey ? `${by}|${groupKey}` : null

  useEffect(() => {
    let current = true
    api
      .get<ListenGroups>(`/api/listen/groups?by=${by}`)
      .then((data) => current && setListed({ by, groups: data.groups, pending: data.pending }))
      .catch((err) => current && setListed({ by, error: describe(err) }))
    return () => {
      current = false
    }
  }, [by, version, refresh])

  const pending = listed?.by === by && 'pending' in listed ? listed.pending : 0
  useEffect(() => {
    // Only while looking at series, and not forever: a library Audible can't be asked about shouldn't keep a phone busy.
    if (by !== 'series' || pending === 0 || refresh >= MAX_POLLS) return
    const id = window.setTimeout(() => setRefresh((n) => n + 1), POLL_MS)
    return () => window.clearTimeout(id)
  }, [by, pending, refresh])

  useEffect(() => {
    if (!groupKey || !openId) return
    let current = true
    api
      .get<ListenLibrary>(`/api/listen/groups/books?by=${by}&key=${encodeURIComponent(groupKey)}`)
      .then((lib) => current && setOpened({ id: openId, items: lib.items }))
      .catch((err) =>
        current && setOpened({ id: openId, error: err instanceof ApiError && err.status === 404 ? 'That is no longer in your library.' : describe(err) }),
      )
    return () => {
      current = false
    }
  }, [by, groupKey, openId, version])

  const mine = listed?.by === by ? listed : null
  const groups = mine && 'groups' in mine ? mine.groups : null
  const listError = mine && 'error' in mine ? mine.error : null

  if (groupKey) {
    const books = opened?.id === openId ? opened : null
    const pile = groups?.find((g) => g.key === groupKey)
    return (
      <section className="group-open">
        <button type="button" className="link-btn group-back" onClick={() => onPick(null)}>
          ‹ {LABEL[by].plural}
        </button>
        <div className="category-heading listen-heading">
          <h2>{pile?.name ?? ''}</h2>
        </div>
        {pile && (
          <p className="subtitle">
            {pile.by ? `${pile.by} · ` : ''}
            {countText(pile.count)}
            {pile.finished > 0 ? ` · ${pile.finished} finished` : ''}
          </p>
        )}
        {books === null && <p className="subtitle">Loading…</p>}
        {books && 'error' in books && <p className="error">{books.error}</p>}
        {books && 'items' in books && (
          <ul className={`listen-grid${view === 'shelf' ? ' listen-bookshelf' : ''}`}>
            {books.items.map((item) => (
              <BookTile key={item.id} item={item} active={playingId === item.id} onOpen={() => onOpenBook(item)} showNumber />
            ))}
          </ul>
        )}
      </section>
    )
  }

  if (listError) return <p className="error">{listError}</p>
  if (!groups) return <p className="subtitle">Loading…</p>

  const term = search.trim().toLowerCase()
  const shown = term ? groups.filter((g) => g.name.toLowerCase().includes(term) || (g.by ?? '').toLowerCase().includes(term)) : groups
  const note =
    by === 'series' && pending > 0 ? (
      <p className="subtitle group-note">
        Still looking up series for {pending} {pending === 1 ? 'book' : 'books'} — until then they are grouped by their titles.
      </p>
    ) : null
  if (shown.length === 0) {
    return (
      <>
        {note}
        <p className="subtitle">{term ? `Nothing matches "${search.trim()}".` : by === 'series' ? 'No series found in your library.' : 'No audiobooks in your Jellyfin library yet.'}</p>
      </>
    )
  }

  return (
    <>
      {note}
      <ul className="group-list" aria-label={LABEL[by].plural}>
        {shown.map((g) => (
          <li key={g.key}>
            <button type="button" className="group-row" onClick={() => onPick(g.key)}>
              <span className="group-stack" aria-hidden="true">
                {g.coverIds.length === 0 ? (
                  <ListenCover id="" hasCover={false} title={g.name} className="group-cover" />
                ) : (
                  g.coverIds.map((id) => <ListenCover key={id} id={id} hasCover title={g.name} width={120} className="group-cover" />)
                )}
              </span>
              <span className="group-text">
                <strong>{g.name}</strong>
                <small>
                  {g.by ? `${g.by} · ` : ''}
                  {countText(g.count)}
                  {g.finished > 0 ? ` · ${g.finished} finished` : ''}
                </small>
              </span>
              <span className="group-chevron" aria-hidden="true">
                ›
              </span>
            </button>
          </li>
        ))}
      </ul>
    </>
  )
}
