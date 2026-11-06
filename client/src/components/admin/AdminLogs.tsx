import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../../api/client'
import type { LogView } from '../../api/types'

/** Tail of the server's log files, so problems can be diagnosed without remoting into the machine. */
export function AdminLogs() {
  const [view, setView] = useState<LogView | null>(null)
  const [level, setLevel] = useState('warning')
  const [search, setSearch] = useState('')
  const [file, setFile] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    setError(null)
    try {
      const params = new URLSearchParams({ level, limit: '200' })
      if (search.trim()) params.set('search', search.trim())
      if (file) params.set('file', file)
      setView(await api.get<LogView>(`/api/admin/logs?${params}`))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Failed to read logs')
    } finally {
      setLoading(false)
    }
  }, [level, search, file])

  useEffect(() => {
    const handle = setTimeout(() => void load(), 250)
    return () => clearTimeout(handle)
  }, [load])

  return (
    <div>
      <div className="filter-row">
        <select value={level} onChange={(e) => setLevel(e.target.value)}>
          <option value="warning">Warnings &amp; errors</option>
          <option value="error">Errors only</option>
          <option value="all">Everything</option>
        </select>
        {view && view.files.length > 1 && (
          <select value={file || view.file} onChange={(e) => setFile(e.target.value)}>
            {view.files.map((f) => (
              <option key={f} value={f}>
                {f}
              </option>
            ))}
          </select>
        )}
      </div>
      <div className="filter-row">
        <input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search the log" />
        <button onClick={load} disabled={loading}>
          {loading ? 'Loading…' : 'Refresh'}
        </button>
      </div>
      {error && <p className="error">{error}</p>}
      {view && view.entries.length === 0 && !loading && <p className="subtitle">No matching log entries.</p>}
      {view?.truncated && <p className="subtitle">Showing the end of a large file.</p>}
      <ul className="log-list">
        {view?.entries.map((e, i) => (
          <li key={i} className={`log-entry log-${e.level}`}>
            {e.text}
          </li>
        ))}
      </ul>
    </div>
  )
}
