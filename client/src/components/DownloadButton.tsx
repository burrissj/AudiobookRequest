import { cancelDownload, enqueueDownload, removeSaved, useOffline } from '../offline/downloadManager'
import { CheckCircleIcon, DownloadIcon } from './PlayerIcons'

interface DownloadButtonProps {
  itemId: string
  title: string
  /** False for books Jellyfin has to convert (they can't be saved as-is). */
  canDownload?: boolean
}

/** Save a book on this device for listening with no signal -- or see its progress, or remove the saved copy. */
export function DownloadButton({ itemId, title, canDownload = true }: DownloadButtonProps) {
  const offline = useOffline()
  if (!offline.supported) return null

  const saved = offline.saved.find((m) => m.id === itemId)
  const active = offline.active.find((a) => a.id === itemId)

  if (active) {
    const pct = active.totalBytes > 0 ? Math.floor((active.doneBytes / active.totalBytes) * 100) : 0
    if (active.state === 'error') {
      return (
        <span className="download-wrap">
          <button className="download-btn download-failed" onClick={() => enqueueDownload(itemId, title)} aria-label={`Retry downloading ${title}`}>
            Retry
          </button>
          {active.message && <span className="download-error">{active.message}</span>}
        </span>
      )
    }
    return (
      <button
        className="download-btn download-active"
        onClick={() => cancelDownload(itemId)}
        aria-label={`${active.state === 'queued' ? 'Queued' : `Downloading ${pct}%`}. Tap to cancel.`}
      >
        {active.state === 'queued' ? 'Queued' : `${pct}%`}
      </button>
    )
  }

  if (saved) {
    return (
      <button
        className="download-btn download-saved"
        onClick={() => {
          if (window.confirm(`Remove the downloaded copy of "${title}" from this device?`)) void removeSaved(itemId)
        }}
        aria-label={`${title} is saved for offline listening. Tap to remove it from this device.`}
      >
        <CheckCircleIcon />
      </button>
    )
  }

  if (!canDownload) return null

  if (offline.unavailable.includes(itemId)) {
    return <small className="download-na">Can’t be saved (converted for playback)</small>
  }

  return (
    <button className="download-btn" onClick={() => enqueueDownload(itemId, title)} aria-label={`Download ${title} for offline listening`}>
      <DownloadIcon />
    </button>
  )
}
