import { useCallback, useEffect, useState } from 'react'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { DetailableBook, SeriesStatus } from '../api/types'
import { FollowButton } from '../components/FollowButton'
import { ListenButton } from '../components/JellyfinLinks'
import { PlayHereButton } from '../components/PlayHereButton'
import { useAudiobookMetadata } from '../hooks/useAudiobookMetadata'
import { formatReleaseDay, isUnreleased } from '../lib/release'
import { capitalize, formatRuntime, htmlToText } from '../lib/text'
import { useRequests } from '../state/useRequests'

function formatReleaseDate(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'long' })
}

export function BookDetail() {
  const navigate = useNavigate()
  const location = useLocation()
  const book = (location.state as { book?: DetailableBook } | null)?.book ?? null
  const { requestedBookIds, requestingId, requestBook, requestSeries, error } = useRequests()
  const [requestingSeries, setRequestingSeries] = useState(false)

  const { metadata, loading: metadataLoading } = useAudiobookMetadata(book?.title, book?.authorName)

  // How much of this book's series is already owned/requested, once we know which series it belongs to.
  // What was fetched is stored with the series it belongs to, and shown only while that is still the one on screen.
  const seriesTitle = metadata?.seriesTitle
  const seriesAsin = metadata?.seriesAsin
  const authorName = book?.authorName
  const seriesKey = seriesTitle && seriesAsin && authorName ? `${seriesTitle}|${seriesAsin}|${authorName}` : null
  const [seriesResult, setSeriesResult] = useState<{ key: string; status: SeriesStatus | null; message: string | null } | null>(null)
  const seriesStatus = seriesResult !== null && seriesResult.key === seriesKey ? seriesResult.status : null
  const seriesMessage = seriesResult !== null && seriesResult.key === seriesKey ? seriesResult.message : null

  const loadSeriesStatus = useCallback((key: string, title: string, asin: string, author: string) => {
    const params = new URLSearchParams({ seriesTitle: title, seriesAsin: asin, author })
    const keep = (prev: typeof seriesResult) => (prev?.key === key ? prev.message : null)
    api
      .get<SeriesStatus>(`/api/books/series/status?${params}`)
      .then((status) => setSeriesResult((prev) => ({ key, status, message: keep(prev) })))
      .catch(() => setSeriesResult((prev) => ({ key, status: null, message: keep(prev) })))
  }, [])

  useEffect(() => {
    if (!seriesKey || !seriesTitle || !seriesAsin || !authorName) return
    loadSeriesStatus(seriesKey, seriesTitle, seriesAsin, authorName)
  }, [seriesKey, seriesTitle, seriesAsin, authorName, loadSeriesStatus])

  function setMessage(message: string | null) {
    if (!seriesKey) return
    setSeriesResult((prev) => ({ key: seriesKey, status: prev?.key === seriesKey ? prev.status : null, message }))
  }

  async function requestMissing() {
    if (!seriesKey || !seriesTitle || !seriesAsin || !authorName) return
    setRequestingSeries(true)
    setMessage(null)
    const result = await requestSeries(seriesTitle, seriesAsin, authorName)
    if (result) {
      setMessage(
        result.created.length === 0
          ? "Nothing new to request: either your indexers don't have the rest, or they're already requested."
          : `Requested ${result.created.length} book(s).${result.skippedByLimit > 0 ? ` ${result.skippedByLimit} more were skipped by your weekly limit.` : ''}`,
      )
      loadSeriesStatus(seriesKey, seriesTitle, seriesAsin, authorName)
    }
    setRequestingSeries(false)
  }

  if (!book) {
    return (
      <div className="page">
        <button onClick={() => navigate(-1)}>← Back</button>
        <p className="subtitle">This book's details aren't available — go back and open it from the list again.</p>
      </div>
    )
  }

  const alreadyRequested = requestedBookIds.has(book.foreignBookId)
  const notOutYet = isUnreleased(book.releaseDate)
  const requesting = requestingId === book.foreignBookId
  const blocked = alreadyRequested || book.alreadyInLibrary === true
  // Audnexus's "summary" is the full blurb; Chaptarr's own overview is usually a short, truncated
  // excerpt. Prefer the full one once it resolves, falling back to what we already have meanwhile.
  const fullOverview = metadata?.description ?? book.overview

  return (
    <div className="page">
      <button onClick={() => navigate(-1)}>← Back</button>

      <div className="detail-header">
        {book.coverUrl && <img src={book.coverUrl} alt="" className="detail-cover" />}
        <div>
          <h1>{book.title}</h1>
          <p className="subtitle">
            <Link className="author-link" to={`/category/a?q=${encodeURIComponent(book.authorName)}`}>
              {book.authorName}
            </Link>
          </p>
          {book.seriesTitle && <p className="subtitle">{book.seriesTitle}</p>}
          {notOutYet && <span className="release-tag">Not out yet · {formatReleaseDay(book.releaseDate)}</span>}
        </div>
      </div>

      <dl className="detail-meta">
        {book.ratingValue != null && book.ratingVotes != null && book.ratingVotes > 0 && (
          <div>
            <dt>Rating</dt>
            <dd>
              {book.ratingValue.toFixed(2)} ★ ({book.ratingVotes.toLocaleString()} ratings)
            </dd>
          </div>
        )}
        {book.releaseDate && (
          <div>
            <dt>{notOutYet ? 'Releases' : 'Released'}</dt>
            <dd>{notOutYet ? formatReleaseDay(book.releaseDate) : formatReleaseDate(book.releaseDate)}</dd>
          </div>
        )}
        {metadataLoading && (
          <div>
            <dt>Listening time</dt>
            <dd className="subtitle">Looking up…</dd>
          </div>
        )}
        {!metadataLoading && metadata?.runtimeMinutes != null && (
          <div>
            <dt>Listening time</dt>
            <dd>{formatRuntime(metadata.runtimeMinutes)}</dd>
          </div>
        )}
        {!metadataLoading && metadata?.formatType && (
          <div>
            <dt>Edition</dt>
            <dd>{capitalize(metadata.formatType)}</dd>
          </div>
        )}
        {!metadataLoading && metadata && metadata.narrators.length > 0 && (
          <div>
            <dt>Narrated by</dt>
            <dd>{metadata.narrators.join(', ')}</dd>
          </div>
        )}
        {!metadataLoading && metadata?.publisherName && (
          <div>
            <dt>Publisher</dt>
            <dd>{metadata.publisherName}</dd>
          </div>
        )}
        {!metadataLoading && metadata?.language && (
          <div>
            <dt>Language</dt>
            <dd>{capitalize(metadata.language)}</dd>
          </div>
        )}
        {!metadataLoading && metadata && metadata.genres.length > 0 && (
          <div>
            <dt>Genres</dt>
            <dd>{metadata.genres.join(', ')}</dd>
          </div>
        )}
      </dl>

      {!alreadyRequested && !book.alreadyInLibrary && notOutYet && (
        <p className="release-note">
          This book isn't released until {formatReleaseDay(book.releaseDate)}, so there is nothing to download yet. You can still request
          it: it waits in your Upcoming list and is searched for automatically, more often once it's out.
        </p>
      )}

      {fullOverview && <p className="detail-overview">{htmlToText(fullOverview)}</p>}

      {!alreadyRequested && book.alreadyInLibrary === true && (
        <p className="subtitle">You already have this in your Jellyfin library.</p>
      )}
      {!alreadyRequested && book.alreadyInLibrary === true && (
        <>
          <PlayHereButton title={book.title} author={book.authorName} className="primary listen-button listen-detail" />
          <ListenButton title={book.title} author={book.authorName} label="Open in Jellyfin" className="listen-button listen-detail" />
        </>
      )}
      {!alreadyRequested && !book.alreadyInLibrary && book.similarInLibraryTitle && (
        <p className="subtitle">
          You may already have this: "{book.similarInLibraryTitle}" is in your library (possibly a different edition).
        </p>
      )}

      {!metadataLoading && metadata?.bundleBookCount != null && (
        <div className="bundle-callout">
          <p>
            This is a bundle of {metadata.bundleBookCount} books in the <strong>{metadata.seriesTitle}</strong> series.
            Indexers rarely carry the bundle itself, so Request below will request the {metadata.bundleBookCount}{' '}
            individual books instead.
          </p>
        </div>
      )}

      {seriesStatus && (
        <div className="series-block">
          <h3>{seriesStatus.seriesTitle}</h3>
          {seriesAsin && authorName && (
            <FollowButton target={{ kind: 'Series', name: seriesStatus.seriesTitle, seriesAsin, seriesAuthor: authorName }} />
          )}
          <p className="subtitle">
            You have {seriesStatus.owned} of {seriesStatus.total}
            {seriesStatus.requested > 0 ? ` · ${seriesStatus.requested} requested` : ''}
          </p>
          <ul className="series-list">
            {seriesStatus.members.map((m) => (
              <li key={`${m.number}-${m.title}`} className={`series-${m.state}`}>
                <span className="series-mark">{m.state === 'owned' ? '✓' : m.state === 'requested' ? '…' : '○'}</span>
                <span>
                  {m.number > 0 ? `${m.number}. ` : ''}
                  {m.title}
                </span>
              </li>
            ))}
          </ul>
          {seriesStatus.owned + seriesStatus.requested < seriesStatus.total && (
            <button onClick={requestMissing} disabled={requestingSeries}>
              {requestingSeries
                ? 'Requesting…'
                : `Request the ${seriesStatus.total - seriesStatus.owned - seriesStatus.requested} missing book(s)`}
            </button>
          )}
          {seriesMessage && <p className="subtitle">{seriesMessage}</p>}
        </div>
      )}

      {error && <p className="error">{error}</p>}

      <button className="primary detail-request-button" disabled={blocked || requesting} onClick={() => requestBook(book)}>
        {alreadyRequested
          ? 'Requested'
          : book.alreadyInLibrary
            ? 'Already have it'
            : requesting
              ? 'Requesting…'
              : notOutYet
                ? 'Request for release'
                : 'Request'}
      </button>
    </div>
  )
}
