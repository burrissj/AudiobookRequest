import { api, ApiError } from '../api/client'
import type { Bookmark } from '../api/types'

/** A bookmark made but not yet delivered (no signal). It is kept on the device and sent when the connection returns. */
export interface PendingBookmark {
  /** Made up here; the server uses it to recognise a repeat, so a bookmark sent twice is still saved once. */
  clientId: string
  itemId: string
  positionSeconds: number
  note: string | null
  label: string | null
  createdAt: string
}

const KEY = 'abr.bookmarks.pending'

/** Fired when the person's rating/tags/bookmarks changed somewhere, so other screens can refresh. */
export const PERSONAL_CHANGED = 'abr:personal-changed'

export function announcePersonalChange() {
  window.dispatchEvent(new Event(PERSONAL_CHANGED))
}

export function newClientId(): string {
  try {
    return crypto.randomUUID()
  } catch {
    return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 10)}`
  }
}

export function loadPendingBookmarks(): PendingBookmark[] {
  try {
    const raw = localStorage.getItem(KEY)
    const parsed = raw ? (JSON.parse(raw) as unknown) : []
    return Array.isArray(parsed) ? (parsed as PendingBookmark[]) : []
  } catch {
    return []
  }
}

function save(list: PendingBookmark[]) {
  try {
    localStorage.setItem(KEY, JSON.stringify(list))
  } catch {
    // Storage blocked: an undelivered bookmark made without signal is lost, never delivered wrongly.
  }
}

export function addPendingBookmark(b: PendingBookmark) {
  save([...loadPendingBookmarks().filter((p) => p.clientId !== b.clientId), b])
}

export function removePendingBookmark(clientId: string) {
  save(loadPendingBookmarks().filter((p) => p.clientId !== clientId))
}

export function pendingBookmarksFor(itemId: string): PendingBookmark[] {
  return loadPendingBookmarks().filter((p) => p.itemId === itemId)
}

/** The server's bookmarks with the not-yet-delivered ones mixed in, in book order. */
export function mergeBookmarks(server: Bookmark[], pending: PendingBookmark[]): Bookmark[] {
  const waiting: Bookmark[] = pending.map((p) => ({
    id: 0,
    pendingClientId: p.clientId,
    positionSeconds: p.positionSeconds,
    note: p.note,
    label: p.label,
    createdAt: p.createdAt,
  }))
  return [...server, ...waiting].sort((a, b) => a.positionSeconds - b.positionSeconds)
}

/**
 * Sends every waiting bookmark. One that gets through (or that the server recognises as already saved) is
 * dropped from the queue; one refused for good (the book is gone, the limit is reached) is dropped too, since
 * retrying can't help; anything else (no connection) stays for next time.
 * @returns how many were delivered
 */
export async function flushPendingBookmarks(): Promise<number> {
  let delivered = 0
  for (const p of loadPendingBookmarks()) {
    try {
      await api.post(`/api/listen/items/${p.itemId}/bookmarks`, {
        positionSeconds: p.positionSeconds,
        note: p.note,
        label: p.label,
        clientId: p.clientId,
      })
      removePendingBookmark(p.clientId)
      delivered++
    } catch (err) {
      if (err instanceof ApiError && err.status !== 401 && err.status < 500) removePendingBookmark(p.clientId)
      else break
    }
  }
  if (delivered > 0) announcePersonalChange()
  return delivered
}
