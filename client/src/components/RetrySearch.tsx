import { useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'
import type { BookRequestDto, BookSearchResult } from '../api/types'
import { BookCard } from './BookCard'
import { releaseInstant } from '../lib/release'

interface RetrySearchProps {
  request: BookRequestDto
  onDone: () => void
}

/** Inline search box for retrying a failed request against a different Chaptarr match. */
export function RetrySearch({ request, onDone }: RetrySearchProps) {
  const [term, setTerm] = useState(request.title)
  const [results, setResults] = useState<BookSearchResult[]>([])
  const [searching, setSearching] = useState(false)
  const [retryingId, setRetryingId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  const searchable = term.trim().length >= 2

  useEffect(() => {
    if (!searchable) return
    const handle = setTimeout(() => {
      setSearching(true)
      api
        .get<BookSearchResult[]>(`/api/books/search?term=${encodeURIComponent(term)}`)
        .then(setResults)
        .catch(console.error)
        .finally(() => setSearching(false))
    }, 350)
    return () => clearTimeout(handle)
  }, [term, searchable])

  const shownResults = searchable ? results : []

  async function retryWith(result: BookSearchResult) {
    setRetryingId(result.foreignBookId)
    setError(null)
    try {
      await api.put(`/api/books/requests/${request.id}/retry`, {
        foreignBookId: result.foreignBookId,
        title: result.title,
        authorName: result.authorName,
        seriesTitle: result.seriesTitle,
        coverUrl: result.coverUrl,
        releaseDate: releaseInstant(result.releaseDate),
      })
      onDone()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to retry')
    } finally {
      setRetryingId(null)
    }
  }

  return (
    <div className="retry-search">
      <input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Title or author" autoFocus />
      {error && <p className="error">{error}</p>}
      {searching && <p className="subtitle">Searching…</p>}
      {!searching && searchable && shownResults.length === 0 && (
        <p className="subtitle">No results for "{term}".</p>
      )}
      <ul className="book-list">
        {shownResults.map((result) => (
          <BookCard
            key={result.foreignBookId}
            coverUrl={result.coverUrl}
            title={result.title}
            authorName={result.authorName}
            seriesTitle={result.seriesTitle}
          >
            <button className="primary" disabled={retryingId === result.foreignBookId} onClick={() => retryWith(result)}>
              {retryingId === result.foreignBookId ? 'Retrying…' : 'Use this'}
            </button>
          </BookCard>
        ))}
      </ul>
    </div>
  )
}
