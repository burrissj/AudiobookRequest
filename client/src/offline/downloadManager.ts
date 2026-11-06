import { useSyncExternalStore } from 'react'
import { api, ApiError } from '../api/client'
import type { ListenDetail } from '../api/types'
import { downloadBook, listOffline, offlineSupported, removeOffline, storageEstimate, type OfflineMeta } from './offlineStore'

export type DownloadState = 'queued' | 'downloading' | 'error'

export interface ActiveDownload {
  id: string
  title: string
  state: DownloadState
  doneBytes: number
  totalBytes: number
  message?: string
}

export interface OfflineSnapshot {
  supported: boolean
  /** Books fully saved on this device. */
  saved: OfflineMeta[]
  /** Downloads running, waiting, or failed (a failed one stays listed so it can be retried). */
  active: ActiveDownload[]
  usage: { usage: number; quota: number } | null
  /** Books found to be unsavable (Jellyfin converts them for playback), so the button can stop offering. */
  unavailable: string[]
}

// One download manager for the whole page, kept outside React so a download carries on while the person moves
// around the app, and so the offline screen (shown when the server can't be reached) can use it too.
let snapshot: OfflineSnapshot = { supported: offlineSupported(), saved: [], active: [], usage: null, unavailable: [] }
const listeners = new Set<() => void>()
const controllers = new Map<string, AbortController>()
let running = false

function set(patch: Partial<OfflineSnapshot>) {
  snapshot = { ...snapshot, ...patch }
  listeners.forEach((l) => l())
}

function patchActive(id: string, patch: Partial<ActiveDownload> | null) {
  const rest = snapshot.active.filter((a) => a.id !== id)
  if (patch === null) {
    set({ active: rest })
    return
  }
  const existing = snapshot.active.find((a) => a.id === id)
  const merged = { ...(existing ?? { id, title: '', state: 'queued' as DownloadState, doneBytes: 0, totalBytes: 0 }), ...patch }
  // Keep queue order stable: replace in place.
  set({ active: existing ? snapshot.active.map((a) => (a.id === id ? merged : a)) : [...rest, merged] })
}

export async function refreshSaved() {
  if (!offlineSupported()) return
  const [saved, usage] = await Promise.all([listOffline(), storageEstimate()])
  set({ saved, usage })
}

async function runQueue() {
  if (running) return
  running = true
  try {
    for (;;) {
      const next = snapshot.active.find((a) => a.state === 'queued')
      if (!next) break
      const controller = new AbortController()
      controllers.set(next.id, controller)
      patchActive(next.id, { state: 'downloading', message: undefined })

      try {
        const detail = await api.get<ListenDetail>(`/api/listen/items/${next.id}`)
        patchActive(next.id, { title: detail.title })
        if (!detail.canDownload) {
          // Not a failure to retry: this book can never be saved as-is.
          set({ unavailable: [...snapshot.unavailable, next.id] })
          patchActive(next.id, null)
          continue
        }
        await downloadBook(
          detail,
          (p) => patchActive(next.id, { doneBytes: p.doneBytes, totalBytes: p.totalBytes }),
          controller.signal,
        )
        patchActive(next.id, null)
        await refreshSaved()
      } catch (err) {
        if (err instanceof DOMException && err.name === 'AbortError') {
          patchActive(next.id, null)
        } else {
          const message =
            err instanceof ApiError ? err.message : err instanceof TypeError ? 'The connection dropped. It will pick up where it left off when you try again.' : err instanceof Error ? err.message : 'The download failed.'
          patchActive(next.id, { state: 'error', message })
        }
      } finally {
        controllers.delete(next.id)
      }
    }
  } finally {
    running = false
  }
}

/** Queue a book for download; downloads run one at a time. Calling it again for a failed one retries it. */
export function enqueueDownload(id: string, title: string) {
  if (!offlineSupported()) return
  const existing = snapshot.active.find((a) => a.id === id)
  if (existing && existing.state !== 'error') return
  patchActive(id, { title, state: 'queued', message: undefined, doneBytes: existing?.doneBytes ?? 0, totalBytes: existing?.totalBytes ?? 0 })
  void runQueue()
}

/** Stop a download. What was already fetched is kept, so starting it again continues. */
export function cancelDownload(id: string) {
  const controller = controllers.get(id)
  if (controller) controller.abort()
  else patchActive(id, null)
}

export async function removeSaved(id: string) {
  cancelDownload(id)
  await removeOffline(id)
  await refreshSaved()
}

function subscribe(listener: () => void) {
  listeners.add(listener)
  return () => listeners.delete(listener)
}

const getSnapshot = () => snapshot

/** The saved books, running downloads and storage use -- live. */
export function useOffline(): OfflineSnapshot {
  return useSyncExternalStore(subscribe, getSnapshot)
}

if (offlineSupported()) void refreshSaved()
