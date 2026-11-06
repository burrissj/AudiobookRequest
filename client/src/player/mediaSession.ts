/**
 * The lock-screen / headset / notification-shade controls.
 *
 * Play and pause are deliberately NOT here. When a handler is registered for them, the browser asks the page's
 * JavaScript to do the work -- and on iPhone, once playback has been paused with the screen locked, iOS puts the
 * page to sleep, so Play on the lock screen does nothing until the phone is unlocked. With no handler the phone
 * resumes the audio element itself, with no JavaScript involved, which works from the lock screen. The page still
 * learns about it (the audio element's play/pause events fire) and does its own bookkeeping then.
 */
export interface LockScreenControls {
  skip: (deltaSeconds: number) => void
  seek: (seconds: number) => void
  prevChapter: () => void
  nextChapter: () => void
  skipBack: number
  skipForward: number
}

type Handler = (details: MediaSessionActionDetails) => void

export function lockScreenHandlers(c: LockScreenControls): [MediaSessionAction, Handler][] {
  return [
    ['seekbackward', (d) => c.skip(-(d.seekOffset ?? c.skipBack))],
    ['seekforward', (d) => c.skip(d.seekOffset ?? c.skipForward)],
    ['seekto', (d) => {
      if (d.seekTime !== undefined) c.seek(d.seekTime)
    }],
    ['previoustrack', () => c.prevChapter()],
    ['nexttrack', () => c.nextChapter()],
  ]
}

interface SessionLike {
  setActionHandler: (action: MediaSessionAction, handler: Handler | null) => void
}

/** Registers the handlers (skipping any action this browser doesn't know) and returns a function that removes them. */
export function applyLockScreenControls(session: SessionLike, controls: LockScreenControls): () => void {
  const handlers = lockScreenHandlers(controls)
  for (const [action, handler] of handlers) {
    try {
      session.setActionHandler(action, handler)
    } catch {
      // Not every browser supports every action.
    }
  }
  return () => {
    for (const [action] of handlers) {
      try {
        session.setActionHandler(action, null)
      } catch {
        // ignore
      }
    }
  }
}
