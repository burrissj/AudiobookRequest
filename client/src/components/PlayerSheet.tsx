import { useState } from 'react'
import { usePlayer, type SyncState } from '../state/usePlayer'
import { chapterIndexAt, formatClock, formatDuration, formatSpeed, MAX_SPEED, MIN_SPEED, remainingAtSpeed } from '../player/playerMath'
import { usePersonalBook } from '../hooks/usePersonalBook'
import { BookPersonal } from './BookPersonal'
import { DownloadButton } from './DownloadButton'
import { ListenButton } from './JellyfinLinks'
import { ListenCover } from './ListenCover'
import { MarqueeText } from './MarqueeText'
import {
  ChevronDownIcon,
  NextChapterIcon,
  PauseIcon,
  PlayIcon,
  PrevChapterIcon,
  SkipBackIcon,
  SkipForwardIcon,
} from './PlayerIcons'
import { PlayerPrefsForm } from './PlayerPrefsForm'
import { SpeedPanel } from './SpeedPanel'
import { StarRating } from './StarRating'

const SLEEP_OPTIONS = [15, 30, 45, 60]

function sleepLabel(endsAt: number): string {
  const minutes = Math.max(0, Math.ceil((endsAt - Date.now()) / 60_000))
  return `${minutes} min left`
}

function syncText(sync: SyncState): { text: string; ok: boolean } | null {
  if (sync.status === 'ok' && sync.at !== null) {
    const seconds = Math.max(0, Math.round((Date.now() - sync.at) / 1000))
    const ago = seconds < 10 ? 'just now' : seconds < 60 ? `${seconds}s ago` : `${Math.round(seconds / 60)} min ago`
    return { text: `Synced with Jellyfin ${ago}`, ok: true }
  }
  if (sync.status === 'error') return { text: sync.message ?? "Couldn't sync with Jellyfin.", ok: false }
  return null
}

/** The mini bar above the tab bar, and the full-screen player it opens. Lives in the app shell so audio survives navigation. */
export function PlayerSheet() {
  const p = usePlayer()
  const [scrub, setScrub] = useState<number | null>(null)
  const [speedOpen, setSpeedOpen] = useState(false)

  const { current } = p
  // Your own rating, tags and bookmarks for this book. Fetched once here and shared by the pieces below.
  const mine = usePersonalBook(current?.id ?? null)
  if (!current) return null

  const playing = p.status === 'playing'
  const hasError = p.status === 'error'
  const shown = scrub ?? p.position
  const max = Math.max(p.duration, 1)
  const speedLabel = formatSpeed(p.speed)

  const commitScrub = () => {
    if (scrub !== null) p.seek(scrub)
    setScrub(null)
  }

  const sleepValue = p.sleep === null ? 'off' : p.sleep.kind === 'chapter' ? 'chapter' : 'custom'

  function onSleepChange(value: string) {
    if (value === 'off') p.setSleepMinutes(null)
    else if (value === 'chapter') p.setSleepAtChapterEnd()
    else p.setSleepMinutes(Number(value))
  }

  if (!p.expanded) {
    return (
      <div className="mini-player">
        {p.notice && <div className="player-notice mini-notice">{p.notice}</div>}
        <button className="mini-main" onClick={() => p.setExpanded(true)} aria-label={`Open player for ${current.title}`}>
          <ListenCover id={current.id} hasCover={current.hasCover} title={current.title} width={96} className="mini-cover" />
          <span className="mini-text">
            <MarqueeText as="strong" text={current.title} />
            <small>{hasError ? 'Cannot play' : `${formatClock(p.position)} · ${speedLabel}`}</small>
          </span>
        </button>
        {!hasError && (
          <button className="mini-play" onClick={p.toggle} aria-label={playing ? 'Pause' : 'Play'}>
            {playing ? <PauseIcon /> : <PlayIcon />}
          </button>
        )}
        <div className="mini-progress" aria-hidden="true">
          <div className="mini-progress-fill" style={{ width: `${Math.min(100, (p.position / max) * 100)}%` }} />
        </div>
      </div>
    )
  }

  const chapter = chapterIndexAt(current.chapters, p.position)
  const sync = syncText(p.sync)

  return (
    <div className="player-sheet" role="dialog" aria-label="Player">
      <div className="player-top">
        <button className="player-icon-btn" onClick={() => p.setExpanded(false)} aria-label="Minimise player">
          <ChevronDownIcon />
        </button>
        <span className="player-top-title">Now playing</span>
        <button className="player-close" onClick={p.close}>
          Stop
        </button>
      </div>

      {p.notice && <div className="player-notice">{p.notice}</div>}

      <ListenCover id={current.id} hasCover={current.hasCover} title={current.title} width={600} className="player-cover" />
      <h2 className="player-title">{current.title}</h2>
      {current.author && <p className="player-author">{current.author}</p>}
      {chapter >= 0 && <p className="player-chapter">{current.chapters[chapter].name}</p>}
      <StarRating value={mine.personal.rating} onChange={(n) => void mine.setRating(n)} disabled={!mine.loaded} />

      {hasError && <p className="error player-error">{p.error}</p>}
      {hasError && !current.canPlay && (
        <div className="player-fallback">
          <ListenButton title={current.title} author={current.author ?? ''} label="Open in Jellyfin" className="primary" />
        </div>
      )}

      {!hasError && (
        <>
          <input
            className="player-seek"
            type="range"
            min={0}
            max={max}
            step={1}
            value={Math.min(shown, max)}
            onChange={(e) => setScrub(Number(e.target.value))}
            onPointerUp={commitScrub}
            onKeyUp={commitScrub}
            onBlur={commitScrub}
            aria-label="Seek"
            aria-valuetext={`${formatClock(shown)} of ${formatClock(p.duration)}`}
          />
          <div className="player-times">
            <span>{formatClock(shown)}</span>
            {p.buffering && <span className="player-buffering">Buffering…</span>}
            <span>-{formatClock(remainingAtSpeed(p.duration, shown, p.speed))}</span>
          </div>

          <div className="player-controls">
            <button className="player-icon-btn" onClick={p.prevChapter} aria-label="Previous chapter">
              <PrevChapterIcon />
            </button>
            <button className="player-icon-btn" onClick={() => p.skip(-p.prefs.skipBack)} aria-label={`Back ${p.prefs.skipBack} seconds`}>
              <SkipBackIcon seconds={p.prefs.skipBack} />
            </button>
            <button className="player-play" onClick={p.toggle} aria-label={playing ? 'Pause' : 'Play'}>
              {playing ? <PauseIcon size={34} /> : <PlayIcon size={34} />}
            </button>
            <button className="player-icon-btn" onClick={() => p.skip(p.prefs.skipForward)} aria-label={`Forward ${p.prefs.skipForward} seconds`}>
              <SkipForwardIcon seconds={p.prefs.skipForward} />
            </button>
            <button className="player-icon-btn" onClick={p.nextChapter} aria-label="Next chapter">
              <NextChapterIcon />
            </button>
          </div>

          <div className="player-speed" role="group" aria-label="Playback speed">
            <button onClick={() => p.bumpSpeed(-1)} disabled={p.speed <= MIN_SPEED} aria-label="Slower by 0.25">
              −
            </button>
            <button className="player-speed-value" onClick={() => setSpeedOpen(true)} aria-label={`Speed ${speedLabel}. Choose a speed`} aria-haspopup="dialog">
              {speedLabel}
            </button>
            <button onClick={() => p.bumpSpeed(1)} disabled={p.speed >= MAX_SPEED} aria-label="Faster by 0.25">
              +
            </button>
          </div>
          <p className="player-speed-note">
            {p.speed === 1
              ? `${formatDuration(Math.max(0, p.duration - shown))} left`
              : `${formatDuration(remainingAtSpeed(p.duration, shown, p.speed))} left at ${speedLabel}`}
          </p>

          <label className="player-sleep">
            Sleep timer
            <select value={sleepValue} onChange={(e) => onSleepChange(e.target.value)}>
              <option value="off">Off</option>
              {SLEEP_OPTIONS.map((m) => (
                <option key={m} value={m}>
                  {m} minutes
                </option>
              ))}
              <option value="chapter">End of chapter</option>
              {p.sleep?.kind === 'minutes' && (
                <option value="custom" disabled>
                  {sleepLabel(p.sleep.endsAt)}
                </option>
              )}
            </select>
          </label>

          <div className="player-extras">
            <span className="player-extras-label">Listen offline</span>
            {current.canDownload ? (
              <DownloadButton itemId={current.id} title={current.title} canDownload />
            ) : (
              <small>Converted books can’t be saved</small>
            )}
          </div>

          {p.upNext && (
            <div className="player-upnext">
              <div>
                <small>Up next in this series</small>
                <MarqueeText as="strong" text={p.upNext.title} />
              </div>
              <button onClick={p.playNext}>Play now</button>
            </div>
          )}

          {sync && <p className={`player-sync${sync.ok ? '' : ' player-sync-bad'}`}>{sync.text}</p>}
        </>
      )}

      {!hasError && <BookPersonal state={mine} />}

      {current.chapters.length > 1 && !hasError && (
        <>
          <h3 className="player-chapters-title">Chapters</h3>
          <ol className="player-chapters">
            {current.chapters.map((c, i) => (
              <li key={`${i}-${c.startSeconds}`}>
                <button className={i === chapter ? 'active' : ''} onClick={() => p.seek(c.startSeconds)}>
                  <span>{c.name}</span>
                  <small>{formatClock(c.startSeconds)}</small>
                </button>
              </li>
            ))}
          </ol>
        </>
      )}

      {speedOpen && <SpeedPanel speed={p.speed} onChange={p.setSpeed} onClose={() => setSpeedOpen(false)} />}

      <details className="player-prefs">
        <summary>Player settings</summary>
        <PlayerPrefsForm />
      </details>
    </div>
  )
}
