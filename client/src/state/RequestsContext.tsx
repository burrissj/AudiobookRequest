import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { api, ApiError } from '../api/client'
import { useRequestUpdates } from '../api/notifications'
import type { AudiobookMetadata, BookRequestDto, ExpandSeriesResponse } from '../api/types'
import { formatReleaseDay, isUnreleased, releaseInstant } from '../lib/release'
import { RequestsContext, type BookSearchResultLike, type RequestAction } from './useRequests'

// Lifted out of individual pages so the Browse/Requests/Recommended tabs share one SignalR
// subscription and one consistent "already requested" set instead of each re-fetching/re-subscribing.
export function RequestsProvider({ children }: { children: ReactNode }) {
  const [requests, setRequests] = useState<BookRequestDto[]>([])
  const [requestingId, setRequestingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshing, setRefreshing] = useState(false)
  const [cancellingId, setCancellingId] = useState<number | null>(null)
  const [actingId, setActingId] = useState<number | null>(null)

  useEffect(() => {
    api.get<BookRequestDto[]>('/api/books/requests').then(setRequests).catch(console.error)
  }, [])

  // Both the SignalR push and this client's own POST response can deliver the same request
  // (whichever arrives first); merge idempotently so neither path can create a duplicate entry.
  function mergeRequest(updated: BookRequestDto) {
    setRequests((prev) => {
      const exists = prev.some((r) => r.id === updated.id)
      return exists ? prev.map((r) => (r.id === updated.id ? updated : r)) : [updated, ...prev]
    })
  }

  function removeRequest(id: number) {
    setRequests((prev) => prev.filter((r) => r.id !== id))
  }

  useRequestUpdates(mergeRequest, removeRequest)

  const requestedBookIds = useMemo(() => new Set(requests.map((r) => r.chaptarrForeignBookId)), [requests])

  async function requestBook(result: BookSearchResultLike) {
    // A book that isn't out yet can't be downloaded, so a search for it can only fail. It is still worth asking for: it waits
    // in the upcoming queue and is searched for automatically, more and more often as the date nears and once it's out.
    if (
      isUnreleased(result.releaseDate) &&
      !window.confirm(
        `"${result.title}" isn't released yet (due ${formatReleaseDay(result.releaseDate)}), so it can't be downloaded until then.

` +
          `Request it anyway? It will wait in your Upcoming list and be searched for automatically, more often once it's out.`,
      )
    ) {
      return
    }

    // A different edition of this book is already in Jellyfin -- worth a second thought before downloading again.
    if (
      result.similarInLibraryTitle &&
      !window.confirm(`You may already have this: "${result.similarInLibraryTitle}" is in your library. Request it anyway?`)
    ) {
      return
    }

    setRequestingId(result.foreignBookId)
    setError(null)
    try {
      // Catch bundles/omnibuses (e.g. "The Darkwood Trilogy") *before* creating a request for them,
      // regardless of where Request was clicked from -- indexers rarely carry the bundle itself, so a
      // single request for one just sits there failing. Best-effort: metadata resolution never throws,
      // so a slow/down lookup just silently falls through to a normal single-book request below.
      const params = new URLSearchParams({ title: result.title, author: result.authorName })
      const metadata = await api.get<AudiobookMetadata>(`/api/books/metadata?${params}`).catch(() => null)

      if (metadata?.bundleBookCount && metadata.seriesTitle && metadata.seriesAsin) {
        const expanded = await api.post<ExpandSeriesResponse>('/api/books/series/expand', {
          seriesTitle: metadata.seriesTitle,
          seriesAsin: metadata.seriesAsin,
          authorName: result.authorName,
        })
        expanded.created.forEach(mergeRequest)
        if (expanded.created.length === 0) {
          setError("Couldn't find matching books for this series in your Chaptarr catalog.")
        } else if (expanded.skippedByLimit > 0) {
          setError(`Weekly request limit reached: ${expanded.skippedByLimit} book(s) of this series were not requested.`)
        }
        return
      }

      const created = await api.post<BookRequestDto>('/api/books/requests', {
        foreignBookId: result.foreignBookId,
        title: result.title,
        authorName: result.authorName,
        seriesTitle: result.seriesTitle,
        coverUrl: result.coverUrl,
        releaseDate: releaseInstant(result.releaseDate),
      })
      mergeRequest(created)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to request book')
    } finally {
      setRequestingId(null)
    }
  }

  // Forces an immediate Chaptarr poll + import pass server-side (rather than waiting for the next
  // ~20s background tick) and replaces the list with its authoritative result.
  async function refreshRequests() {
    setRefreshing(true)
    setError(null)
    try {
      const fresh = await api.post<BookRequestDto[]>('/api/books/requests/refresh')
      setRequests(fresh)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to refresh')
    } finally {
      setRefreshing(false)
    }
  }

  // Cancels a request so it can be requested again from scratch; the server also best-effort removes
  // the book from Chaptarr. Removes it from local state immediately rather than waiting on the
  // SignalR "requestRemoved" push (own-action feedback shouldn't depend on the socket round-trip).
  async function cancelRequest(id: number) {
    setCancellingId(id)
    setError(null)
    try {
      await api.delete(`/api/books/requests/${id}`)
      removeRequest(id)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to cancel request')
    } finally {
      setCancellingId(null)
    }
  }

  // search-again / approve / deny / wishlist all return the updated request; merge it straight in.
  async function actOnRequest(id: number, action: RequestAction, body?: unknown) {
    setActingId(id)
    setError(null)
    try {
      const updated =
        action === 'wishlist'
          ? await api.put<BookRequestDto>(`/api/books/requests/${id}/wishlist`, body)
          : await api.post<BookRequestDto>(`/api/books/requests/${id}/${action}`, body)
      mergeRequest(updated)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'That action failed')
    } finally {
      setActingId(null)
    }
  }

  async function requestSeries(seriesTitle: string, seriesAsin: string, authorName: string) {
    setError(null)
    try {
      const expanded = await api.post<ExpandSeriesResponse>('/api/books/series/expand', { seriesTitle, seriesAsin, authorName })
      expanded.created.forEach(mergeRequest)
      return expanded
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to request the series')
      return null
    }
  }

  return (
    <RequestsContext.Provider
      value={{
        requests,
        requestedBookIds,
        requestingId,
        error,
        requestBook,
        refreshing,
        refreshRequests,
        cancellingId,
        cancelRequest,
        actingId,
        actOnRequest,
        requestSeries,
      }}
    >
      {children}
    </RequestsContext.Provider>
  )
}
