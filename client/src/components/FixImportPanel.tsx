import { useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'
import type { BookRequestDto, ImportFixPreview, ImportFixResult } from '../api/types'

function formatSize(bytes: number): string {
  return bytes >= 1e9 ? `${(bytes / 1e9).toFixed(1)} GB` : `${Math.round(bytes / 1e6)} MB`
}

interface FixImportPanelProps {
  request: BookRequestDto
  onClose: () => void
}

/**
 * Admin confirm step for "Fix import": shows what Chaptarr is holding and which book it guessed, then
 * tells Chaptarr to import those files as the requested book instead. Nothing happens until the button is pressed.
 */
export function FixImportPanel({ request, onClose }: FixImportPanelProps) {
  const [preview, setPreview] = useState<ImportFixPreview | null>(null)
  const [result, setResult] = useState<ImportFixResult | null>(null)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [working, setWorking] = useState(false)

  useEffect(() => {
    api
      .get<ImportFixPreview>(`/api/books/requests/${request.id}/fix-import`)
      .then(setPreview)
      .catch((err) => setLoadError(err instanceof ApiError ? err.message : 'Could not check Chaptarr'))
  }, [request.id])

  async function runFix() {
    setWorking(true)
    try {
      setResult(await api.post<ImportFixResult>(`/api/books/requests/${request.id}/fix-import`))
    } catch (err) {
      setResult({ outcome: 'failed', message: err instanceof ApiError ? err.message : 'The import call failed' })
    } finally {
      setWorking(false)
    }
  }

  return (
    <div className="fix-import-panel">
      {!preview && !loadError && <p className="subtitle">Asking Chaptarr what it's holding…</p>}
      {loadError && <p className="error">{loadError}</p>}

      {preview && !preview.canFix && <p className="subtitle">{preview.reason ?? "There's nothing to import."}</p>}

      {preview?.canFix && !result && (
        <>
          <p>
            Chaptarr finished downloading this but won't import it. Import the file{preview.files.length === 1 ? '' : 's'} below as{' '}
            <strong>{preview.targetTitle}</strong>?
          </p>
          <ul>
            {preview.files.map((f) => (
              <li key={f.fileName}>
                <span className="fix-import-file">{f.fileName}</span> ({formatSize(f.sizeBytes)})
                {f.matchedTitle && <span className="fix-import-note"> — Chaptarr matched it to "{f.matchedTitle}"</span>}
              </li>
            ))}
          </ul>
          {preview.autoFixNote && (
            <p className="subtitle">
              {preview.autoFixable ? 'Looks safe: ' : 'Not automatic: '}
              {preview.autoFixNote}.
            </p>
          )}
          <p className="subtitle">Chaptarr copies or moves the files according to its own media-management settings.</p>
          <div className="request-action-group">
            <button className="primary" disabled={working} onClick={runFix}>
              {working ? 'Importing…' : 'Import as this book'}
            </button>
            <button disabled={working} onClick={onClose}>
              Cancel
            </button>
          </div>
        </>
      )}

      {result && (
        <>
          <p className={result.outcome === 'failed' ? 'error' : 'success'}>{result.message}</p>
          <button onClick={onClose}>Close</button>
        </>
      )}

      {(!preview || !preview.canFix) && !result && (
        <button onClick={onClose} style={{ marginTop: 8 }}>
          Close
        </button>
      )}
    </div>
  )
}
