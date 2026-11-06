import { useState } from 'react'
import type { BookRequestDto } from '../api/types'
import { useRequests } from '../state/useRequests'
import { FixImportPanel } from './FixImportPanel'
import { isImportBlocked } from '../lib/importBlocked'
import { ListenButton } from './JellyfinLinks'
import { PlayHereButton } from './PlayHereButton'
import { RetrySearch } from './RetrySearch'

interface RequestActionsProps {
  request: BookRequestDto
  /** The requester or an admin -- everyone else sees the status only. */
  canManage: boolean
  isAdmin: boolean
}

/** Per-request controls: approve/decline, search again, keep trying, retry with a different match, cancel, plus Chaptarr's diagnostic. */
export function RequestActions({ request: r, canManage, isAdmin }: RequestActionsProps) {
  const { cancellingId, cancelRequest, actingId, actOnRequest } = useRequests()
  const [retryOpen, setRetryOpen] = useState(false)
  const [confirmCancel, setConfirmCancel] = useState(false)
  const [showDiagnostic, setShowDiagnostic] = useState(false)
  const [fixOpen, setFixOpen] = useState(false)

  const busy = actingId === r.id
  const awaitingSearch = r.status === 'Requested' || r.status === 'Searching'
  const upcoming = r.status === 'Upcoming'
  const hasDiagnostic = r.diagnostic != null && r.status !== 'Ready'

  if (r.status === 'Ready') {
    // Mostly the useful thing now is to go and listen. But a book deleted from Jellyfin leaves this entry behind,
    // so whoever owns it (or an admin) can clear it away; the audiobook file itself is never touched.
    return (
      <li className="request-actions-row">
        <PlayHereButton title={r.title} author={r.author} className="primary listen-button" />
        <ListenButton title={r.title} author={r.author} label="Open in Jellyfin" className="listen-button" />
        {canManage && (
          <button
            className="retry-toggle"
            disabled={cancellingId === r.id}
            onClick={() => {
              if (window.confirm(`Remove "${r.title}" from your list? The audiobook itself is not deleted.`)) void cancelRequest(r.id)
            }}
          >
            {cancellingId === r.id ? 'Removing…' : 'Remove from list'}
          </button>
        )}
      </li>
    )
  }

  if ((!canManage && !(isAdmin && r.status === 'PendingApproval') && !hasDiagnostic)) {
    return null
  }

  async function doCancel() {
    await cancelRequest(r.id)
    setConfirmCancel(false)
  }

  function decline() {
    const note = window.prompt('Reason for declining (optional, shown to the requester):')
    if (note === null) return
    void actOnRequest(r.id, 'deny', { note: note || null })
  }

  return (
    <li className="request-actions-row">
      {hasDiagnostic && (
        <div className="request-diagnostic">
          <button className="retry-toggle" onClick={() => setShowDiagnostic(!showDiagnostic)}>
            {showDiagnostic ? 'Hide details' : 'Why is this stuck?'}
          </button>
          {showDiagnostic && <p>{r.diagnostic}</p>}
        </div>
      )}

      {isAdmin && isImportBlocked(r) && (
        <button className="primary" onClick={() => setFixOpen(!fixOpen)}>
          {fixOpen ? 'Close' : 'Fix import'}
        </button>
      )}
      {fixOpen && <FixImportPanel request={r} onClose={() => setFixOpen(false)} />}

      {isAdmin && r.status === 'PendingApproval' && (
        <span className="request-action-group">
          <button className="primary" disabled={busy} onClick={() => actOnRequest(r.id, 'approve')}>
            Approve
          </button>
          <button disabled={busy} onClick={decline}>
            Decline
          </button>
        </span>
      )}

      {canManage && confirmCancel ? (
        <span className="confirm-cancel">
          <span>{r.status === 'Denied' ? 'Remove this request?' : 'Cancel this request?'}</span>
          <button onClick={doCancel} disabled={cancellingId === r.id}>
            {cancellingId === r.id ? 'Working…' : 'Yes'}
          </button>
          <button onClick={() => setConfirmCancel(false)}>No</button>
        </span>
      ) : (
        canManage && (
          <>
            {(awaitingSearch || r.status === 'Failed' || upcoming) && (
              <button className="retry-toggle" disabled={busy} onClick={() => actOnRequest(r.id, 'search-again')}>
                {upcoming ? 'Check now' : 'Search again'}
              </button>
            )}
            {(awaitingSearch || r.status === 'Failed') && (
              <button
                className="retry-toggle"
                disabled={busy}
                title="Keep re-searching on a schedule instead of giving up when nothing is found"
                onClick={() => actOnRequest(r.id, 'wishlist', { enabled: !r.wishlisted })}
              >
                {r.wishlisted ? 'Stop trying' : 'Keep trying'}
              </button>
            )}
            {r.status === 'Failed' && (
              <button className="retry-toggle" onClick={() => setRetryOpen(!retryOpen)}>
                {retryOpen ? 'Close' : 'Retry with different search'}
              </button>
            )}
            <button className="retry-toggle" onClick={() => setConfirmCancel(true)}>
              {r.status === 'Denied' ? 'Remove' : 'Cancel request'}
            </button>
          </>
        )
      )}
      {retryOpen && <RetrySearch request={r} onDone={() => setRetryOpen(false)} />}
    </li>
  )
}
