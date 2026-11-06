import { useEffect, useState } from 'react'
import type { ConnectionProblemInfo } from '../api/connection'
import { useOffline } from '../offline/downloadManager'
import { PlayerProvider } from '../state/PlayerContext'
import type { ListenItem } from '../api/types'
import { ListenBookSheet } from './ListenBookSheet'
import { PlayerSheet } from './PlayerSheet'
import { SavedBooks } from './SavedBooks'

interface ConnectionProblemProps {
  problem: ConnectionProblemInfo
  retrying: boolean
  onRetry: () => void
}

/**
 * Shown instead of the app when the server can't be reached at all. It retries by itself every few seconds
 * and the moment the device comes back online, so people on a VPN that connects a little late just see it fix itself.
 * If books are saved on the device, it also offers to play those -- they need no server.
 */
export function ConnectionProblem({ problem, retrying, onRetry }: ConnectionProblemProps) {
  const offline = useOffline()
  const [listeningOffline, setListeningOffline] = useState(false)

  // Not while listening to a saved book: reconnecting swaps the whole app in, which would cut the audio off.
  useEffect(() => {
    if (listeningOffline) return
    const id = window.setInterval(onRetry, 5000)
    const onBack = () => onRetry()
    window.addEventListener('online', onBack)
    document.addEventListener('visibilitychange', onBack)
    return () => {
      window.clearInterval(id)
      window.removeEventListener('online', onBack)
      document.removeEventListener('visibilitychange', onBack)
    }
  }, [onRetry, listeningOffline])

  if (listeningOffline) {
    return (
      <PlayerProvider>
        <div className="page">
          <h1>Saved books</h1>
          <p className="subtitle">The server can't be reached, so only books saved on this device are available.</p>
          <SavedOffline />
          <button
            className="primary"
            onClick={() => {
              setListeningOffline(false)
              onRetry()
            }}
          >
            Try to reconnect
          </button>
        </div>
        <PlayerSheet />
      </PlayerProvider>
    )
  }

  return (
    <div className="page connection-problem">
      <h1>Can't connect</h1>
      <p>{problem.summary}</p>
      <ul>
        <li>Check this device is on your home Wi-Fi, or that your VPN (such as Tailscale) is connected.</li>
        <li>
          If you're on a VPN, make sure it can reach your home network and that <strong>{location.host}</strong> resolves there.
        </li>
        <li>Nothing is wrong with your account or setup; this page fixes itself once the server is reachable.</li>
      </ul>
      {offline.saved.length > 0 && (
        <button className="primary" onClick={() => setListeningOffline(true)}>
          Listen to saved books ({offline.saved.length})
        </button>
      )}
      <button className={offline.saved.length > 0 ? '' : 'primary'} onClick={onRetry} disabled={retrying}>
        {retrying ? 'Trying…' : 'Try again'}
      </button>
      <p className="connection-detail">{problem.detail}</p>
    </div>
  )
}

/** The saved books, each opening its page (the player is already provided around this). */
function SavedOffline() {
  const [open, setOpen] = useState<ListenItem | null>(null)
  return (
    <>
      <SavedBooks onOpen={setOpen} />
      {open && <ListenBookSheet item={open} onClose={() => setOpen(null)} />}
    </>
  )
}
