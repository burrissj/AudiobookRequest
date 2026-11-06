// Service worker. It exists so the app can be installed, opens quickly, and can play books saved for offline.
// It never caches data: every /api and /hubs request goes straight to the network -- with one exception, the audio
// and cover of books the person chose to save, which are served from the offline cache so they play with no signal.
//
//  * /assets/* (build output, content-hashed so a file never changes under the same name): cache first.
//  * Page navigations: network first (so a new release is picked up immediately), but give up after a few seconds
//    and use the last shell we saw, so the app still opens on a slow or half-working VPN.
//  * /api/listen/items/{id}/stream for a saved book: answered from the offline cache, with Range support.
const ASSET_CACHE = 'abr-assets-v1'
const SHELL_CACHE = 'abr-shell-v1'
// Must match OFFLINE_CACHE in src/offline/offlineStore.ts. It MUST be in this list: anything else starting with
// "abr-" is deleted on activate, and that would erase the person's saved books on every app update.
const OFFLINE_CACHE = 'abr-offline-v1'
const CURRENT_CACHES = [ASSET_CACHE, SHELL_CACHE, OFFLINE_CACHE]
const NAVIGATE_TIMEOUT_MS = 4000

self.addEventListener('install', () => {
  self.skipWaiting()
})

self.addEventListener('activate', (event) => {
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.filter((k) => k.startsWith('abr-') && !CURRENT_CACHES.includes(k)).map((k) => caches.delete(k))))
      .then(() => self.clients.claim()),
  )
})

const STREAM_PATH = /^\/api\/listen\/items\/([0-9a-fA-F]{32})\/stream$/
const IMAGE_PATH = /^\/api\/listen\/items\/([0-9a-fA-F]{32})\/image$/

// "bytes=100-199", "bytes=100-", "bytes=-500" -> {start, end} clamped to the file, or null if unsatisfiable/invalid.
function parseRange(header, size) {
  const m = /^bytes=(\d*)-(\d*)$/.exec((header || '').trim())
  if (!m || (m[1] === '' && m[2] === '')) return { invalid: true }
  let start
  let end
  if (m[1] === '') {
    const n = parseInt(m[2], 10)
    start = Math.max(0, size - n)
    end = size - 1
  } else {
    start = parseInt(m[1], 10)
    end = m[2] === '' ? size - 1 : Math.min(size - 1, parseInt(m[2], 10))
  }
  if (start >= size || start > end) return null
  return { start, end }
}

async function serveSavedBook(request, id) {
  const cache = await caches.open(OFFLINE_CACHE)
  const metaResponse = await cache.match(`/__offline/${id}/meta`)
  if (!metaResponse) return null

  const meta = await metaResponse.json()
  const size = meta.size
  const chunkSize = meta.chunkSize

  const rangeHeader = request.headers.get('range')
  let start = 0
  let end = size - 1
  let partial = false
  if (rangeHeader) {
    const range = parseRange(rangeHeader, size)
    if (range === null) {
      return new Response(null, { status: 416, headers: { 'Content-Range': `bytes */${size}` } })
    }
    if (!range.invalid) {
      start = range.start
      end = range.end
      partial = true
    }
  }

  const first = Math.floor(start / chunkSize)
  const last = Math.floor(end / chunkSize)
  let next = first

  // Streamed one chunk at a time so a 600 MB book never sits in memory.
  const body = new ReadableStream({
    async pull(controller) {
      if (next > last) {
        controller.close()
        return
      }
      const res = await cache.match(`/__offline/${id}/c/${next}`)
      if (!res) {
        controller.error(new Error('A saved part of this book is missing. Remove it and download it again.'))
        return
      }
      const bytes = new Uint8Array(await res.arrayBuffer())
      const chunkStart = next * chunkSize
      const from = Math.max(start, chunkStart) - chunkStart
      const to = Math.min(end, chunkStart + bytes.length - 1) - chunkStart
      controller.enqueue(bytes.subarray(from, to + 1))
      next++
    },
  })

  const headers = {
    'Content-Type': meta.contentType || 'audio/mp4',
    'Accept-Ranges': 'bytes',
    'Content-Length': String(end - start + 1),
    'Cache-Control': 'private, no-store',
  }
  if (partial) headers['Content-Range'] = `bytes ${start}-${end}/${size}`
  return new Response(body, { status: partial ? 206 : 200, headers })
}

async function savedCover(id) {
  const cache = await caches.open(OFFLINE_CACHE)
  return cache.match(`/__offline/${id}/cover`)
}

function fetchWithTimeout(request, ms) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('timeout')), ms)
    fetch(request).then(
      (r) => {
        clearTimeout(timer)
        resolve(r)
      },
      (e) => {
        clearTimeout(timer)
        reject(e)
      },
    )
  })
}

self.addEventListener('fetch', (event) => {
  const request = event.request
  if (request.method !== 'GET') return

  const url = new URL(request.url)
  if (url.origin !== self.location.origin) return

  const stream = STREAM_PATH.exec(url.pathname)
  if (stream) {
    event.respondWith(
      serveSavedBook(request, stream[1].toLowerCase())
        .catch(() => null)
        .then((saved) => saved ?? fetch(request)),
    )
    return
  }

  const image = IMAGE_PATH.exec(url.pathname)
  if (image) {
    // Live cover when we can reach the server; the saved one when we can't.
    event.respondWith(
      fetch(request).catch(async () => (await savedCover(image[1].toLowerCase())) ?? Response.error()),
    )
    return
  }

  if (url.pathname.startsWith('/api/') || url.pathname.startsWith('/hubs/')) return

  if (url.pathname.startsWith('/assets/') || url.pathname.startsWith('/icons/')) {
    event.respondWith(
      caches.open(ASSET_CACHE).then(async (cache) => {
        const hit = await cache.match(request)
        if (hit) return hit
        const response = await fetch(request)
        if (response.ok) cache.put(request, response.clone())
        return response
      }),
    )
    return
  }

  if (request.mode === 'navigate') {
    event.respondWith(
      fetchWithTimeout(request, NAVIGATE_TIMEOUT_MS)
        .then((response) => {
          if (response.ok) {
            const copy = response.clone()
            caches.open(SHELL_CACHE).then((cache) => cache.put('/', copy))
          }
          return response
        })
        .catch(async () => {
          const cached = await caches.match('/')
          return (
            cached ??
            new Response('<!doctype html><meta name="viewport" content="width=device-width"><body style="font:16px system-ui;padding:24px;background:#121214;color:#ddd"><h2>You\'re offline</h2><p>Audiobook Request needs a connection to your server.</p>', {
              status: 503,
              headers: { 'Content-Type': 'text/html; charset=utf-8' },
            })
          )
        }),
    )
  }
})
