import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { AppHeader } from '../components/AppHeader'
import { CategorySectionView } from '../components/CategorySectionView'
import { CoverCard } from '../components/CoverCard'
import { useCatalogActions } from '../hooks/useCatalogActions'
import type { CategoryListEntry, CategorySection, FollowFindDto, RecommendationDto } from '../api/types'
import { useFollows } from '../state/useFollows'
import { useRequests } from '../state/useRequests'

const HIDDEN_KEY = 'abr.hiddenCategories'

function loadHidden(): string[] {
  try {
    const raw = localStorage.getItem(HIDDEN_KEY)
    return raw ? (JSON.parse(raw) as string[]) : []
  } catch {
    return []
  }
}

function saveHidden(ids: string[]) {
  try {
    localStorage.setItem(HIDDEN_KEY, JSON.stringify(ids))
  } catch {
    // private mode / blocked storage: hiding just lasts for this visit
  }
}

// Same rule as the server: every typed word must appear in the title, description or tags.
function matches(entry: CategoryListEntry, query: string): boolean {
  const words = query.toLowerCase().split(/[\s,-]+/).filter(Boolean)
  if (words.length === 0) return false
  const haystack = `${entry.category.title} ${entry.category.description} ${entry.tags.join(' ')}`.toLowerCase()
  return words.every((w) => haystack.includes(w))
}

// "New" for a book out already, "Out Oct 21" for an announced one.
function releaseBadge(releaseDate: string | null): string | null {
  if (!releaseDate) return null
  const date = new Date(`${releaseDate}T00:00:00`)
  return date.getTime() > Date.now()
    ? `Out ${date.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`
    : 'New'
}

export function Recommended() {
  const navigate = useNavigate()
  const actions = useCatalogActions()
  const { requestedBookIds } = useRequests()
  const { follows } = useFollows()
  const [finds, setFinds] = useState<FollowFindDto[]>([])
  const [picks, setPicks] = useState<RecommendationDto[]>([])
  const [sections, setSections] = useState<CategorySection[]>([])
  const [sectionsLoading, setSectionsLoading] = useState(true)
  const [allCategories, setAllCategories] = useState<CategoryListEntry[]>([])
  const [query, setQuery] = useState('')
  const [hidden, setHidden] = useState<string[]>(loadHidden)

  useEffect(() => {
    api.get<RecommendationDto[]>('/api/books/recommendations').then(setPicks).catch(console.error)
    api.get<CategoryListEntry[]>('/api/browse/categories').then(setAllCategories).catch(console.error)
    api.get<FollowFindDto[]>('/api/follows/finds').then(setFinds).catch(console.error)
    loadSections()
  }, [])

  function loadSections(seed?: number) {
    setSectionsLoading(true)
    api
      .get<CategorySection[]>(`/api/browse/home${seed === undefined ? '' : `?seed=${seed}`}`)
      .then(setSections)
      .catch(console.error)
      .finally(() => setSectionsLoading(false))
  }

  function dismissFind(id: number) {
    setFinds((prev) => prev.filter((f) => f.id !== id))
    api.post(`/api/follows/finds/${id}/dismiss`).catch(console.error)
  }

  function hide(id: string) {
    const next = [...hidden, id]
    setHidden(next)
    saveHidden(next)
  }

  function showAll() {
    setHidden([])
    saveHidden([])
  }

  const visibleSections = sections.filter((s) => !hidden.includes(s.category.id))
  const trimmed = query.trim()
  const found = useMemo(() => allCategories.filter((c) => matches(c, trimmed)), [allCategories, trimmed])

  return (
    <div className="page">
      <AppHeader title="For You" />

      <label>
        Search categories
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder="Try cozy, space, vampires, true crime…"
          type="search"
        />
      </label>

      <p className="following-link">
        <Link to="/following">
          Following{follows.length > 0 ? ` (${follows.length})` : ''} ›
        </Link>
      </p>

      {actions.error && <p className="error">{actions.error}</p>}

      {trimmed.length > 0 ? (
        <ul className="category-results">
          {found.map(({ category }) => (
            <li key={category.id}>
              <Link to={`/category/${category.id}`}>
                <strong>{category.title}</strong>
                <span>{category.description}</span>
              </Link>
            </li>
          ))}
          <li className="category-results-custom">
            <Link to={`/category/q?text=${encodeURIComponent(trimmed)}`}>
              <strong>Search all audiobooks for "{trimmed}"</strong>
              <span>{found.length === 0 ? 'No category matches that, but you can search the whole catalog.' : 'Or search the whole catalog.'}</span>
            </Link>
          </li>
        </ul>
      ) : (
        <>
          {finds.length > 0 && (
            <section className="category-section">
              <div className="category-heading">
                <h2>New from authors you follow</h2>
              </div>
              <p className="category-description">Fresh and upcoming books from the authors and series you follow.</p>
              <div className="cover-shelf">
                {finds.map((find) => (
                  <CoverCard
                    key={find.id}
                    coverUrl={find.item.coverUrl}
                    title={find.item.title}
                    author={find.followName}
                    showText
                    hoursPlacement="none"
                    badge={releaseBadge(find.item.releaseDate)}
                    busy={actions.busyAsin === find.item.asin}
                    onClick={() => actions.open(find.item)}
                  >
                    <span
                      role="button"
                      tabIndex={0}
                      className="find-dismiss"
                      onClick={(e) => {
                        e.stopPropagation()
                        dismissFind(find.id)
                      }}
                      onKeyDown={(e) => {
                        if (e.key === 'Enter') dismissFind(find.id)
                      }}
                    >
                      Dismiss
                    </span>
                  </CoverCard>
                ))}
              </div>
            </section>
          )}

          {picks.length > 0 && (
            <section className="category-section">
              <div className="category-heading">
                <h2>Picked for you</h2>
              </div>
              <p className="category-description">Based on what you've listened to in Jellyfin.</p>
              <div className="cover-shelf">
                {picks.map((rec) => (
                  <CoverCard
                    key={rec.foreignBookId}
                    coverUrl={rec.coverUrl}
                    title={rec.title}
                    author={rec.authorName}
                    showText
                    hoursPlacement="none"
                    badge={requestedBookIds.has(rec.foreignBookId) ? 'Requested' : null}
                    onClick={() => navigate('/book', { state: { book: rec } })}
                  >
                    <span className="cover-reason">{rec.reason}</span>
                  </CoverCard>
                ))}
              </div>
            </section>
          )}

          {sectionsLoading && sections.length === 0 && <p className="subtitle">Finding something good…</p>}

          {visibleSections.map((section) => (
            <CategorySectionView key={section.category.id} section={section} actions={actions} onDismiss={() => hide(section.category.id)} />
          ))}

          {!sectionsLoading && sections.length > 0 && visibleSections.length === 0 && (
            <p className="subtitle">You've hidden everything on this page. Shuffle for a fresh set, or bring them back.</p>
          )}

          <div className="for-you-footer">
            <button onClick={() => loadSections(Math.floor(Math.random() * 1_000_000))} disabled={sectionsLoading}>
              {sectionsLoading ? 'Shuffling…' : 'Shuffle categories'}
            </button>
            {hidden.length > 0 && (
              <button className="retry-toggle" onClick={showAll}>
                Show {hidden.length} hidden categor{hidden.length === 1 ? 'y' : 'ies'}
              </button>
            )}
          </div>
        </>
      )}
    </div>
  )
}
