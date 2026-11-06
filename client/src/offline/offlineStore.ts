import type { ListenDetail } from '../api/types'

/**
 * Offline books live in the browser's Cache Storage as 4 MB chunks under made-up addresses:
 *   /__offline/{id}/meta      what the book is (JSON) -- written last, so a half-finished download never looks complete
 *   /__offline/{id}/c/{n}     the audio, chunk n
 *   /__offline/{id}/cover     the cover image
 * The service worker (public/sw.js) answers the player's stream requests for a saved book from these chunks, with
 * proper Range support, so the same <audio> code plays it online or off. Keep names in sync with sw.js.
 */
export const OFFLINE_CACHE = 'abr-offline-v1'
export const CHUNK_SIZE = 4 * 1024 * 1024
const MIN_FREE_BYTES = 100 * 1024 * 1024

export interface OfflineMeta {
  id: string
  title: string
  author: string | null
  size: number
  chunkSize: number
  chunks: number
  contentType: string
  downloadedAt: number
  detail: ListenDetail
}

export interface DownloadProgress {
  doneBytes: number
  totalBytes: number
}

export function offlineSupported(): boolean {
  return typeof caches !== 'undefined' && 'serviceWorker' in navigator && window.isSecureContext
}

export const metaKey = (id: string) => `/__offline/${id}/meta`
export const chunkKey = (id: string, n: number) => `/__offline/${id}/c/${n}`
export const coverKey = (id: string) => `/__offline/${id}/cover`
const partialKey = (id: string) => `/__offline/${id}/partial`

export function chunkCount(size: number, chunkSize = CHUNK_SIZE): number {
  return Math.ceil(size / chunkSize)
}

/** The inclusive byte range of chunk n of a file of `size` bytes. */
export function chunkBytes(size: number, n: number, chunkSize = CHUNK_SIZE): { start: number; end: number } {
  const start = n * chunkSize
  return { start, end: Math.min(size - 1, start + chunkSize - 1) }
}

/** Total size from a Content-Range header like "bytes 0-4194303/638731591". */
export function totalFromContentRange(header: string | null): number | null {
  const m = header ? /\/(\d+)\s*$/.exec(header) : null
  return m ? Number(m[1]) : null
}

export async function listOffline(): Promise<OfflineMeta[]> {
  if (!offlineSupported()) return []
  const cache = await caches.open(OFFLINE_CACHE)
  const keys = await cache.keys()
  const metas: OfflineMeta[] = []
  for (const k of keys) {
    if (!k.url.endsWith('/meta') || !k.url.includes('/__offline/')) continue
    const res = await cache.match(k)
    if (!res) continue
    try {
      metas.push((await res.json()) as OfflineMeta)
    } catch {
      // A damaged entry is skipped; removing the book clears it.
    }
  }
  return metas.sort((a, b) => b.downloadedAt - a.downloadedAt)
}

export async function getOfflineMeta(id: string): Promise<OfflineMeta | null> {
  if (!offlineSupported()) return null
  const cache = await caches.open(OFFLINE_CACHE)
  const res = await cache.match(metaKey(id))
  if (!res) return null
  try {
    return (await res.json()) as OfflineMeta
  } catch {
    return null
  }
}

export async function removeOffline(id: string): Promise<void> {
  if (!offlineSupported()) return
  const cache = await caches.open(OFFLINE_CACHE)
  const prefix = `/__offline/${id}/`
  for (const k of await cache.keys()) {
    if (new URL(k.url).pathname.startsWith(prefix)) await cache.delete(k)
  }
}

export async function storageEstimate(): Promise<{ usage: number; quota: number } | null> {
  try {
    const e = await navigator.storage?.estimate()
    return e?.quota ? { usage: e.usage ?? 0, quota: e.quota } : null
  } catch {
    return null
  }
}

/**
 * Saves a book for offline listening. Resumable: chunks already stored are skipped, so a dropped connection or a
 * closed tab costs only the chunk in flight. Throws on failure (and AbortError when cancelled); a partly saved book
 * is left in place so the next attempt continues, and removed with removeOffline.
 */
export async function downloadBook(
  detail: ListenDetail,
  onProgress: (p: DownloadProgress) => void,
  signal: AbortSignal,
): Promise<void> {
  if (!offlineSupported()) throw new Error("This browser can't save books for offline listening.")
  if (!detail.canDownload) throw new Error('This book has to be converted to play, so it can’t be saved for offline.')

  const cache = await caches.open(OFFLINE_CACHE)
  const url = `/api/listen/items/${detail.id}/stream`
  const fetchRange = async (start: number, end: number) => {
    const res = await fetch(url, { headers: { Range: `bytes=${start}-${end}` }, credentials: 'include', signal })
    if (res.status === 401) throw new Error('Sign out and back in, then try again.')
    if (res.status !== 206 && res.status !== 200) throw new Error(`The server couldn't send the audio (${res.status}).`)
    return res
  }

  // Learn the file's size from the first chunk (or from the note left by an earlier attempt).
  let size: number | null = null
  let contentType = 'audio/mp4'
  const note = await cache.match(partialKey(detail.id))
  if (note) {
    const p = (await note.json()) as { size: number; contentType: string }
    size = p.size
    contentType = p.contentType
  }

  if (size === null) {
    const first = await fetchRange(0, CHUNK_SIZE - 1)
    size = totalFromContentRange(first.headers.get('Content-Range')) ?? Number(first.headers.get('Content-Length'))
    contentType = first.headers.get('Content-Type') ?? contentType
    if (!size || !Number.isFinite(size)) throw new Error("Couldn't tell how big this book is.")

    const estimate = await storageEstimate()
    if (estimate && size > estimate.quota - estimate.usage - MIN_FREE_BYTES) {
      throw new Error('Not enough free space on this device for this book.')
    }
    // Ask the browser not to evict saved books under storage pressure; harmless if refused.
    void navigator.storage?.persist?.().catch(() => {})

    const blob = await first.blob()
    if (blob.size !== chunkBytes(size, 0).end + 1) throw new Error('The first part of the download came back the wrong size.')
    await cache.put(chunkKey(detail.id, 0), new Response(blob))
    await cache.put(partialKey(detail.id), new Response(JSON.stringify({ size, contentType })))
  }

  const total = chunkCount(size)
  let doneBytes = 0
  for (let n = 0; n < total; n++) {
    signal.throwIfAborted()
    const { start, end } = chunkBytes(size, n)
    if (await cache.match(chunkKey(detail.id, n))) {
      doneBytes += end - start + 1
      onProgress({ doneBytes, totalBytes: size })
      continue
    }
    const res = await fetchRange(start, end)
    const blob = await res.blob()
    if (blob.size !== end - start + 1) throw new Error('Part of the download came back the wrong size. Try again.')
    await cache.put(chunkKey(detail.id, n), new Response(blob))
    doneBytes += blob.size
    onProgress({ doneBytes, totalBytes: size })
  }

  // The cover is nice to have; the book is saved without it if it can't be fetched.
  try {
    const cover = await fetch(`/api/listen/items/${detail.id}/image?w=600`, { credentials: 'include', signal })
    if (cover.ok) await cache.put(coverKey(detail.id), cover)
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') throw err
  }

  const meta: OfflineMeta = {
    id: detail.id,
    title: detail.title,
    author: detail.author,
    size,
    chunkSize: CHUNK_SIZE,
    chunks: total,
    contentType,
    downloadedAt: Date.now(),
    detail,
  }
  await cache.put(metaKey(detail.id), new Response(JSON.stringify(meta), { headers: { 'Content-Type': 'application/json' } }))
  await cache.delete(partialKey(detail.id))
}
