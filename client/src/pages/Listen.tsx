import { useEffect, useState } from 'react'
import { Link, NavLink, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import { useAuth } from '../auth/useAuth'
import type { ListenItem, ListenLibrary, TagCount } from '../api/types'
import { AppHeader } from '../components/AppHeader'
import { BookTile } from '../components/BookTile'
import { GetAppLinks } from '../components/JellyfinLinks'
import { LibraryGroups, type GroupBy } from '../components/LibraryGroups'
import { ListenBookSheet } from '../components/ListenBookSheet'
import { SavedBooks } from '../components/SavedBooks'
import { useListenView } from '../hooks/useListenView'
import { useOnline } from '../hooks/useOnline'
import { libraryParams, type ListenFilter as Filter } from '../lib/listenQuery'
import { useOffline } from '../offline/downloadManager'
import { PERSONAL_CHANGED } from '../player/bookmarks'
import { usePlayer } from '../state/usePlayer'

type Mode = 'listen' | 'library'

const FILTERS: Record<Mode, { key: Filter; label: string }[]> = {
  // What the person is in the middle of, and what is already on this device.
  listen: [
    { key: 'resume', label: 'Continue' },
    { key: 'saved', label: 'Downloaded' },
  ],
  // The whole collection, sorted and filtered however they like.
  library: [
    { key: 'all', label: 'A–Z' },
    { key: 'recent', label: 'Recently added' },
    { key: 'authors', label: 'Authors' },
    { key: 'series', label: 'Series' },
    { key: 'rated', label: 'Rated' },
    { key: 'finished', label: 'Finished' },
  ],
}

// A multiple of three, so the last row of the grid is always full; six rows is a screenful or two, not a marathon.
// The library is browsed rather than resumed, so it pages in bigger steps.
const PAGE: Record<Mode, number> = { listen: 18, library: 36 }

// How many books the "Recently added" shelf on the home view holds.
const SHELF = 12

// The views that sort the whole library into piles instead of listing books.
const GROUPINGS: Partial<Record<Filter, GroupBy>> = { authors: 'author', series: 'series' }

function useDebounced<T>(value: T, ms: number): T {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const id = window.setTimeout(() => setDebounced(value), ms)
    return () => window.clearTimeout(id)
  }, [value, ms])
  return debounced
}

/**
 * The person's own Jellyfin audiobooks. "Listening" is what they are part-way through and what is saved on this device;
 * "Library" is everything, to search, sort, filter, or walk through by author or series. Tapping a book opens its page
 * (description, their own rating, tags and bookmark notes) with the play button on it; it does not start playing by itself.
 */
export function Listen({ mode = 'listen' }: { mode?: Mode }) {
  const player = usePlayer()
  const { user } = useAuth()
  const online = useOnline()
  const offline = useOffline()
  const [view, setView] = useListenView()
  const [params, setParams] = useSearchParams()
  const [filter, setFilter] = useState<Filter>(() => {
    if (mode === 'library') {
      const asked = params.get('filter')
      return FILTERS.library.find((f) => f.key === asked)?.key ?? 'all'
    }
    return navigator.onLine ? 'resume' : 'saved'
  })
  const [search, setSearch] = useState('')
  const [activeTag, setActiveTag] = useState<string | null>(null)
  const [tags, setTags] = useState<TagCount[]>([])
  // Bumped when the person's own ratings/tags change, so the list and the tag row show the new state.
  const [personalVersion, setPersonalVersion] = useState(0)
  const [open, setOpen] = useState<ListenItem | null>(null)
  const term = useDebounced(search.trim(), 300)
  const page = PAGE[mode]

  const [items, setItems] = useState<ListenItem[]>([])
  const [recent, setRecent] = useState<ListenItem[]>([])
  const [total, setTotal] = useState(0)
  const [loading, setLoading] = useState(filter !== 'saved' && !GROUPINGS[filter])
  const [error, setError] = useState<string | null>(null)
  const [unreachable, setUnreachable] = useState(false)

  function describe(err: unknown): string {
    return err instanceof ApiError ? err.message : "Couldn't load your books."
  }

  // Going offline mid-visit: show what is on the device rather than a failed list.
  useEffect(() => {
    if (mode === 'listen' && !online && filter !== 'saved') setFilter('saved')
  }, [mode, online, filter])

  const libraryOffline = mode === 'library' && !online
  const groupBy = mode === 'library' ? (GROUPINGS[filter] ?? null) : null
  // The open author or series is part of the address, so the phone's back button returns to the list.
  const groupKey = groupBy ? params.get('group') : null

  useEffect(() => {
    if (filter === 'saved' || libraryOffline || groupBy) return
    let cancelled = false
    const params = libraryParams(filter, term, activeTag, page)

    api
      .get<ListenLibrary>(`/api/listen/library?${params}`)
      .then((lib) => {
        if (cancelled) return
        setItems(lib.items)
        setTotal(lib.total)
        setError(null)
        setUnreachable(false)
        setLoading(false)
      })
      .catch((err) => {
        if (cancelled) return
        // Server not answering (VPN down, away from home): fall back to what's saved on the device, if anything.
        if (mode === 'listen' && !(err instanceof ApiError) && offline.saved.length > 0) {
          setUnreachable(true)
          setFilter('saved')
          return
        }
        setError(describe(err))
        setLoading(false)
      })
    return () => {
      cancelled = true
    }
    // `offline.saved.length` is read only for the fallback decision; refetching when it changes would be wrong.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filter, term, activeTag, personalVersion, mode, libraryOffline, groupBy])

  // The home view (Continue, no search) also offers a shelf of what was added lately, a swipe away.
  const home = mode === 'listen' && filter === 'resume' && !term
  useEffect(() => {
    if (!home) return
    let current = true
    api
      .get<ListenLibrary>(`/api/listen/library?${libraryParams('recent', '', null, SHELF + 6)}`)
      .then((lib) => current && setRecent(lib.items))
      .catch(() => {})
    return () => {
      current = false
    }
  }, [home, personalVersion])

  useEffect(() => {
    if (mode !== 'library') return
    let current = true
    api
      .get<TagCount[]>('/api/listen/tags')
      .then((t) => current && setTags(t))
      .catch(() => {})
    return () => {
      current = false
    }
  }, [mode, personalVersion])

  useEffect(() => {
    const onChange = () => setPersonalVersion((n) => n + 1)
    window.addEventListener(PERSONAL_CHANGED, onChange)
    return () => window.removeEventListener(PERSONAL_CHANGED, onChange)
  }, [])

  async function loadMore() {
    const params = libraryParams(filter, term, activeTag, page, items.length)
    try {
      const lib = await api.get<ListenLibrary>(`/api/listen/library?${params}`)
      setItems((prev) => [...prev, ...lib.items])
      setTotal(lib.total)
    } catch (err) {
      setError(describe(err))
    }
  }

  function chooseFilter(next: Filter, tag: string | null = null) {
    setUnreachable(false)
    setActiveTag(tag)
    if (next !== 'saved' && !GROUPINGS[next]) setLoading(true)
    setFilter(next)
    // Keep the address in step so a reload (or a shared link) lands on the same view, and any open pile is closed.
    if (mode === 'library') setParams(next === 'all' || next === 'tag' ? {} : { filter: next }, { replace: true })
  }

  function pickGroup(key: string | null) {
    setParams(key ? { filter, group: key } : { filter })
  }

  const showingSaved = filter === 'saved'
  // The ordinary list of books (not the device's saved ones, not the author/series piles).
  const plain = !showingSaved && !libraryOffline && !groupBy
  const searchLabel = groupBy === 'author' ? 'Search authors' : groupBy === 'series' ? 'Search series' : showingSaved ? 'Search downloaded books' : 'Search your library'
  const gridClass = `listen-grid${view === 'shelf' ? ' listen-bookshelf' : ''}`
  // Not the ones already shown under Continue.
  const shelf = home ? recent.filter((r) => !items.some((i) => i.id === r.id)).slice(0, SHELF) : []
  const showSearch = mode === 'library' || showingSaved

  return (
    <div className="page">
      <AppHeader title={mode === 'library' ? 'Library' : 'Listen'} />

      <div className="listen-toolbar">
        <nav className="segmented" aria-label="Listen or Library">
          <NavLink to="/listen" end className={({ isActive }) => (isActive ? 'active' : '')}>
            Listening
          </NavLink>
          <NavLink to="/listen/library" className={({ isActive }) => (isActive ? 'active' : '')}>
            Library
          </NavLink>
        </nav>
        <div className="segmented" role="group" aria-label="Layout">
          <button aria-pressed={view === 'grid'} className={view === 'grid' ? 'active' : ''} onClick={() => setView('grid')}>
            Grid
          </button>
          <button aria-pressed={view === 'shelf'} className={view === 'shelf' ? 'active' : ''} onClick={() => setView('shelf')}>
            Shelf
          </button>
        </div>
      </div>

      <p className="subtitle">
        {mode === 'library' ? (
          'Everything in your Jellyfin library. Tap a book to read about it and play it.'
        ) : (
          <>
            Play your audiobooks right here — your place stays in sync with Jellyfin.{' '}
            <Link className="header-link" to="/stats">
              Your listening stats
            </Link>
          </>
        )}
      </p>

      {showSearch && !groupKey && (
        <input
          className="listen-search"
          type="search"
          placeholder={searchLabel}
          value={search}
          onChange={(e) => {
            if (plain) setLoading(true)
            setSearch(e.target.value)
          }}
          aria-label={searchLabel}
          enterKeyHint="search"
          autoCapitalize="none"
          autoCorrect="off"
        />
      )}

      {(!term || showingSaved || groupBy) && (
        <div className="chip-row listen-filters" role="tablist">
          {FILTERS[mode]
            .filter((f) => f.key !== 'saved' || offline.supported)
            .map((f) => (
              <button
                key={f.key}
                role="tab"
                aria-selected={filter === f.key}
                className={`filter-chip${filter === f.key ? ' active' : ''}`}
                disabled={!online && f.key !== 'saved'}
                onClick={() => chooseFilter(f.key)}
              >
                {f.label}
              </button>
            ))}
        </div>
      )}

      {mode === 'library' && !term && tags.length > 0 && (
        <div className="chip-row listen-tags" aria-label="Your tags">
          {tags.map((t) => (
            <button
              key={t.tag}
              className={`filter-chip tag-filter${filter === 'tag' && activeTag === t.tag ? ' active' : ''}`}
              onClick={() => (filter === 'tag' && activeTag === t.tag ? chooseFilter('all') : chooseFilter('tag', t.tag))}
            >
              # {t.tag} <small>{t.count}</small>
            </button>
          ))}
        </div>
      )}

      {(unreachable || !online) && showingSaved && (
        <p className="subtitle offline-note">
          {online ? "Can't reach the server right now" : "You're offline"} — showing the books saved on this device.
        </p>
      )}

      {libraryOffline && (
        <p className="subtitle offline-note">
          You're offline, so the library can't be shown. <Link to="/listen">Books saved on this device</Link> still play.
        </p>
      )}

      {showingSaved && <SavedBooks search={search} view={view} onOpen={setOpen} />}

      {groupBy && !libraryOffline && (
        <LibraryGroups
          by={groupBy}
          groupKey={groupKey}
          search={search}
          view={view}
          playingId={player.current?.id}
          version={personalVersion}
          onOpenBook={setOpen}
          onPick={pickGroup}
        />
      )}

      {plain && error && <p className="error">{error}</p>}
      {plain && loading && !error && <p className="subtitle">Loading…</p>}

      {plain && !loading && !error && items.length === 0 && (
        <p className="subtitle">
          {term ? (
            `Nothing matches "${term}".`
          ) : filter === 'resume' ? (
            <>
              You haven't started a book yet. Pick one from your <Link to="/listen/library">Library</Link>.
            </>
          ) : filter === 'finished' ? (
            "You haven't finished a book yet."
          ) : filter === 'rated' ? (
            'No rated books yet. Open a book and tap the stars.'
          ) : filter === 'tag' ? (
            `Nothing is tagged "${activeTag}" any more.`
          ) : (
            'No audiobooks in your Jellyfin library yet.'
          )}
        </p>
      )}

      {!showingSaved && items.length > 0 && home && (
        <div className="category-heading listen-heading">
          <h2>Continue listening</h2>
        </div>
      )}

      {plain && items.length > 0 && (
        <ul className={gridClass}>
          {items.map((item) => (
            <BookTile key={item.id} item={item} active={player.current?.id === item.id} onOpen={() => setOpen(item)} />
          ))}
        </ul>
      )}

      {plain && items.length < total && !loading && (
        <button className="listen-more" onClick={() => void loadMore()}>
          Show more ({total - items.length} left)
        </button>
      )}

      {!showingSaved && !loading && shelf.length > 0 && (
        <section className="listen-home-shelf">
          <div className="category-heading listen-heading">
            <h2>Recently added</h2>
            <Link className="link-btn" to="/listen/library?filter=recent">
              See all ›
            </Link>
          </div>
          <ul className="listen-shelf">
            {shelf.map((item) => (
              <BookTile key={item.id} item={item} active={player.current?.id === item.id} onOpen={() => setOpen(item)} />
            ))}
          </ul>
        </section>
      )}

      {open && <ListenBookSheet item={open} onClose={() => setOpen(null)} canEdit={user?.role === 'Admin'} />}

      <GetAppLinks />
    </div>
  )
}
