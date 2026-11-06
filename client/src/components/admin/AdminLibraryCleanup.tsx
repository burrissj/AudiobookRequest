import { useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { RemoveStaleResult, StaleScan } from '../../api/types'
import { useRequests } from '../../state/useRequests'

/**
 * Finished requests whose book was deleted from Jellyfin. Checks Jellyfin live, lists what's gone, and removes
 * the chosen records (never the audio files), so they stop cluttering the Requests list and can be requested again.
 */
export function AdminLibraryCleanup() {
  const { refreshRequests } = useRequests()
  const [scan, setScan] = useState<StaleScan | null>(null)
  const [chosen, setChosen] = useState<Set<number>>(new Set())
  const [busy, setBusy] = useState<'checking' | 'removing' | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function check() {
    setBusy('checking')
    setError(null)
    setMessage(null)
    try {
      const result = await api.get<StaleScan>('/api/admin/library/stale')
      setScan(result)
      setChosen(new Set(result.missing.map((m) => m.id)))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't check the library.")
    } finally {
      setBusy(null)
    }
  }

  async function remove() {
    if (chosen.size === 0) return
    if (!window.confirm(`Remove ${chosen.size} finished request(s) from the list? The audiobook files are not touched.`)) return
    setBusy('removing')
    setError(null)
    try {
      const result = await api.post<RemoveStaleResult>('/api/admin/library/stale/remove', { requestIds: [...chosen] })
      setMessage(
        `Removed ${result.removed} request(s).` +
          (result.stillInJellyfin > 0 ? ` ${result.stillInJellyfin} were left because Jellyfin has them after all.` : ''),
      )
      await refreshRequests()
      setScan(await api.get<StaleScan>('/api/admin/library/stale'))
      setChosen(new Set())
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't remove them.")
    } finally {
      setBusy(null)
    }
  }

  function toggle(id: number) {
    setChosen((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  return (
    <div className="health-item cleanup-panel">
      <p>Books deleted from Jellyfin still show as "Ready to listen" in the Requests list. Find them and clear them out.</p>
      <button onClick={() => void check()} disabled={busy !== null}>
        {busy === 'checking' ? 'Checking Jellyfin…' : scan ? 'Check again' : 'Find books no longer in Jellyfin'}
      </button>

      {error && <p className="error">{error}</p>}
      {message && <p className="success">{message}</p>}

      {scan?.blocked && <p className="diagnostic-text">{scan.blocked}</p>}

      {scan && !scan.blocked && scan.missing.length === 0 && (
        <p className="success">
          Everything finished is still in Jellyfin ({scan.finishedChecked} checked against {scan.jellyfinBooks} books).
        </p>
      )}

      {scan && !scan.blocked && scan.missing.length > 0 && (
        <>
          <p className="subtitle">
            {scan.missing.length} of {scan.finishedChecked} finished request(s) are no longer in Jellyfin:
          </p>
          <ul className="cleanup-list">
            {scan.missing.map((m) => (
              <li key={m.id}>
                <label className="checkbox-label">
                  <input type="checkbox" checked={chosen.has(m.id)} onChange={() => toggle(m.id)} />
                  <span>
                    <strong>{m.title}</strong>
                    <small>
                      {m.author} · requested by {m.requestedBy}
                    </small>
                  </span>
                </label>
              </li>
            ))}
          </ul>
          <button className="primary" onClick={() => void remove()} disabled={busy !== null || chosen.size === 0}>
            {busy === 'removing' ? 'Removing…' : `Remove ${chosen.size} from the list`}
          </button>
        </>
      )}
    </div>
  )
}
