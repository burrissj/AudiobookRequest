import { useEffect } from 'react'
import type { BookRequestDto } from '../api/types'
import { useAudiobookMetadata } from '../hooks/useAudiobookMetadata'
import { BookAbout } from './BookAbout'
import { BookCard } from './BookCard'
import { ProgressBar } from './ProgressBar'
import { RequestActions } from './RequestActions'
import { RequestSteps } from './RequestSteps'
import { StatusBadge } from './StatusBadge'
import { formatNextCheck, formatReleaseDay } from '../lib/release'

function formatBytes(bytes: number): string {
  const gb = bytes / 1e9
  if (gb >= 1) {
    return `${gb.toFixed(1)} GB`
  }
  return `${(bytes / 1e6).toFixed(0)} MB`
}

function formatDay(iso: string): string {
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

interface RequestSheetProps {
  request: BookRequestDto
  caption?: string
  canManage: boolean
  isAdmin: boolean
  onClose: () => void
}

/** The full story of one request, opened from its cover: status and progress, plus everything that can be done to it. */
export function RequestSheet({ request: r, caption, canManage, isAdmin, onClose }: RequestSheetProps) {
  const { metadata, loading } = useAudiobookMetadata(r.title, r.author)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="sheet-backdrop" onClick={onClose}>
      <div className="request-sheet" role="dialog" aria-modal="true" aria-label={r.title} onClick={(e) => e.stopPropagation()}>
        <button className="sheet-close" onClick={onClose} aria-label="Close">
          ✕
        </button>
        <ul className="book-list request-sheet-list">
          <BookCard coverUrl={r.coverUrl} title={r.title} authorName={r.author} seriesTitle={r.series} caption={caption || undefined}>
            <div className="request-status">
              <StatusBadge status={r.status} />
              {r.status === 'Downloading' && r.downloadProgressPercent != null && (
                <>
                  <ProgressBar percent={r.downloadProgressPercent} />
                  <span className="progress-label">
                    {Math.round(r.downloadProgressPercent * 100)}%
                    {r.downloadSizeBytes ? ` of ${formatBytes(r.downloadSizeBytes)}` : ''}
                    {r.downloadClientName ? ` via ${r.downloadClientName}` : ''}
                  </span>
                </>
              )}
            </div>
          </BookCard>
          <li className="request-detail">
            <RequestSteps status={r.status} />
            <p className="subtitle request-dates">
              Requested {formatDay(r.requestedAt)} by {r.requestedByUsername}
              {r.snatchedAt && r.status !== 'Ready' ? ` · found ${formatDay(r.snatchedAt)}` : ''}
              {r.readyAt ? ` · ready ${formatDay(r.readyAt)}` : ''}
            </p>
            <BookAbout metadata={metadata} loading={loading} collapsible />
          </li>
          {r.status === 'Upcoming' && (
            <li className="request-actions-row">
              <p className="release-note">
                Not out yet{r.releaseDate ? `: due ${formatReleaseDay(r.releaseDate)}` : ''}. It is looked for now and then in case it
                appears early, and searched for much more often once it's released.
                {r.nextSearchAt ? ` Next check ${formatNextCheck(r.nextSearchAt)}.` : ''}
              </p>
            </li>
          )}
          <RequestActions request={r} canManage={canManage} isAdmin={isAdmin} />
        </ul>
      </div>
    </div>
  )
}
