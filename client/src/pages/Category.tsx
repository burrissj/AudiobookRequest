import { useCallback, useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { BrowseItem, CategoryInfo, CategoryPage } from '../api/types'
import { badgeFor, badgeToneFor, roundedCount } from '../lib/catalog'
import { CoverCard } from '../components/CoverCard'
import { FollowButton } from '../components/FollowButton'
import { useCatalogActions } from '../hooks/useCatalogActions'

type Sort = 'BestSellers' | '-ReleaseDate' | 'Relevance'

const NO_ITEMS: BrowseItem[] = []

type PageOutcome = { result: CategoryPage } | { message: string }

async function fetchCategoryPage(id: string, text: string, sort: Sort | null, page: number): Promise<PageOutcome> {
  try {
    const params = new URLSearchParams({ page: String(page) })
    if (sort) params.set('sort', sort)
    if (id === 'q' || id === 'a') params.set('q', text)
    return { result: await api.get<CategoryPage>(`/api/browse/category/${id}?${params}`) }
  } catch (err) {
    return { message: err instanceof ApiError && err.status === 404 ? "That category doesn't exist." : 'Could not load this category.' }
  }
}

/** Every book in one category, as an endless grid: pick a sort, hide what you already have, load more. */
export function Category() {
  const { id = '' } = useParams()
  const [searchParams] = useSearchParams()
  // Free-text search ('q') and author pages ('a') both carry their text in ?q=; ?text= is the older spelling.
  const text = searchParams.get('q') ?? searchParams.get('text') ?? ''
  const navigate = useNavigate()
  const actions = useCatalogActions()

  const [info, setInfo] = useState<CategoryInfo | null>(null)
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(0)
  const [hasMore, setHasMore] = useState(false)
  const [sort, setSort] = useState<Sort | null>(null)
  const [hideOwned, setHideOwned] = useState(true)

  // Results are stored with the view they belong to (category + search + sort). Anything stored for a different
  // view simply isn't shown, so changing the sort never flashes the old list and nothing has to be cleared by hand.
  const loadKey = `${id}|${text}|${sort ?? ''}`
  const [loaded, setLoaded] = useState<{ key: string; items: BrowseItem[] }>({ key: '', items: [] })
  const [failure, setFailure] = useState<{ key: string; message: string } | null>(null)
  const [loadingMore, setLoadingMore] = useState(false)
  const items = loaded.key === loadKey ? loaded.items : NO_ITEMS
  const error = failure?.key === loadKey ? failure.message : null
  const loading = (loaded.key !== loadKey && error === null) || loadingMore

  // Shows the outcome of a fetch. Kept apart from the fetching itself so effects never set state directly.
  const show = useCallback((key: string, append: boolean, outcome: PageOutcome) => {
    if ('message' in outcome) {
      setFailure({ key, message: outcome.message })
      return
    }
    const { result } = outcome
    setInfo(result.category)
    setTotal(result.totalTitles)
    setPage(result.page)
    setHasMore(result.hasMore)
    setFailure(null)
    setLoaded((prev) => {
      const merged = append && prev.key === key ? [...prev.items, ...result.items] : result.items
      const seen = new Set<string>()
      return { key, items: merged.filter((i) => (seen.has(i.asin) ? false : (seen.add(i.asin), true))) }
    })
  }, [])

  useEffect(() => {
    let current = true
    fetchCategoryPage(id, text, sort, 0).then((outcome) => current && show(loadKey, false, outcome))
    return () => {
      current = false
    }
  }, [id, text, sort, loadKey, show])

  async function loadMore() {
    setLoadingMore(true)
    show(loadKey, true, await fetchCategoryPage(id, text, sort, page + 1))
    setLoadingMore(false)
  }

  const visible = hideOwned ? items.filter((i) => !i.alreadyInLibrary) : items
  const hiddenCount = items.length - visible.length

  return (
    <div className="page">
      <button onClick={() => navigate(-1)}>← Back</button>

      {info && (
        <header className="category-page-header">
          <h1>{info.title}</h1>
          <p className="category-description">{info.description}</p>
          <p className="subtitle">About {roundedCount(total)} titles</p>
          {id === 'a' && <FollowButton target={{ kind: 'Author', name: info.title }} />}
        </header>
      )}

      <div className="category-controls">
        <div className="segmented">
          <button className={(sort ?? 'default') === 'default' ? 'active' : ''} onClick={() => setSort(null)}>
            {info?.hasKeywordSort ? 'Best match' : 'Popular'}
          </button>
          <button className={sort === '-ReleaseDate' ? 'active' : ''} onClick={() => setSort('-ReleaseDate')}>
            Newest
          </button>
        </div>
        <label className="checkbox-label">
          <input type="checkbox" checked={hideOwned} onChange={(e) => setHideOwned(e.target.checked)} />
          Hide what I have
        </label>
      </div>

      {error && <p className="error">{error}</p>}
      {actions.error && <p className="error">{actions.error}</p>}

      <div className="cover-grid cover-grid-text">
        {visible.map((item) => (
          <CoverCard
            key={item.asin}
            coverUrl={item.coverUrl}
            title={item.title}
            author={item.author}
            runtimeMinutes={item.runtimeMinutes}
            badge={badgeFor(item, actions.isRequested(item))}
            badgeTone={badgeToneFor(item, actions.isRequested(item))}
            showText
            busy={actions.busyAsin === item.asin}
            onClick={() => actions.open(item)}
          />
        ))}
      </div>

      {loading && <p className="subtitle">Loading…</p>}
      {!loading && visible.length === 0 && !error && (
        <p className="subtitle">{hiddenCount > 0 ? `You already have everything on this page (${hiddenCount} hidden).` : 'Nothing to show.'}</p>
      )}
      {hiddenCount > 0 && visible.length > 0 && <p className="subtitle">{hiddenCount} you already have are hidden.</p>}

      {hasMore && !loading && (
        <button className="load-more" onClick={() => void loadMore()}>
          Show more
        </button>
      )}
    </div>
  )
}
