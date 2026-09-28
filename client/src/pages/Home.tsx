import { useEffect, useMemo, useState } from 'react'
import { api, ApiError } from '../api/client'
import { useRequestUpdates } from '../api/notifications'
import type { BookRequestDto, BookRequestStatus, BookSearchResult } from '../api/types'
import { useAuth } from '../auth/AuthContext'

const STATUS_LABEL: Record<BookRequestStatus, string> = {
  Requested: 'Requested',
  Searching: 'Searching…',
  Downloading: 'Downloading…',
  Processing: 'Processing…',
  Ready: 'Ready to listen',
  Failed: 'Failed',
}

export function Home() {
  const { user, logout } = useAuth()
  const [term, setTerm] = useState('')
  const [results, setResults] = useState<BookSearchResult[]>([])
  const [searching, setSearching] = useState(false)
  const [requests, setRequests] = useState<BookRequestDto[]>([])
  const [requesting, setRequesting] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

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

  useRequestUpdates(mergeRequest)

  useEffect(() => {
    if (term.trim().length < 2) {
      setResults([])
      return
    }
    const handle = setTimeout(() => {
      setSearching(true)
      api
        .get<BookSearchResult[]>(`/api/books/search?term=${encodeURIComponent(term)}`)
        .then(setResults)
        .catch(console.error)
        .finally(() => setSearching(false))
    }, 350)
    return () => clearTimeout(handle)
  }, [term])

  const requestedBookIds = useMemo(() => new Set(requests.map((r) => r.readarrForeignBookId)), [requests])

  async function requestBook(result: BookSearchResult) {
    setRequesting(result.foreignBookId)
    setError(null)
    try {
      const created = await api.post<BookRequestDto>('/api/books/requests', {
        foreignBookId: result.foreignBookId,
        foreignAuthorId: result.foreignAuthorId,
        title: result.title,
        authorName: result.authorName,
        seriesTitle: result.seriesTitle,
        coverUrl: result.coverUrl,
      })
      mergeRequest(created)
      setResults((prev) => prev.filter((r) => r.foreignBookId !== result.foreignBookId))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to request book')
    } finally {
      setRequesting(null)
    }
  }

  return (
    <div className="page home-page">
      <header className="home-header">
        <h1>Audiobook Request</h1>
        <button onClick={() => logout()}>Sign out</button>
      </header>
      <p className="subtitle">
        Signed in as <strong>{user?.username}</strong>
      </p>

      <section>
        <label>
          Search for a book
          <input value={term} onChange={(e) => setTerm(e.target.value)} placeholder="Title or author" />
        </label>
        {error && <p className="error">{error}</p>}
        {searching && <p className="subtitle">Searching…</p>}
        <ul className="book-list">
          {results.map((result) => {
            const alreadyRequested = requestedBookIds.has(result.foreignBookId)
            return (
              <li key={result.foreignBookId} className="book-row">
                {result.coverUrl && <img src={result.coverUrl} alt="" className="book-cover" />}
                <div className="book-info">
                  <strong>{result.title}</strong>
                  <span>{result.authorName}</span>
                  {result.seriesTitle && <span className="subtitle">{result.seriesTitle}</span>}
                </div>
                <button
                  className="primary"
                  disabled={alreadyRequested || requesting === result.foreignBookId}
                  onClick={() => requestBook(result)}
                >
                  {alreadyRequested ? 'Requested' : requesting === result.foreignBookId ? 'Requesting…' : 'Request'}
                </button>
              </li>
            )
          })}
        </ul>
      </section>

      <section>
        <h2>My Requests</h2>
        {requests.length === 0 && <p className="subtitle">No requests yet — search above to get started.</p>}
        <ul className="book-list">
          {requests.map((r) => (
            <li key={r.id} className="book-row">
              {r.coverUrl && <img src={r.coverUrl} alt="" className="book-cover" />}
              <div className="book-info">
                <strong>{r.title}</strong>
                <span>{r.author}</span>
              </div>
              <span className={`badge ${r.status === 'Ready' ? 'badge-ok' : r.status === 'Failed' ? 'badge-fail' : ''}`}>
                {STATUS_LABEL[r.status]}
              </span>
            </li>
          ))}
        </ul>
      </section>
    </div>
  )
}
