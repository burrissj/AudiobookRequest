import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react'
import { api, ApiError } from '../../api/client'
import type { ImportCandidate, ImportFolder, ImportJob, ImportScan } from '../../api/types'
import { formatBytes } from '../../lib/format'
import { ImportQueue } from './ImportQueue'
import { ImportWizard } from './ImportWizard'

/**
 * Tidy up loose audiobooks: pick a folder, then work through the books in it one at a time -- identify each, and it joins
 * a conversion queue that turns it into a tagged .m4b, files it in the library under the naming pattern and tells Jellyfin
 * to rescan. Identifying carries on while earlier books convert.
 */
export function AdminImport() {
  const [folder, setFolder] = useState<ImportFolder | null>(null)
  const [folderText, setFolderText] = useState('')
  const [scan, setScan] = useState<ImportScan | null>(null)
  const [scanning, setScanning] = useState(false)
  const [savingFolder, setSavingFolder] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [activeId, setActiveId] = useState<string | null>(null)
  const [showDone, setShowDone] = useState(false)
  const [jobs, setJobs] = useState<ImportJob[]>([])
  // Which jobs had finished the last time we looked; null until the first look, so opening the page doesn't rescan.
  const seenDone = useRef<Set<string> | null>(null)

  const rescan = useCallback(async () => {
    setScanning(true)
    setError(null)
    try {
      setScan(await api.get<ImportScan>('/api/admin/import/scan'))
    } catch (err) {
      setScan(null)
      setError(err instanceof ApiError ? err.message : "Couldn't read that folder.")
    } finally {
      setScanning(false)
    }
  }, [])

  const applyJobs = useCallback(
    (list: ImportJob[]) => {
      setJobs(list)
      const done = new Set(list.filter((j) => j.status === 'Succeeded').map((j) => j.jobId))
      const before = seenDone.current
      seenDone.current = done
      // A book just finished: the folder list should show it as imported.
      if (before && [...done].some((id) => !before.has(id))) void rescan()
    },
    [rescan],
  )

  const refreshJobs = useCallback(async () => {
    try {
      applyJobs(await api.get<ImportJob[]>('/api/admin/import/jobs'))
    } catch {
      // The next poll tries again.
    }
  }, [applyJobs])

  const anyOpen = jobs.some(isOpen)

  // Pick up the queue as it stands (a book may have been converting when the page was left).
  useEffect(() => {
    let current = true
    api
      .get<ImportJob[]>('/api/admin/import/jobs')
      .then((list) => current && applyJobs(list))
      .catch(() => {})
    return () => {
      current = false
    }
  }, [applyJobs])

  // While anything is waiting or converting, keep the queue up to date.
  useEffect(() => {
    if (!anyOpen) return
    const id = window.setInterval(() => void refreshJobs(), 2000)
    return () => window.clearInterval(id)
  }, [anyOpen, refreshJobs])

  async function cancelJob(job: ImportJob) {
    try {
      await api.delete(`/api/admin/import/jobs/${job.jobId}`)
    } catch {
      // It finished while we were asking: the refresh shows how.
    }
    await refreshJobs()
  }

  async function clearFinished() {
    await api.delete('/api/admin/import/jobs')
    await refreshJobs()
  }

  function retryJob(job: ImportJob) {
    if (scan?.candidates.some((c) => c.id === job.candidateId)) {
      setActiveId(job.candidateId)
    } else {
      setError("That book isn't in the folder any more. Scan again.")
    }
  }

  // Open on the saved folder.
  useEffect(() => {
    let current = true
    api
      .get<ImportFolder>('/api/admin/import')
      .then((f) => {
        if (!current) return
        setFolder(f)
        setFolderText(f.folder ?? '')
        if (f.folder) {
          api
            .get<ImportScan>('/api/admin/import/scan')
            .then((s) => current && setScan(s))
            .catch((err) => current && setError(err instanceof ApiError ? err.message : "Couldn't read that folder."))
        }
      })
      .catch((err) => current && setError(err instanceof ApiError ? err.message : "Couldn't load the import settings."))
    return () => {
      current = false
    }
  }, [])

  async function saveFolder(e: FormEvent) {
    e.preventDefault()
    setSavingFolder(true)
    setError(null)
    try {
      const saved = await api.put<ImportFolder>('/api/admin/import/folder', { path: folderText })
      setFolder(saved)
      setFolderText(saved.folder ?? '')
      setActiveId(null)
      if (saved.folder) await rescan()
      else setScan(null)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't save that folder.")
    } finally {
      setSavingFolder(false)
    }
  }

  const all = scan?.candidates ?? []
  const todo = all.filter((c) => !c.imported)
  const openJobs = new Map(jobs.filter(isOpen).map((j) => [j.candidateId, j]))
  // Books still to identify: not imported, and not already in the queue.
  const waiting = todo.filter((c) => !openJobs.has(c.id))
  const inQueue = todo.length - waiting.length
  const visible = showDone ? all : todo
  const active = all.find((c) => c.id === activeId) ?? null
  const nextWaiting = active ? waiting.find((c) => c.id !== active.id && todo.indexOf(c) > todo.findIndex((t) => t.id === active.id)) ?? null : null

  // The book is in the queue: carry straight on with the next one, or back to the list when that was the last.
  async function queued() {
    const next = nextWaiting
    await refreshJobs()
    setActiveId(next?.id ?? null)
  }

  function skip() {
    setActiveId(nextWaiting?.id ?? null)
  }

  return (
    <div className="admin-import">
      <p className="subtitle">
        Gather audiobooks you got from elsewhere: each is converted to a single .m4b with proper tags and cover art, filed in your library the same way as
        everything else, and added to Jellyfin. You identify them one at a time.
      </p>

      {folder?.problem && <p className="diagnostic-text">{folder.problem}</p>}
      {error && <p className="error">{error}</p>}

      <ImportQueue jobs={jobs} onCancel={(j) => void cancelJob(j)} onRetry={retryJob} onClear={() => void clearFinished()} />

      {!active && (
        <form className="import-folder" onSubmit={saveFolder}>
          <label>
            Folder to import from
            <input
              value={folderText}
              onChange={(e) => setFolderText(e.target.value)}
              placeholder="D:\Audiobooks\To sort"
              autoCapitalize="none"
              autoCorrect="off"
              spellCheck={false}
            />
          </label>
          <small className="import-note">The path as this server sees it. It must be a separate folder from your library.</small>
          <div className="import-actions">
            <button type="submit" className="primary" disabled={savingFolder || folderText.trim() === ''}>
              {savingFolder ? 'Checking…' : folder?.folder ? 'Save and scan' : 'Scan this folder'}
            </button>
            {folder?.folder && (
              <button type="button" onClick={() => void rescan()} disabled={scanning}>
                {scanning ? 'Scanning…' : 'Scan again'}
              </button>
            )}
          </div>
        </form>
      )}

      {!active && scan && (
        <>
          {all.length === 0 && <p className="subtitle">No audiobooks found in that folder. (MP3, M4B, M4A, FLAC, AAC, OGG, OPUS, WMA and WAV files are picked up.)</p>}
          {all.length > 0 && (
            <>
              <p className="subtitle">
                {waiting.length} book{waiting.length === 1 ? '' : 's'} to sort
                {inQueue > 0 ? `, ${inQueue} in the queue` : ''}
                {all.length > todo.length ? `, ${all.length - todo.length} already imported` : ''}.
              </p>
              {waiting.length > 0 && (
                <button className="primary" onClick={() => setActiveId(waiting[0].id)}>
                  Start with the first one
                </button>
              )}
              {all.length > todo.length && (
                <label className="checkbox-label">
                  <input type="checkbox" checked={showDone} onChange={(e) => setShowDone(e.target.checked)} />
                  Show the ones already imported
                </label>
              )}
              <ul className="import-list">
                {visible.map((c) => (
                  <CandidateRow key={c.id} candidate={c} job={openJobs.get(c.id)} onOpen={() => setActiveId(c.id)} />
                ))}
              </ul>
            </>
          )}
        </>
      )}

      {active && (
        <ImportWizard
          key={active.id}
          candidate={active}
          hasNext={nextWaiting !== null}
          onSkip={skip}
          onQueued={() => void queued()}
          onBack={() => setActiveId(null)}
        />
      )}
    </div>
  )
}

const isOpen = (j: ImportJob) => j.status === 'Queued' || j.status === 'Running'

function CandidateRow({ candidate, job, onOpen }: { candidate: ImportCandidate; job: ImportJob | undefined; onOpen: () => void }) {
  return (
    <li>
      <button className="import-row" onClick={onOpen} disabled={!!job}>
        <span className="import-row-text">
          <strong>{candidate.name}</strong>
          <small>
            {candidate.relativePath !== candidate.name ? `${candidate.relativePath} · ` : ''}
            {candidate.fileCount} file{candidate.fileCount === 1 ? '' : 's'} · {formatBytes(candidate.totalBytes)}
          </small>
        </span>
        {candidate.imported && <span className="badge import-done">Imported</span>}
        {job && <span className="badge">{job.status === 'Running' ? 'Converting' : 'In the queue'}</span>}
      </button>
    </li>
  )
}
