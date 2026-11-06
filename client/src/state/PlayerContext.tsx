import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import type Hls from 'hls.js'
import { api, ApiError } from '../api/client'
import type { ListenDetail, ListenItem } from '../api/types'
import { getOfflineMeta } from '../offline/offlineStore'
import { flushPendingBookmarks } from '../player/bookmarks'
import { applyLockScreenControls } from '../player/mediaSession'
import {
  ListenTracker,
  loadPending,
  localDateString,
  updatePending,
  type PendingEntry,
} from '../player/listenTracker'
import { loadPrefs, savePrefs, smartRewindSeconds, type PlayerPrefs } from '../player/prefs'
import { chapterIndexAt, clampSpeed, loadSpeed, saveSpeed, stepSpeed } from '../player/playerMath'
import { PlayerContext, type PlayerContextValue, type PlayerStatus, type SleepTimer, type SyncState } from './usePlayer'

const REPORT_EVERY_MS = 10_000
const PENDING_SAVE_EVERY_MS = 5_000
// A book left within this long of its end starts over rather than resuming into the credits.
const RESTART_WITHIN_SECONDS = 15
const NOTICE_MS = 7_000

function newSessionId(): string {
  try {
    return crypto.randomUUID().replaceAll('-', '')
  } catch {
    return Math.random().toString(16).slice(2) + Date.now().toString(16)
  }
}

const round1 = (n: number) => Math.round(n * 10) / 10

// Apple's browsers play HLS natively and do it better than any library; everyone else uses hls.js.
function shouldUseNativeHls(audio: HTMLAudioElement): boolean {
  const ua = navigator.userAgent
  const apple = /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)
  return apple && audio.canPlayType('application/vnd.apple.mpegurl') !== ''
}

// keepalive lets the last report go out while the page is being closed or reloaded. It carries the place only:
// listening time stays saved on the device until a normal report confirms it, so it can't be counted twice.
function reportPlaceWithKeepalive(itemId: string, positionSeconds: number, paused: boolean, mediaSourceId: string | null, playSessionId: string) {
  const query = mediaSourceId ? `?mediaSourceId=${encodeURIComponent(mediaSourceId)}` : ''
  try {
    void fetch(`/api/listen/items/${itemId}/progress${query}`, {
      method: 'POST',
      credentials: 'include',
      keepalive: true,
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ positionSeconds, isPaused: paused, event: 'progress', playSessionId }),
    }).catch(() => {})
  } catch {
    // Nothing more to try.
  }
}

export function PlayerProvider({ children }: { children: ReactNode }) {
  const audioRef = useRef<HTMLAudioElement>(null)
  const hlsRef = useRef<Hls | null>(null)
  const sessionIdRef = useRef('')
  const startAtRef = useRef(0)
  const lastReportRef = useRef(0)
  const lastPendingSaveRef = useRef(0)
  const pausedAtRef = useRef<number | null>(null)
  const trackerRef = useRef(new ListenTracker())

  const [current, setCurrent] = useState<ListenDetail | null>(null)
  const [status, setStatus] = useState<PlayerStatus>('idle')
  const [buffering, setBuffering] = useState(false)
  const [position, setPosition] = useState(0)
  const [duration, setDuration] = useState(0)
  const [speed, setSpeedState] = useState(1)
  const [error, setError] = useState<string | null>(null)
  const [sleep, setSleep] = useState<SleepTimer>(null)
  const [expanded, setExpanded] = useState(false)
  const [prefs, setPrefsState] = useState<PlayerPrefs>(loadPrefs)
  const [sync, setSync] = useState<SyncState>({ status: 'idle', at: null, message: null })
  const [upNext, setUpNext] = useState<ListenItem | null>(null)
  const [notice, setNotice] = useState<string | null>(null)

  // Mirrors for event handlers that outlive a render. currentRef is set by hand next to setCurrent so it can be
  // cleared the instant a book is switched or closed.
  const currentRef = useRef<ListenDetail | null>(null)
  const speedRef = useRef(1)
  const sleepRef = useRef<SleepTimer>(null)
  const prefsRef = useRef(prefs)
  const upNextRef = useRef<ListenItem | null>(null)
  const playRef = useRef<(id: string) => Promise<void>>(async () => {})
  useEffect(() => {
    speedRef.current = speed
    sleepRef.current = sleep
    prefsRef.current = prefs
    upNextRef.current = upNext
  }, [speed, sleep, prefs, upNext])

  const chapterIndex = useMemo(() => (current ? chapterIndexAt(current.chapters, position) : -1), [current, position])

  // ---- delivery of place + listening time to the server ----

  const savePending = useCallback(() => {
    const item = currentRef.current
    const audio = audioRef.current
    if (!item || !audio) return
    const t = trackerRef.current
    if (t.content < 0.5 && t.wall < 0.5) {
      updatePending(item.id, null)
      return
    }
    const entry: PendingEntry = {
      content: round1(t.content),
      wall: round1(t.wall),
      position: audio.currentTime,
      title: item.title,
      author: item.author,
      mediaSourceId: item.mediaSourceId,
      localDate: localDateString(),
    }
    updatePending(item.id, entry)
  }, [])

  const report = useCallback(
    (event: 'start' | 'progress' | 'stop') => {
      const audio = audioRef.current
      const item = currentRef.current
      if (!audio || !item) return

      const sent = { content: round1(trackerRef.current.content), wall: round1(trackerRef.current.wall) }
      const body = {
        positionSeconds: audio.currentTime,
        isPaused: audio.paused,
        event,
        playSessionId: sessionIdRef.current,
        listenedSeconds: sent.content,
        wallSeconds: sent.wall,
        localDate: localDateString(),
        title: item.title,
        author: item.author,
      }
      lastReportRef.current = Date.now()
      // Persist before sending: if the tab dies mid-request, the listening is still on the device to deliver later.
      savePending()

      const query = item.mediaSourceId ? `?mediaSourceId=${encodeURIComponent(item.mediaSourceId)}` : ''
      api
        .post(`/api/listen/items/${item.id}/progress${query}`, body)
        .then(() => {
          trackerRef.current.subtract(sent)
          setSync({ status: 'ok', at: Date.now(), message: null })
          if (currentRef.current?.id === item.id) savePending()
          else updatePending(item.id, null)
        })
        .catch((err) => {
          // Never interrupts listening. What wasn't delivered stays in the tracker/pending and rides the next report.
          const message =
            err instanceof ApiError && err.status === 401
              ? 'Sign out and back in to sync with Jellyfin.'
              : navigator.onLine === false
                ? 'Offline: your place is saved on this device.'
                : "Couldn't reach Jellyfin; will keep trying."
          setSync((s) => ({ status: 'error', at: s.at, message }))
        })
    },
    [savePending],
  )

  const stopHls = useCallback((item: ListenDetail | null) => {
    hlsRef.current?.destroy()
    hlsRef.current = null
    if (item?.streamMode === 'hls') api.delete(`/api/listen/items/${item.id}/hls`).catch(() => {})
  }, [])

  const applySpeed = useCallback((value: number) => {
    const audio = audioRef.current
    if (!audio) return
    audio.defaultPlaybackRate = value
    audio.playbackRate = value
    // Keep voices natural at 0.25x-3x rather than chipmunk/slow-mo.
    audio.preservesPitch = true
  }, [])

  const setSpeed = useCallback(
    (value: number) => {
      const next = clampSpeed(value)
      setSpeedState(next)
      applySpeed(next)
      if (currentRef.current) saveSpeed(currentRef.current.id, next)
    },
    [applySpeed],
  )

  const bumpSpeed = useCallback((direction: 1 | -1) => setSpeed(stepSpeed(speedRef.current, direction)), [setSpeed])

  const setPrefs = useCallback((patch: Partial<PlayerPrefs>) => {
    setPrefsState((p) => {
      const next = { ...p, ...patch }
      savePrefs(next)
      return next
    })
  }, [])

  // Points the <audio> at a book: its own file, or Jellyfin's AAC conversion for formats browsers can't play.
  const attachSource = useCallback(
    async (detail: ListenDetail, resumeAt: number, chosenSpeed: number) => {
      const audio = audioRef.current
      if (!audio) return
      hlsRef.current?.destroy()
      hlsRef.current = null

      if (detail.streamMode === 'hls') {
        const url = `/api/listen/items/${detail.id}/hls/main.m3u8`
        if (shouldUseNativeHls(audio)) {
          startAtRef.current = resumeAt
          audio.src = url
        } else {
          const { default: HlsLib } = await import('hls.js')
          if (!HlsLib.isSupported()) throw new Error("This browser can't play converted books. Open it in Jellyfin instead.")
          if (currentRef.current?.id !== detail.id) return // switched books while the player code loaded
          // hls.js starts at the right place itself, so don't also seek on loadedmetadata.
          startAtRef.current = 0
          const hls = new HlsLib({ startPosition: resumeAt, maxBufferLength: 60, maxMaxBufferLength: 120 })
          hlsRef.current = hls
          hls.on(HlsLib.Events.ERROR, (_event, data) => {
            if (!data.fatal) return
            if (data.type === HlsLib.ErrorTypes.NETWORK_ERROR) hls.startLoad()
            else if (data.type === HlsLib.ErrorTypes.MEDIA_ERROR) hls.recoverMediaError()
            else {
              setStatus('error')
              setError("Couldn't play the converted audio. Open this book in Jellyfin instead.")
            }
          })
          hls.loadSource(url)
          hls.attachMedia(audio)
        }
      } else {
        startAtRef.current = resumeAt
        audio.src = `/api/listen/items/${detail.id}/stream`
      }

      applySpeed(chosenSpeed)
      audio.load()
      // Browsers reset the rate on load; set it again once the source is known.
      applySpeed(chosenSpeed)
    },
    [applySpeed],
  )

  const resume = useCallback(() => {
    void audioRef.current?.play().catch(() => {})
  }, [])

  const play = useCallback(
    async (itemId: string) => {
      const audio = audioRef.current
      if (!audio) return

      // Tapping the book that's already loaded just resumes it.
      if (currentRef.current?.id === itemId && audio.src) {
        if (audio.paused) resume()
        setExpanded(true)
        return
      }

      if (currentRef.current) {
        report('stop')
        stopHls(currentRef.current)
        // Forget the old book before pausing so the pause doesn't send one more progress report for it.
        currentRef.current = null
      }
      audio.pause()
      lastReportRef.current = 0
      trackerRef.current.reset()
      pausedAtRef.current = null
      setError(null)
      setStatus('loading')
      setSleep(null)
      setUpNext(null)
      setPosition(0)
      setDuration(0)

      let detail: ListenDetail
      try {
        detail = await api.get<ListenDetail>(`/api/listen/items/${itemId}`)
      } catch (err) {
        // No server (or no signal): a book saved on this device still plays.
        const saved = err instanceof ApiError ? null : await getOfflineMeta(itemId)
        if (!saved) {
          setCurrent(null)
          currentRef.current = null
          setStatus('error')
          setError(err instanceof ApiError ? err.message : "Couldn't load this book. Check your connection, or save books for offline listening.")
          return
        }
        detail = saved.detail
      }

      setCurrent(detail)
      currentRef.current = detail
      setDuration(detail.durationSeconds)
      setExpanded(true)

      if (!detail.canPlay) {
        setStatus('error')
        setError(detail.cantPlayReason ?? "This book can't be played here.")
        audio.removeAttribute('src')
        audio.load()
        return
      }

      // A place saved on this device that never reached Jellyfin is newer than Jellyfin's.
      const local = loadPending()[itemId]
      const known = local && local.position > 0 ? local.position : detail.positionSeconds
      const resumeAt = known > 0 && known < detail.durationSeconds - RESTART_WITHIN_SECONDS ? Math.max(0, known - prefsRef.current.resumeRewind) : 0
      startAtRef.current = resumeAt
      setPosition(resumeAt)

      const chosen = loadSpeed(detail.id)
      setSpeedState(chosen)
      sessionIdRef.current = newSessionId()
      try {
        await attachSource(detail, resumeAt, chosen)
        await audio.play()
      } catch (err) {
        if (err instanceof Error && err.name !== 'NotAllowedError' && err.name !== 'AbortError') {
          setStatus('error')
          setError(err.message)
        } else {
          // Autoplay can be refused (needs a tap); the player is loaded and shows the play button.
          setStatus('paused')
        }
      }

      // What comes after, for the "Up next" line and for auto-play. Quietly absent when there's no series or no signal.
      api
        .get<ListenItem | undefined>(`/api/listen/items/${detail.id}/next`)
        .then((next) => {
          if (currentRef.current?.id === detail.id) setUpNext(next ?? null)
        })
        .catch(() => {})
    },
    [attachSource, report, resume, stopHls],
  )

  useEffect(() => {
    playRef.current = play
  }, [play])

  const playNext = useCallback(() => {
    const next = upNextRef.current
    if (next) void playRef.current(next.id)
  }, [])

  const toggle = useCallback(() => {
    const audio = audioRef.current
    if (!audio || !currentRef.current?.canPlay) return
    if (audio.paused) resume()
    else audio.pause()
  }, [resume])

  const seek = useCallback((seconds: number) => {
    const audio = audioRef.current
    if (!audio) return
    const limit = Number.isFinite(audio.duration) ? audio.duration : (currentRef.current?.durationSeconds ?? seconds)
    audio.currentTime = Math.min(Math.max(0, seconds), limit)
    setPosition(audio.currentTime)
  }, [])

  const skip = useCallback(
    (delta: number) => {
      const audio = audioRef.current
      if (audio) seek(audio.currentTime + delta)
    },
    [seek],
  )

  const nextChapter = useCallback(() => {
    const item = currentRef.current
    const audio = audioRef.current
    if (!item || !audio) return
    const next = item.chapters.find((c) => c.startSeconds > audio.currentTime + 0.5)
    if (next) seek(next.startSeconds)
    else skip(prefsRef.current.skipForward)
  }, [seek, skip])

  const prevChapter = useCallback(() => {
    const item = currentRef.current
    const audio = audioRef.current
    if (!item || !audio) return
    const index = chapterIndexAt(item.chapters, audio.currentTime)
    if (index < 0) {
      seek(0)
      return
    }
    // Like a CD player: near the start of a chapter go to the one before, otherwise restart this one.
    const restart = audio.currentTime - item.chapters[index].startSeconds > 3
    seek(restart || index === 0 ? item.chapters[index].startSeconds : item.chapters[index - 1].startSeconds)
  }, [seek])

  const setSleepMinutes = useCallback((minutes: number | null) => {
    setSleep(minutes === null ? null : { kind: 'minutes', endsAt: Date.now() + minutes * 60_000 })
  }, [])

  const setSleepAtChapterEnd = useCallback(() => {
    const item = currentRef.current
    const audio = audioRef.current
    if (!item || !audio) return
    setSleep({ kind: 'chapter', chapterIndex: chapterIndexAt(item.chapters, audio.currentTime) })
  }, [])

  const close = useCallback(() => {
    const audio = audioRef.current
    if (currentRef.current) {
      report('stop')
      stopHls(currentRef.current)
    }
    currentRef.current = null
    audio?.pause()
    audio?.removeAttribute('src')
    audio?.load()
    setCurrent(null)
    setStatus('idle')
    setPosition(0)
    setDuration(0)
    setSleep(null)
    setUpNext(null)
    setExpanded(false)
    setError(null)
  }, [report, stopHls])

  // ---- audio element events ----

  useEffect(() => {
    const audio = audioRef.current
    if (!audio) return
    const tracker = trackerRef.current

    const onLoadedMetadata = () => {
      if (Number.isFinite(audio.duration)) setDuration(audio.duration)
      if (startAtRef.current > 0) {
        audio.currentTime = startAtRef.current
        startAtRef.current = 0
      }
      applySpeed(speedRef.current)
    }
    const onPlay = () => {
      setStatus('playing')
      setError(null)
      tracker.breakSpan()

      // Step back a little after a pause so you hear the sentence you left in the middle of. Done here, on the audio
      // element's own play event, because play can start without our code: the phone's lock screen resumes the audio
      // itself (see player/mediaSession.ts), and this still applies when the page wakes up.
      const pausedFor = pausedAtRef.current === null ? 0 : Date.now() - pausedAtRef.current
      pausedAtRef.current = null
      const back = smartRewindSeconds(prefsRef.current.resumeRewind, pausedFor)
      if (back > 0 && audio.currentTime > 0) audio.currentTime = Math.max(0, audio.currentTime - back)
    }
    const onPlaying = () => {
      setBuffering(false)
      setStatus('playing')
      pausedAtRef.current = null
      tracker.breakSpan()
      if (Date.now() - lastReportRef.current > 1000) report(lastReportRef.current === 0 ? 'start' : 'progress')
    }
    const onPause = () => {
      tracker.breakSpan()
      if (!currentRef.current || audio.ended) return
      pausedAtRef.current = Date.now()
      setStatus('paused')
      report('progress')
    }
    const onWaiting = () => setBuffering(true)
    const onSeek = () => {
      tracker.breakSpan()
      // Moving the playhead while paused is choosing where to start: don't step back from the chosen place.
      if (audio.paused) pausedAtRef.current = null
    }
    const onTimeUpdate = () => {
      setPosition(audio.currentTime)
      const now = Date.now()
      if (!audio.paused && currentRef.current) {
        tracker.sample(audio.currentTime, performance.now(), speedRef.current)
        if (now - lastPendingSaveRef.current > PENDING_SAVE_EVERY_MS) {
          lastPendingSaveRef.current = now
          savePending()
        }
      }
      const rule = sleepRef.current
      const item = currentRef.current
      if (rule?.kind === 'chapter' && item && chapterIndexAt(item.chapters, audio.currentTime) !== rule.chapterIndex) {
        audio.pause()
        setSleep(null)
      }
    }
    const onEnded = () => {
      const item = currentRef.current
      setStatus('paused')
      if (!item) return
      api.post(`/api/listen/items/${item.id}/played`).catch(() => {})

      // Straight on to the next book in the series, if there is one and the person wants that. Starting it reports
      // this book as stopped, so only report here when nothing follows.
      const next = upNextRef.current
      if (next && prefsRef.current.autoPlayNext) {
        setNotice(`Finished ${item.title}. Playing the next one: ${next.title}`)
        void playRef.current(next.id)
      } else {
        report('stop')
      }
    }
    const onError = async () => {
      const item = currentRef.current
      if (!item || !audio.src) return
      setStatus('error')
      // The audio element hides the HTTP status; ask the API to find out whether the sign-in is the problem.
      try {
        await api.get(`/api/listen/items/${item.id}`)
        setError("Couldn't play this book. Check your connection and try again.")
      } catch (err) {
        setError(err instanceof ApiError ? err.message : "Couldn't play this book. Check your connection and try again.")
      }
    }

    audio.addEventListener('loadedmetadata', onLoadedMetadata)
    audio.addEventListener('play', onPlay)
    audio.addEventListener('playing', onPlaying)
    audio.addEventListener('pause', onPause)
    audio.addEventListener('waiting', onWaiting)
    audio.addEventListener('seeking', onSeek)
    audio.addEventListener('seeked', onSeek)
    audio.addEventListener('timeupdate', onTimeUpdate)
    audio.addEventListener('ended', onEnded)
    audio.addEventListener('error', onError)
    return () => {
      audio.removeEventListener('loadedmetadata', onLoadedMetadata)
      audio.removeEventListener('play', onPlay)
      audio.removeEventListener('playing', onPlaying)
      audio.removeEventListener('pause', onPause)
      audio.removeEventListener('waiting', onWaiting)
      audio.removeEventListener('seeking', onSeek)
      audio.removeEventListener('seeked', onSeek)
      audio.removeEventListener('timeupdate', onTimeUpdate)
      audio.removeEventListener('ended', onEnded)
      audio.removeEventListener('error', onError)
    }
  }, [applySpeed, report, savePending])

  // Tell Jellyfin where we are every few seconds while playing.
  useEffect(() => {
    if (status !== 'playing') return
    const id = window.setInterval(() => report('progress'), REPORT_EVERY_MS)
    return () => window.clearInterval(id)
  }, [status, report])

  // Deliver anything an earlier visit couldn't (no signal, closed tab), now and whenever the connection returns.
  useEffect(() => {
    let cancelled = false
    const flush = async () => {
      if (navigator.onLine === false) return
      const pending = loadPending()
      for (const [itemId, e] of Object.entries(pending)) {
        if (cancelled || currentRef.current?.id === itemId) continue
        const query = e.mediaSourceId ? `?mediaSourceId=${encodeURIComponent(e.mediaSourceId)}` : ''
        try {
          await api.post(`/api/listen/items/${itemId}/progress${query}`, {
            positionSeconds: e.position,
            isPaused: true,
            event: 'progress',
            listenedSeconds: e.content,
            wallSeconds: e.wall,
            localDate: e.localDate,
            title: e.title,
            author: e.author,
          })
          updatePending(itemId, null)
        } catch (err) {
          // A book that's gone from the library (404) can never be delivered; anything else is retried later.
          if (err instanceof ApiError && err.status === 404) updatePending(itemId, null)
        }
      }
      // Bookmarks made with no signal go out the same way.
      if (!cancelled) await flushPendingBookmarks()
    }
    void flush()
    window.addEventListener('online', flush)
    return () => {
      cancelled = true
      window.removeEventListener('online', flush)
    }
  }, [])

  // Sleep timer by clock time.
  useEffect(() => {
    if (sleep?.kind !== 'minutes') return
    const id = window.setInterval(() => {
      if (Date.now() >= sleep.endsAt) {
        audioRef.current?.pause()
        setSleep(null)
      }
    }, 1000)
    return () => window.clearInterval(id)
  }, [sleep])

  // Let the "next book started" message fade by itself.
  useEffect(() => {
    if (!notice) return
    const id = window.setTimeout(() => setNotice(null), NOTICE_MS)
    return () => window.clearTimeout(id)
  }, [notice])

  // Last chance to save the place: tab hidden/closed, or the shell unmounting (sign out).
  useEffect(() => {
    const flush = () => {
      const item = currentRef.current
      const audio = audioRef.current
      if (!item || !audio) return
      savePending()
      reportPlaceWithKeepalive(item.id, audio.currentTime, audio.paused, item.mediaSourceId, sessionIdRef.current)
    }
    const onVisibility = () => {
      if (document.visibilityState === 'hidden') flush()
    }
    window.addEventListener('pagehide', flush)
    document.addEventListener('visibilitychange', onVisibility)
    const audio = audioRef.current
    const hlsAtMount = hlsRef
    return () => {
      window.removeEventListener('pagehide', flush)
      document.removeEventListener('visibilitychange', onVisibility)
      if (currentRef.current) {
        savePending()
        reportPlaceWithKeepalive(currentRef.current.id, audio?.currentTime ?? 0, true, currentRef.current.mediaSourceId, sessionIdRef.current)
      }
      hlsAtMount.current?.destroy()
      audio?.pause()
    }
  }, [savePending])

  // Lock-screen / headset / notification controls.
  useEffect(() => {
    if (!('mediaSession' in navigator)) return
    if (!current) {
      navigator.mediaSession.metadata = null
      return
    }
    navigator.mediaSession.metadata = new MediaMetadata({
      title: current.title,
      artist: current.author ?? '',
      album: 'Audiobook',
      artwork: current.hasCover
        ? [
            { src: `/api/listen/items/${current.id}/image?w=256`, sizes: '256x256', type: 'image/jpeg' },
            { src: `/api/listen/items/${current.id}/image?w=512`, sizes: '512x512', type: 'image/jpeg' },
          ]
        : [],
    })
  }, [current])

  useEffect(() => {
    if (!('mediaSession' in navigator)) return
    // Play and pause are left to the phone on purpose; see player/mediaSession.ts.
    return applyLockScreenControls(navigator.mediaSession, { skip, seek, prevChapter, nextChapter, skipBack: prefs.skipBack, skipForward: prefs.skipForward })
  }, [skip, seek, prevChapter, nextChapter, prefs.skipBack, prefs.skipForward])

  useEffect(() => {
    if (!('mediaSession' in navigator) || !current) return
    navigator.mediaSession.playbackState = status === 'playing' ? 'playing' : 'paused'
  }, [status, current])

  useEffect(() => {
    if (!('mediaSession' in navigator) || !current || !(duration > 0)) return
    try {
      navigator.mediaSession.setPositionState({ duration, playbackRate: speed, position: Math.min(position, duration) })
    } catch {
      // Some browsers reject unusual values; the lock screen just won't show a scrubber.
    }
  }, [current, duration, speed, position])

  // Pages need room for the mini player above the tab bar.
  useEffect(() => {
    document.body.classList.toggle('has-player', current !== null)
    return () => document.body.classList.remove('has-player')
  }, [current])

  const value = useMemo<PlayerContextValue>(
    () => ({
      current,
      status,
      buffering,
      position,
      duration,
      speed,
      error,
      sleep,
      expanded,
      chapterIndex,
      prefs,
      sync,
      upNext,
      notice,
      play,
      playNext,
      toggle,
      seek,
      skip,
      setSpeed,
      bumpSpeed,
      nextChapter,
      prevChapter,
      setSleepMinutes,
      setSleepAtChapterEnd,
      setExpanded,
      setPrefs,
      close,
    }),
    [
      current, status, buffering, position, duration, speed, error, sleep, expanded, chapterIndex, prefs, sync, upNext, notice,
      play, playNext, toggle, seek, skip, setSpeed, bumpSpeed, nextChapter, prevChapter, setSleepMinutes, setSleepAtChapterEnd, setPrefs, close,
    ],
  )

  return (
    <PlayerContext.Provider value={value}>
      {children}
      <audio ref={audioRef} preload="metadata" />
    </PlayerContext.Provider>
  )
}
