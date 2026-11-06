import { createContext, useContext } from 'react'
import type { ListenDetail, ListenItem } from '../api/types'
import type { PlayerPrefs } from '../player/prefs'

export type PlayerStatus = 'idle' | 'loading' | 'playing' | 'paused' | 'error'

/** "minutes" ends at a clock time; "chapter" pauses when the current chapter finishes. */
export type SleepTimer = { kind: 'minutes'; endsAt: number } | { kind: 'chapter'; chapterIndex: number } | null

/** Whether the person's place and listening time are reaching Jellyfin. */
export interface SyncState {
  status: 'idle' | 'ok' | 'error'
  /** When the last report got through. */
  at: number | null
  message: string | null
}

export interface PlayerContextValue {
  current: ListenDetail | null
  status: PlayerStatus
  buffering: boolean
  position: number
  duration: number
  speed: number
  error: string | null
  sleep: SleepTimer
  expanded: boolean
  chapterIndex: number
  prefs: PlayerPrefs
  sync: SyncState
  /** The next unfinished book of the series, if there is one. */
  upNext: ListenItem | null
  /** A short message about something that just happened by itself (the next book starting). */
  notice: string | null
  play: (itemId: string) => Promise<void>
  playNext: () => void
  toggle: () => void
  seek: (seconds: number) => void
  skip: (deltaSeconds: number) => void
  setSpeed: (speed: number) => void
  bumpSpeed: (direction: 1 | -1) => void
  nextChapter: () => void
  prevChapter: () => void
  setSleepMinutes: (minutes: number | null) => void
  setSleepAtChapterEnd: () => void
  setExpanded: (expanded: boolean) => void
  setPrefs: (patch: Partial<PlayerPrefs>) => void
  close: () => void
}

export const PlayerContext = createContext<PlayerContextValue | undefined>(undefined)

export function usePlayer() {
  const ctx = useContext(PlayerContext)
  if (!ctx) throw new Error('usePlayer must be used within a PlayerProvider')
  return ctx
}
