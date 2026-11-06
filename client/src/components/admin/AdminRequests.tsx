import { useMemo, useState } from 'react'
import type { BookRequestDto, BookRequestStatus } from '../../api/types'
import { useRequests } from '../../state/useRequests'
import { FixImportPanel } from '../FixImportPanel'
import { isImportBlocked } from '../../lib/importBlocked'
import { StatusBadge } from '../StatusBadge'
import { formatReleaseDay } from '../../lib/release'

const STATUS_ORDER: BookRequestStatus[] = [
  'PendingApproval',
  'Failed',
  'Requested',
  'Searching',
  'Upcoming',
  'Snatched',
  'Downloading',
  'Processing',
  'Ready',
  'Denied',
]

const SEARCHABLE: BookRequestStatus[] = ['Failed', 'Requested', 'Searching', 'Upcoming']

function timeAgo(iso: string): string {
  const minutes = Math.max(0, Math.round((Date.now() - new Date(iso).getTime()) / 60000))
  if (minutes < 60) return `${minutes}m ago`
  if (minutes < 60 * 24) return `${Math.round(minutes / 60)}h ago`
  return `${Math.round(minutes / 60 / 24)}d ago`
}

function hasProblem(r: BookRequestDto): boolean {
  return !!r.diagnostic && r.status !== 'Ready'
}

/** Every request from every user, filterable, with bulk approve / decline / search again / cancel. */
export function AdminRequests() {
  const { requests, actOnRequest, cancelRequest, error } = useRequests()
  const [status, setStatus] = useState<'all' | 'stuck' | BookRequestStatus>('all')
  const [user, setUser] = useState('all')
  const [text, setText] = useState('')
  const [selected, setSelected] = useState<Set<number>>(new Set())
  const [working, setWorking] = useState(false)
  const [fixingId, setFixingId] = useState<number | null>(null)

  const users = useMemo(() => [...new Set(requests.map((r) => r.requestedByUsername))].sort(), [requests])
  const counts = useMemo(() => {
    const c: Partial<Record<BookRequestStatus, number>> = {}
    requests.forEach((r) => (c[r.status] = (c[r.status] ?? 0) + 1))
    return c
  }, [requests])

  const visible = requests.filter((r) => {
    if (status === 'stuck') {
      if (!hasProblem(r)) return false
    } else if (status !== 'all' && r.status !== status) {
      return false
    }
    if (user !== 'all' && r.requestedByUsername !== user) return false
    if (text.trim() && !`${r.title} ${r.author}`.toLowerCase().includes(text.trim().toLowerCase())) return false
    return true
  })

  const selectedRequests = visible.filter((r) => selected.has(r.id))
  const countWhere = (pred: (r: BookRequestDto) => boolean) => selectedRequests.filter(pred).length

  function toggle(id: number) {
    setSelected((prev) => {
      const next = new Set(prev)
      if (!next.delete(id)) next.add(id)
      return next
    })
  }

  function toggleAllVisible() {
    setSelected(selectedRequests.length === visible.length ? new Set() : new Set(visible.map((r) => r.id)))
  }

  async function bulk(kind: 'approve' | 'deny' | 'search-again' | 'cancel') {
    let targets = selectedRequests
    let body: unknown
    if (kind === 'approve') targets = targets.filter((r) => r.status === 'PendingApproval')
    if (kind === 'deny') {
      targets = targets.filter((r) => r.status === 'PendingApproval')
      const note = window.prompt(`Reason for declining ${targets.length} request(s) (optional):`)
      if (note === null) return
      body = { note: note || null }
    }
    if (kind === 'search-again') targets = targets.filter((r) => SEARCHABLE.includes(r.status))
    if (targets.length === 0) return
    if (kind === 'cancel' && !window.confirm(`Cancel ${targets.length} request(s)? Books are removed from Chaptarr; files on disk are kept.`)) return

    setWorking(true)
    for (const r of targets) {
      if (kind === 'cancel') await cancelRequest(r.id)
      else await actOnRequest(r.id, kind, body)
    }
    setSelected(new Set())
    setWorking(false)
  }

  return (
    <div>
      <div className="chip-row">
        <button className={`chip${status === 'all' ? ' active' : ''}`} onClick={() => setStatus('all')}>
          All ({requests.length})
        </button>
        <button className={`chip${status === 'stuck' ? ' active' : ''}`} onClick={() => setStatus('stuck')}>
          Needs attention ({requests.filter(hasProblem).length})
        </button>
        {STATUS_ORDER.filter((s) => counts[s]).map((s) => (
          <button key={s} className={`chip${status === s ? ' active' : ''}`} onClick={() => setStatus(s)}>
            {s === 'PendingApproval' ? 'Pending' : s} ({counts[s]})
          </button>
        ))}
      </div>

      <div className="filter-row">
        <input value={text} onChange={(e) => setText(e.target.value)} placeholder="Filter by title or author" />
        <select value={user} onChange={(e) => setUser(e.target.value)}>
          <option value="all">All users</option>
          {users.map((u) => (
            <option key={u} value={u}>
              {u}
            </option>
          ))}
        </select>
      </div>

      {error && <p className="error">{error}</p>}

      <div className="bulk-bar">
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={visible.length > 0 && selectedRequests.length === visible.length}
            onChange={toggleAllVisible}
          />
          {selectedRequests.length > 0 ? `${selectedRequests.length} selected` : 'Select all'}
        </label>
        {selectedRequests.length > 0 && (
          <span className="request-action-group">
            {countWhere((r) => r.status === 'PendingApproval') > 0 && (
              <>
                <button className="primary" disabled={working} onClick={() => bulk('approve')}>
                  Approve
                </button>
                <button disabled={working} onClick={() => bulk('deny')}>
                  Decline
                </button>
              </>
            )}
            {countWhere((r) => SEARCHABLE.includes(r.status)) > 0 && (
              <button disabled={working} onClick={() => bulk('search-again')}>
                Search again
              </button>
            )}
            <button disabled={working} onClick={() => bulk('cancel')}>
              Cancel
            </button>
          </span>
        )}
      </div>

      {visible.length === 0 && <p className="subtitle">Nothing matches.</p>}
      <ul className="book-list">
        {visible.map((r) => (
          <li key={r.id} className="admin-request-row">
            <input type="checkbox" checked={selected.has(r.id)} onChange={() => toggle(r.id)} aria-label={`Select ${r.title}`} />
            <div className="book-info">
              <strong>{r.title}</strong>
              <span>
                {r.author} · {r.requestedByUsername} · {timeAgo(r.requestedAt)}
              </span>
              {r.failureReason && <span className="book-caption">{r.failureReason}</span>}
              {hasProblem(r) && <span className="book-caption diagnostic-text">{r.diagnostic}</span>}
              {r.status === 'Upcoming' && r.releaseDate && <span className="book-caption">Out {formatReleaseDay(r.releaseDate)}</span>}
              {r.wishlisted && r.status !== 'Upcoming' && <span className="book-caption">Wishlisted</span>}
              {isImportBlocked(r) && (
                <button className="retry-toggle fix-import-link" onClick={() => setFixingId(fixingId === r.id ? null : r.id)}>
                  {fixingId === r.id ? 'Close' : 'Fix import…'}
                </button>
              )}
              {fixingId === r.id && <FixImportPanel request={r} onClose={() => setFixingId(null)} />}
            </div>
            <StatusBadge status={r.status} />
          </li>
        ))}
      </ul>
    </div>
  )
}
