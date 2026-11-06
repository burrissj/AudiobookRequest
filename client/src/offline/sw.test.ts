import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import swSource from '../../public/sw.js?raw'
import { chunkKey, coverKey, metaKey, OFFLINE_CACHE } from './offlineStore'

// Runs the real public/sw.js against an in-memory Cache Storage, so the offline playback logic is tested as shipped.

const ID = '0123456789abcdef0123456789abcdef'
const OTHER_ID = 'fedcba9876543210fedcba9876543210'

interface Stored {
  body: Uint8Array
  status: number
  headers: [string, string][]
}

class MemCache {
  items = new Map<string, Stored>()

  private key(k: string | Request): string {
    if (typeof k === 'string') return k
    const u = new URL(k.url)
    return u.pathname + u.search
  }

  async match(k: string | Request): Promise<Response | undefined> {
    const s = this.items.get(this.key(k))
    return s ? new Response(s.body.slice(), { status: s.status, headers: s.headers }) : undefined
  }

  async put(k: string | Request, res: Response) {
    this.items.set(this.key(k), { body: new Uint8Array(await res.arrayBuffer()), status: res.status, headers: [...res.headers.entries()] })
  }

  async delete(k: string | Request) {
    return this.items.delete(this.key(k))
  }
}

class MemStorage {
  caches = new Map<string, MemCache>()
  async open(name: string) {
    if (!this.caches.has(name)) this.caches.set(name, new MemCache())
    return this.caches.get(name)!
  }
  async keys() {
    return [...this.caches.keys()]
  }
  async delete(name: string) {
    return this.caches.delete(name)
  }
  async match(k: string) {
    for (const c of this.caches.values()) {
      const r = await c.match(k)
      if (r) return r
    }
    return undefined
  }
}

type Handler = (event: unknown) => void

function loadWorker(storage: MemStorage, fetchImpl: (r: Request) => Promise<Response>) {
  const handlers: Record<string, Handler> = {}
  const fakeSelf = {
    location: { origin: 'http://localhost' },
    addEventListener: (type: string, fn: Handler) => {
      handlers[type] = fn
    },
    skipWaiting: () => {},
    clients: { claim: async () => {} },
  }
  new Function('self', 'caches', 'fetch', swSource)(fakeSelf, storage, fetchImpl)

  /** Fires a GET at the worker; resolves to its response, or null if the worker left it alone. */
  async function get(path: string, headers: Record<string, string> = {}, mode = 'cors'): Promise<Response | null> {
    const request = { url: `http://localhost${path}`, method: 'GET', mode, headers: new Headers(headers) }
    let responded: Promise<Response> | undefined
    handlers.fetch({ request, respondWith: (p: Promise<Response>) => (responded = Promise.resolve(p)) })
    return responded ?? null
  }

  async function activate() {
    let done: Promise<unknown> = Promise.resolve()
    handlers.activate({ waitUntil: (p: Promise<unknown>) => (done = p) })
    await done
  }

  return { get, activate }
}

// A 25-byte "book" in 10-byte chunks: the last chunk is short, and ranges can cross chunk boundaries.
const BOOK = Uint8Array.from({ length: 25 }, (_, i) => i + 100)
const CHUNK = 10

async function saveBook(storage: MemStorage, id = ID, opts: { skipChunk?: number } = {}) {
  const cache = await storage.open(OFFLINE_CACHE)
  const meta = { id, size: BOOK.length, chunkSize: CHUNK, chunks: 3, contentType: 'audio/mp4', title: 'T', detail: {} }
  await cache.put(metaKey(id), new Response(JSON.stringify(meta)))
  for (let n = 0; n < 3; n++) {
    if (n === opts.skipChunk) continue
    await cache.put(chunkKey(id, n), new Response(BOOK.slice(n * CHUNK, (n + 1) * CHUNK)))
  }
  await cache.put(coverKey(id), new Response(new Uint8Array([9, 9, 9]), { headers: { 'Content-Type': 'image/jpeg' } }))
}

const bytes = async (r: Response | null) => new Uint8Array(await r!.arrayBuffer())
const network = (label: string) => async () => new Response(label)

describe('service worker: saved books', () => {
  let storage: MemStorage
  beforeEach(() => {
    storage = new MemStorage()
  })

  it('leaves the stream alone (goes to the network) for a book that is not saved', async () => {
    const sw = loadWorker(storage, network('FROM-NETWORK'))
    const res = await sw.get(`/api/listen/items/${ID}/stream`)
    expect(await res!.text()).toBe('FROM-NETWORK')
  })

  it('serves a whole saved book, even with the network available', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('FROM-NETWORK'))

    const res = await sw.get(`/api/listen/items/${ID}/stream`)

    expect(res!.status).toBe(200)
    expect(res!.headers.get('Content-Length')).toBe('25')
    expect(res!.headers.get('Accept-Ranges')).toBe('bytes')
    expect(res!.headers.get('Content-Type')).toBe('audio/mp4')
    expect(await bytes(res)).toEqual(BOOK)
  })

  it('answers a range that crosses a chunk boundary with exactly those bytes', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('x'))

    const res = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=8-22' })

    expect(res!.status).toBe(206)
    expect(res!.headers.get('Content-Range')).toBe('bytes 8-22/25')
    expect(res!.headers.get('Content-Length')).toBe('15')
    expect(await bytes(res)).toEqual(BOOK.slice(8, 23))
  })

  it('answers the single-byte probe browsers send first', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('x'))

    const res = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=0-1' })

    expect(res!.status).toBe(206)
    expect(res!.headers.get('Content-Range')).toBe('bytes 0-1/25')
    expect(await bytes(res)).toEqual(BOOK.slice(0, 2))
  })

  it('handles open-ended and suffix ranges', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('x'))

    const open = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=20-' })
    expect(open!.headers.get('Content-Range')).toBe('bytes 20-24/25')
    expect(await bytes(open)).toEqual(BOOK.slice(20))

    const suffix = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=-5' })
    expect(suffix!.headers.get('Content-Range')).toBe('bytes 20-24/25')
    expect(await bytes(suffix)).toEqual(BOOK.slice(20))
  })

  it('clamps an end beyond the file, and refuses a start beyond it with 416', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('x'))

    const clamped = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=22-9999' })
    expect(clamped!.headers.get('Content-Range')).toBe('bytes 22-24/25')

    const past = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=25-' })
    expect(past!.status).toBe(416)
    expect(past!.headers.get('Content-Range')).toBe('bytes */25')

    const backwards = await sw.get(`/api/listen/items/${ID}/stream`, { Range: 'bytes=12-3' })
    expect(backwards!.status).toBe(416)
  })

  it('serves every byte exactly once when read in arbitrary ranges', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('x'))
    const out: number[] = []
    for (const [s, e] of [[0, 3], [4, 4], [5, 17], [18, 24]]) {
      out.push(...(await bytes(await sw.get(`/api/listen/items/${ID}/stream`, { Range: `bytes=${s}-${e}` }))))
    }
    expect(out).toEqual([...BOOK])
  })

  it('errors the stream rather than inventing audio when a saved part is missing', async () => {
    await saveBook(storage, ID, { skipChunk: 1 })
    const sw = loadWorker(storage, network('x'))

    const res = await sw.get(`/api/listen/items/${ID}/stream`)

    await expect(res!.arrayBuffer()).rejects.toThrow()
  })

  it('does not serve one book from another book’s files', async () => {
    await saveBook(storage, OTHER_ID)
    const sw = loadWorker(storage, network('FROM-NETWORK'))

    const res = await sw.get(`/api/listen/items/${ID}/stream`)

    expect(await res!.text()).toBe('FROM-NETWORK')
  })

  it('uses the saved cover only when the network is down', async () => {
    await saveBook(storage)
    const online = loadWorker(storage, network('LIVE-COVER'))
    expect(await (await online.get(`/api/listen/items/${ID}/image`))!.text()).toBe('LIVE-COVER')

    const offline = loadWorker(storage, async () => {
      throw new TypeError('Failed to fetch')
    })
    expect(await bytes(await offline.get(`/api/listen/items/${ID}/image`))).toEqual(new Uint8Array([9, 9, 9]))
  })

  it('never touches other API calls', async () => {
    await saveBook(storage)
    const sw = loadWorker(storage, network('x'))
    expect(await sw.get('/api/listen/library')).toBeNull()
    expect(await sw.get(`/api/listen/items/${ID}`)).toBeNull()
    expect(await sw.get(`/api/listen/items/${ID}/progress`)).toBeNull()
    expect(await sw.get('/hubs/requests')).toBeNull()
  })
})

describe('service worker: updates', () => {
  it('keeps saved books when it cleans up old caches, but deletes caches it no longer uses', async () => {
    const storage = new MemStorage()
    await saveBook(storage)
    await storage.open('abr-assets-v1')
    await storage.open('abr-shell-v1')
    await storage.open('abr-assets-v0-ancient')
    await storage.open('someone-elses-cache')
    const sw = loadWorker(storage, network('x'))

    await sw.activate()

    const left = await storage.keys()
    expect(left).toContain(OFFLINE_CACHE)
    expect(left).toContain('abr-assets-v1')
    expect(left).toContain('abr-shell-v1')
    expect(left).not.toContain('abr-assets-v0-ancient')
    expect(left).toContain('someone-elses-cache')
    // and the book itself is still whole
    expect(await (await storage.open(OFFLINE_CACHE)).match(metaKey(ID))).toBeDefined()
  })

  it('uses the same cache name as the page code', () => {
    expect(swSource).toContain(`const OFFLINE_CACHE = '${OFFLINE_CACHE}'`)
  })
})

describe('service worker: opening the app on a bad connection', () => {
  beforeEach(() => vi.useFakeTimers())
  afterEach(() => vi.useRealTimers())

  it('falls back to the last shell it saw when the server does not answer within a few seconds', async () => {
    const storage = new MemStorage()
    await (await storage.open('abr-shell-v1')).put('/', new Response('<html>cached shell</html>'))
    const sw = loadWorker(storage, () => new Promise<Response>(() => {})) // never answers

    const pending = sw.get('/listen', {}, 'navigate')
    await vi.advanceTimersByTimeAsync(4100)

    expect(await (await pending)!.text()).toBe('<html>cached shell</html>')
  })

  it('shows a short notice when there is nothing cached and no connection', async () => {
    const sw = loadWorker(new MemStorage(), async () => {
      throw new TypeError('Failed to fetch')
    })

    const res = await sw.get('/', {}, 'navigate')

    expect(res!.status).toBe(503)
    expect(await res!.text()).toContain('offline')
  })
})
