import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { BookSearchResult, BrowseItem } from '../api/types'
import { laterRelease } from '../lib/release'
import { useRequests } from '../state/useRequests'

/**
 * What tapping a catalog cover does. Catalog books only exist in Audible's catalog until someone opens
 * one, at which point it is matched to Chaptarr (cached per book) and the normal detail page, with its
 * Request button, takes over.
 */
export function useCatalogActions() {
  const navigate = useNavigate()
  const { requestedBookIds } = useRequests()
  const resolved = useRef(new Map<string, BookSearchResult>())
  const [busyAsin, setBusyAsin] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function open(item: BrowseItem) {
    setError(null)
    let match = resolved.current.get(item.asin)
    if (!match) {
      setBusyAsin(item.asin)
      try {
        match = await api.post<BookSearchResult>('/api/browse/resolve', { title: item.title, author: item.author })
        resolved.current.set(item.asin, match)
      } catch (err) {
        setError(err instanceof ApiError ? err.message : 'Could not look that book up')
        return
      } finally {
        setBusyAsin(null)
      }
    }
    // Chaptarr may know only the print date; the audiobook's own date from the catalog wins if it is later.
    const releaseDate = laterRelease(match.releaseDate, item.releaseDate)
    navigate('/book', { state: { book: releaseDate === match.releaseDate ? match : { ...match, releaseDate } } })
  }

  /** Requested on the server when the page loaded, or requested from this session since. */
  function isRequested(item: BrowseItem): boolean {
    const match = resolved.current.get(item.asin)
    return item.alreadyRequested || (match !== undefined && requestedBookIds.has(match.foreignBookId))
  }

  return { open, busyAsin, error, clearError: () => setError(null), isRequested }
}
