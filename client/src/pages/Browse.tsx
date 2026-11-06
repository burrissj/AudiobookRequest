import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { AppHeader } from '../components/AppHeader'
import { BookCard } from '../components/BookCard'
import { InstallBanner } from '../components/InstallBanner'
import type { BookSearchResult } from '../api/types'
import { formatReleaseDay, isUnreleased } from '../lib/release'
import { useRequests } from '../state/useRequests'

const LENGTHS = {
  any: { label: 'Any length', min: 0, max: 0 },
  short: { label: 'Under 5 hours', min: 0, max: 5 },
  medium: { label: '5 to 10 hours', min: 5, max: 10 },
  long: { label: '10 to 20 hours', min: 10, max: 20 },
  epic: { label: 'Over 20 hours', min: 20, max: 0 },
} as const

type LengthKey = keyof typeof LENGTHS

function formatRuntime(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`
}

function describe(result: BookSearchResult, alreadyRequested: boolean): string | undefined {
  const parts: string[] = []
  if (result.alreadyInLibrary && !alreadyRequested) {
    parts.push('Already in your library')
  } else if (result.similarInLibraryTitle && !alreadyRequested) {
    parts.push(`You may already have this: "${result.similarInLibraryTitle}"`)
  }
  if (isUnreleased(result.releaseDate)) parts.push(`Not out yet: ${formatReleaseDay(result.releaseDate)}`)
  if (result.runtimeMinutes != null) parts.push(formatRuntime(result.runtimeMinutes))
  if (result.narrators && result.narrators.length > 0) parts.push(`Narrated by ${result.narrators.join(', ')}`)
  return parts.length > 0 ? parts.join(' · ') : undefined
}

export function Browse() {
  const navigate = useNavigate()
  const [term, setTerm] = useState('')
  const [results, setResults] = useState<BookSearchResult[]>([])
  const [searching, setSearching] = useState(false)
  const [showFilters, setShowFilters] = useState(false)
  const [narrator, setNarrator] = useState('')
  const [length, setLength] = useState<LengthKey>('any')
  const { requestedBookIds, requestingId, error, requestBook } = useRequests()

  const filtering = narrator.trim() !== '' || length !== 'any'

  const searchable = term.trim().length >= 2

  useEffect(() => {
    if (!searchable) return
    const handle = setTimeout(() => {
      setSearching(true)
      const params = new URLSearchParams({ term })
      if (narrator.trim()) params.set('narrator', narrator.trim())
      if (LENGTHS[length].min > 0) params.set('minHours', String(LENGTHS[length].min))
      if (LENGTHS[length].max > 0) params.set('maxHours', String(LENGTHS[length].max))
      api
        .get<BookSearchResult[]>(`/api/books/search?${params}`)
        .then(setResults)
        .catch(console.error)
        .finally(() => setSearching(false))
    }, 350)
    return () => clearTimeout(handle)
  }, [term, narrator, length, searchable])

  // A search that is no longer long enough shows nothing, whatever the last search left behind.
  const shownResults = searchable ? results : []

  return (
    <div className="page">
      <AppHeader title="Browse" />
      <InstallBanner />
      <label>
        Search for a book
        <input
          value={term}
          onChange={(e) => setTerm(e.target.value)}
          placeholder="Title or author"
          // Only where there's a mouse: on a phone, focusing it pops the keyboard every time the tab is opened.
          autoFocus={window.matchMedia('(pointer: fine)').matches}
          enterKeyHint="search"
          autoCapitalize="none"
          autoCorrect="off"
        />
      </label>
      <button className="retry-toggle" onClick={() => setShowFilters(!showFilters)}>
        {showFilters ? 'Hide filters' : filtering ? 'Filters (on)' : 'Filter by narrator or length'}
      </button>
      {showFilters && (
        <div className="filter-row">
          <input value={narrator} onChange={(e) => setNarrator(e.target.value)} placeholder="Narrator" />
          <select value={length} onChange={(e) => setLength(e.target.value as LengthKey)}>
            {Object.entries(LENGTHS).map(([key, { label }]) => (
              <option key={key} value={key}>
                {label}
              </option>
            ))}
          </select>
        </div>
      )}
      {filtering && (
        <p className="subtitle filter-note">
          Narrator and length come from Audible, so only the top few results are checked and books it can't match are hidden.
        </p>
      )}
      {error && <p className="error">{error}</p>}
      {searching && <p className="subtitle">Searching…</p>}
      {!searching && searchable && shownResults.length === 0 && (
        <p className="subtitle">No results for "{term}".</p>
      )}
      <ul className="book-list">
        {shownResults.map((result) => {
          const alreadyRequested = requestedBookIds.has(result.foreignBookId)
          const blocked = alreadyRequested || result.alreadyInLibrary
          const requesting = requestingId === result.foreignBookId
          return (
            <BookCard
              key={result.foreignBookId}
              coverUrl={result.coverUrl}
              title={result.title}
              authorName={result.authorName}
              seriesTitle={result.seriesTitle}
              caption={describe(result, alreadyRequested)}
              onClick={() => navigate('/book', { state: { book: result } })}
            >
              <button
                className="primary"
                disabled={blocked || requesting}
                onClick={(e) => {
                  e.stopPropagation()
                  requestBook(result)
                }}
              >
                {alreadyRequested ? 'Requested' : result.alreadyInLibrary ? 'Already have it' : requesting ? 'Requesting…' : 'Request'}
              </button>
            </BookCard>
          )
        })}
      </ul>
    </div>
  )
}
