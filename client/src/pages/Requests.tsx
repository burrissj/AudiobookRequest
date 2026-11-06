import { useState } from 'react'
import { useAuth } from '../auth/useAuth'
import { AppHeader } from '../components/AppHeader'
import { CoverCard } from '../components/CoverCard'
import { GetAppLinks } from '../components/JellyfinLinks'
import { RequestSheet } from '../components/RequestSheet'
import { coverStatus, groupRequests } from '../lib/requestGroups'
import { useRequests } from '../state/useRequests'

/** How many covers a section shows before "Show more": six rows of three. */
const SHOWN = 18

export function Requests() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const { requests, refreshing, refreshRequests, error } = useRequests()
  const [scope, setScope] = useState<'mine' | 'all'>('mine')
  const [openId, setOpenId] = useState<number | null>(null)
  const [expanded, setExpanded] = useState<Set<string>>(new Set())

  const visible = scope === 'mine' ? requests.filter((r) => r.requestedByUsername === user?.username) : requests
  const groups = groupRequests(visible)
  // Looked up by id each time, so the sheet follows the request as it changes and closes itself once it is gone.
  const open = openId === null ? null : (visible.find((r) => r.id === openId) ?? null)

  function captionFor(r: (typeof requests)[number]): string {
    const mine = r.requestedByUsername === user?.username
    return [scope === 'all' && !mine ? `Requested by ${r.requestedByUsername}` : null, r.failureReason, r.wishlisted ? 'Wishlisted: will keep trying' : null]
      .filter(Boolean)
      .join(' · ')
  }

  return (
    <div className="page">
      <AppHeader title="Requests" />
      <div className="requests-toolbar">
        <div className="segmented">
          <button className={scope === 'mine' ? 'active' : ''} onClick={() => setScope('mine')}>
            Mine
          </button>
          <button className={scope === 'all' ? 'active' : ''} onClick={() => setScope('all')}>
            Everyone
          </button>
        </div>
        <button onClick={refreshRequests} disabled={refreshing}>
          {refreshing ? 'Refreshing…' : 'Refresh'}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      <GetAppLinks />
      {visible.length === 0 && (
        <p className="subtitle">
          {scope === 'mine' ? 'No requests yet — search the Browse tab to get started.' : 'Nobody has requested anything yet.'}
        </p>
      )}

      {groups.map((group) => {
        const all = expanded.has(group.key)
        const shown = all ? group.items : group.items.slice(0, SHOWN)
        return (
          <section key={group.key} className="category-section request-group">
            <div className="category-heading">
              <h2>{group.title}</h2>
              <span className="request-group-count">{group.items.length}</span>
            </div>
            <div className="cover-grid">
              {shown.map((r) => {
                const status = coverStatus(r)
                const mine = r.requestedByUsername === user?.username
                return (
                  <CoverCard
                    key={r.id}
                    coverUrl={r.coverUrl}
                    title={r.title}
                    author={r.author}
                    showText
                    hoursPlacement="none"
                    badge={status.text}
                    badgeTone={status.tone}
                    onClick={() => setOpenId(r.id)}
                  >
                    {r.status === 'Downloading' && r.downloadProgressPercent != null && (
                      <span className="cover-progress" aria-hidden="true">
                        <span style={{ width: `${Math.min(1, r.downloadProgressPercent) * 100}%` }} />
                      </span>
                    )}
                    {scope === 'all' && !mine && <span className="cover-reason">{r.requestedByUsername}</span>}
                  </CoverCard>
                )
              })}
            </div>
            {!all && group.items.length > SHOWN && (
              <button className="listen-more" onClick={() => setExpanded((prev) => new Set(prev).add(group.key))}>
                Show {group.items.length - SHOWN} more
              </button>
            )}
          </section>
        )
      })}

      {open && (
        <RequestSheet
          request={open}
          caption={captionFor(open)}
          canManage={open.requestedByUsername === user?.username || isAdmin}
          isAdmin={isAdmin}
          onClose={() => setOpenId(null)}
        />
      )}
    </div>
  )
}
