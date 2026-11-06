import type { ListenItem } from '../api/types'
import type { ListenView } from '../hooks/useListenView'
import { formatBytes } from '../lib/format'
import { loadPending } from '../player/listenTracker'
import { formatDuration } from '../player/playerMath'
import { cancelDownload, enqueueDownload, useOffline } from '../offline/downloadManager'
import { usePlayer } from '../state/usePlayer'
import { ListenTile } from './ListenTile'

/**
 * The books saved on this device, plus any downloads in progress. Works without a server: everything shown
 * comes from the device itself, so this is also what the app offers when it can't reach the server.
 */
export function SavedBooks({ search = '', view = 'grid', onOpen }: { search?: string; view?: ListenView; onOpen: (item: ListenItem) => void }) {
  const player = usePlayer()
  const offline = useOffline()
  const pending = loadPending()
  const term = search.trim().toLowerCase()

  const saved = offline.saved.filter((m) => !term || m.title.toLowerCase().includes(term) || (m.author ?? '').toLowerCase().includes(term))
  const used = offline.saved.reduce((sum, m) => sum + m.size, 0)

  return (
    <div className="saved-books">
      {offline.active.length > 0 && (
        <ul className="listen-list saved-active">
          {offline.active.map((a) => {
            const pct = a.totalBytes > 0 ? Math.floor((a.doneBytes / a.totalBytes) * 100) : 0
            return (
              <li key={a.id} className="saved-active-row">
                <div className="saved-active-text">
                  <strong>{a.title || 'Preparing…'}</strong>
                  {a.state === 'error' ? (
                    <small className="error">{a.message}</small>
                  ) : (
                    <small>{a.state === 'queued' ? 'Waiting to start' : `Downloading ${pct}%`}</small>
                  )}
                  {a.state === 'downloading' && (
                    <span className="listen-row-bar" aria-hidden="true">
                      <span style={{ width: `${pct}%` }} />
                    </span>
                  )}
                </div>
                {a.state === 'error' ? (
                  <button onClick={() => enqueueDownload(a.id, a.title)}>Retry</button>
                ) : null}
                <button onClick={() => cancelDownload(a.id)}>{a.state === 'error' ? 'Dismiss' : 'Cancel'}</button>
              </li>
            )
          })}
        </ul>
      )}

      {saved.length === 0 && offline.active.length === 0 && (
        <p className="subtitle">
          {term
            ? `Nothing saved matches "${search.trim()}".`
            : 'Nothing saved on this device yet. Tap the download button on a book to listen without a connection.'}
        </p>
      )}

      {saved.length > 0 && (
        <ul className={`listen-grid${view === 'shelf' ? ' listen-bookshelf' : ''}`}>
          {saved.map((m) => {
            const place = pending[m.id]?.position ?? m.detail.positionSeconds
            const left = Math.max(0, m.detail.durationSeconds - place)
            return (
              <ListenTile
                key={m.id}
                id={m.id}
                hasCover={m.detail.hasCover}
                title={m.title}
                author={m.author ?? null}
                meta={place > 0 ? `${formatDuration(left)} left` : formatDuration(m.detail.durationSeconds)}
                detail={formatBytes(m.size)}
                fraction={m.detail.durationSeconds > 0 ? place / m.detail.durationSeconds : 0}
                active={player.current?.id === m.id}
                onOpen={() => onOpen({ ...m.detail, title: m.title, author: m.author ?? null, positionSeconds: place })}
              />
            )
          })}
        </ul>
      )}

      {offline.saved.length > 0 && (
        <p className="subtitle saved-usage">
          {offline.saved.length} book{offline.saved.length === 1 ? '' : 's'} saved · {formatBytes(used)} on this device
          {offline.usage ? ` · ${formatBytes(Math.max(0, offline.usage.quota - offline.usage.usage))} free for the app` : ''}
        </p>
      )}
    </div>
  )
}
