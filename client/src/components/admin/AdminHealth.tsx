import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../../api/client'
import { Link } from 'react-router-dom'
import { AdminLibraryCleanup } from './AdminLibraryCleanup'
import type { BackupInfo, BackupList, ChecklistItem, HealthReport, LibraryStatus, VersionInfo } from '../../api/types'

const STATUS_LABEL = { ok: 'OK', warn: 'Warning', fail: 'Problem', skip: 'Not set up', info: 'Note' } as const
const STATUS_BADGE = { ok: 'badge-ok', warn: 'badge-pending', fail: 'badge-fail', skip: '', info: '' } as const

function formatSize(bytes: number): string {
  return bytes >= 1e6 ? `${(bytes / 1e6).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1e3))} KB`
}

const REQUIRED_KEYS = ['chaptarr', 'jellyfin', 'ffmpeg', 'folders', 'files', 'library']
const CHECK_MARK = { done: '✓', todo: '!', optional: '○' } as const

/** The "is everything set up?" list. Required items must be done for the app to work; optional ones make it nicer. */
function SetupChecklistView({ items }: { items: ChecklistItem[] }) {
  const requiredItems = items.filter((i) => REQUIRED_KEYS.includes(i.key))
  const optionalItems = items.filter((i) => !REQUIRED_KEYS.includes(i.key))
  const requiredDone = requiredItems.filter((i) => i.status === 'done').length
  const optionalDone = optionalItems.filter((i) => i.status === 'done').length
  const allRequiredDone = requiredDone === requiredItems.length

  return (
    <div className={`setup-checklist${allRequiredDone ? ' setup-complete' : ''}`}>
      <div className="health-head">
        <strong>{allRequiredDone ? 'Setup complete' : 'Finish setting up'}</strong>
        <span className={`badge ${allRequiredDone ? 'badge-ok' : 'badge-pending'}`}>
          {requiredDone} of {requiredItems.length} required
        </span>
      </div>
      <ul className="checklist">
        {[...requiredItems, ...optionalItems].map((item) => (
          <li key={item.key} className={`checklist-item checklist-${item.status}`}>
            <span className="checklist-mark" aria-hidden="true">
              {CHECK_MARK[item.status]}
            </span>
            <div>
              <strong>{item.title}</strong>
              {!REQUIRED_KEYS.includes(item.key) && <span className="subtitle checklist-tag"> optional</span>}
              <p>{item.detail}</p>
              {item.action && (
                <p className="checklist-action">
                  {item.action.startsWith('Settings') ? <Link to="/settings">{item.action}</Link> : item.action}
                </p>
              )}
            </div>
          </li>
        ))}
      </ul>
      <p className="subtitle checklist-foot">
        {optionalDone} of {optionalItems.length} optional extras on.
      </p>
    </div>
  )
}


function when(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : 'never'
}

/** Is everything wired up? Reachability, folders, disk space, background jobs, and where requests are piling up. */
type HealthResult = { report: HealthReport } | { error: string }

async function fetchHealth(): Promise<HealthResult> {
  try {
    return { report: await api.get<HealthReport>('/api/admin/health') }
  } catch (err) {
    return { error: err instanceof ApiError ? err.message : 'Failed to run health checks' }
  }
}

export function AdminHealth() {
  const [report, setReport] = useState<HealthReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [rescanning, setRescanning] = useState(false)
  const [backups, setBackups] = useState<BackupList | null>(null)
  const [backingUp, setBackingUp] = useState(false)
  const [version, setVersion] = useState<VersionInfo | null>(null)

  const showChecks = useCallback((result: HealthResult) => {
    if ('report' in result) {
      setReport(result.report)
      setError(null)
    } else {
      setError(result.error)
    }
    setLoading(false)
  }, [])

  // The Refresh button: show the spinner again, then run them.
  const load = useCallback(async () => {
    setLoading(true)
    showChecks(await fetchHealth())
  }, [showChecks])

  // The first run happens as soon as the tab opens (the page starts out in its loading state).
  useEffect(() => {
    let current = true
    fetchHealth().then((result) => current && showChecks(result))
    api.get<BackupList>('/api/admin/backups').then(setBackups).catch(console.error)
    api.get<VersionInfo>('/api/admin/version').then(setVersion).catch(console.error)
    return () => {
      current = false
    }
  }, [showChecks])

  async function backUpNow() {
    setBackingUp(true)
    setError(null)
    try {
      await api.post<BackupInfo>('/api/admin/backups')
      setBackups(await api.get<BackupList>('/api/admin/backups'))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'The backup failed')
    } finally {
      setBackingUp(false)
    }
  }

  async function rescan() {
    setRescanning(true)
    setError(null)
    try {
      const library = await api.post<LibraryStatus>('/api/admin/library/rescan')
      setReport((r) => (r ? { ...r, library } : r))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Rescan failed')
    } finally {
      setRescanning(false)
    }
  }

  return (
    <div>
      <div className="requests-toolbar">
        <button onClick={load} disabled={loading}>
          {loading ? 'Checking…' : 'Run checks again'}
        </button>
      </div>
      {error && <p className="error">{error}</p>}

      {report && (
        <>
          <SetupChecklistView items={report.checklist} />

          <ul className="health-list">
            {report.checks.map((c) => (
              <li key={c.name} className={`health-item health-${c.status}`}>
                <div className="health-head">
                  <strong>{c.name}</strong>
                  <span className={`badge ${STATUS_BADGE[c.status]}`}>{STATUS_LABEL[c.status]}</span>
                </div>
                <p>{c.detail}</p>
              </li>
            ))}
          </ul>

          <h3>Library</h3>
          <div className="health-item">
            <p>
              {report.library.bookCount} audiobook(s) known from Jellyfin. Last scan: {when(report.library.lastRefreshedAt)}.
            </p>
            <p className="subtitle">
              Used to stop anyone re-requesting books you already own. Refreshes automatically every 6 hours.
            </p>
            <button onClick={rescan} disabled={rescanning || !report.library.jellyfinConfigured}>
              {rescanning ? 'Scanning…' : 'Rescan library now'}
            </button>
          </div>

          <h3>Clean up the Requests list</h3>
          <AdminLibraryCleanup />

          <h3>Requests</h3>
          <div className="health-item">
            <p>
              {Object.entries(report.requestCounts)
                .map(([status, n]) => `${status}: ${n}`)
                .join(' · ') || 'No requests yet.'}
            </p>
            {report.stuckRequests > 0 && (
              <p className="diagnostic-text">
                {report.stuckRequests} request(s) have a problem reported, see "Needs attention" on the Requests tab.
              </p>
            )}
          </div>

          <h3>Backups</h3>
          <div className="health-item">
            {backups && backups.retentionCount === 0 && (
              <p>Automatic backups are off. Turn them on under Settings → Backups.</p>
            )}
            {backups && backups.backups.length === 0 && backups.retentionCount > 0 && (
              <p>No backup yet. The first one is taken a few minutes after the service starts.</p>
            )}
            {backups && backups.backups.length > 0 && (
              <ul className="backup-list">
                {backups.backups.map((b) => (
                  <li key={b.fileName}>
                    <a href={`/api/admin/backups/${encodeURIComponent(b.fileName)}`} download>
                      {b.fileName}
                    </a>
                    <span className="subtitle backup-meta">
                      {formatSize(b.sizeBytes)} · {when(b.createdAt)}
                    </span>
                  </li>
                ))}
              </ul>
            )}
            <button onClick={backUpNow} disabled={backingUp}>
              {backingUp ? 'Backing up…' : 'Back up now'}
            </button>
            <p className="subtitle backup-restore">
              To restore: stop the AudiobookRequest service, replace <code>data\app.db</code> in the install folder (usually
              C:\ProgramData\AudiobookRequest) with the backup renamed to <code>app.db</code>, then start the service. Backups
              contain your API keys, so keep downloads private.
            </p>
          </div>

          <h3>Background jobs</h3>
          <ul className="health-list">
            {report.jobs.length === 0 && (
              <li className="health-item">
                <p>No job has run yet.</p>
              </li>
            )}
            {report.jobs.map((j) => (
              <li key={j.name} className={`health-item health-${j.lastSucceeded === false ? 'fail' : 'ok'}`}>
                <div className="health-head">
                  <strong>{j.name}</strong>
                  <span className={`badge ${j.lastSucceeded === false ? 'badge-fail' : 'badge-ok'}`}>
                    {j.lastSucceeded === false ? 'Failing' : 'OK'}
                  </span>
                </div>
                <p>Last run: {when(j.lastRunAt)}</p>
                {j.lastSucceeded === false && (
                  <p className="diagnostic-text">
                    {j.lastError} (last success: {when(j.lastSuccessAt)})
                  </p>
                )}
              </li>
            ))}
          </ul>

          {version && (
            <>
              <h3>About</h3>
              <div className="health-item">
                <p>
                  Audiobook Request <strong>{version.version}</strong>
                </p>
                {version.changelog.map((entry) => (
                  <div key={entry.version} className="changelog-entry">
                    <strong>
                      {entry.version} <span className="subtitle">({entry.date})</span>
                    </strong>
                    <ul>
                      {entry.items.map((item) => (
                        <li key={item}>{item}</li>
                      ))}
                    </ul>
                  </div>
                ))}
              </div>
            </>
          )}
        </>
      )}
    </div>
  )
}
