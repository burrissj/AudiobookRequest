import { describe, expect, it, vi } from 'vitest'
import playerSource from '../state/PlayerContext.tsx?raw'
import { applyLockScreenControls, lockScreenHandlers, type LockScreenControls } from './mediaSession'

function controls(): LockScreenControls & { calls: string[] } {
  const calls: string[] = []
  return {
    calls,
    skip: (d) => calls.push(`skip ${d}`),
    seek: (s) => calls.push(`seek ${s}`),
    prevChapter: () => calls.push('prev'),
    nextChapter: () => calls.push('next'),
    skipBack: 15,
    skipForward: 30,
  }
}

describe('lock screen controls', () => {
  it('leave play and pause to the phone', () => {
    // A JavaScript handler for play can't run on an iPhone whose page has gone to sleep (paused, screen locked),
    // so Play on the lock screen would do nothing until the phone was unlocked. With none, iOS resumes the audio itself.
    const actions = lockScreenHandlers(controls()).map(([action]) => action)

    expect(actions).not.toContain('play')
    expect(actions).not.toContain('pause')
    expect(actions.sort()).toEqual(['nexttrack', 'previoustrack', 'seekbackward', 'seekforward', 'seekto'])
  })

  it('is never put back by the player itself', () => {
    expect(playerSource).not.toMatch(/setActionHandler\(\s*['"](play|pause)['"]/)
    expect(playerSource).not.toMatch(/\[\s*['"](play|pause)['"]\s*,/)
    expect(playerSource).toContain('applyLockScreenControls')
  })

  it('skip by the offset the phone asks for, else by the chosen skip length', () => {
    const c = controls()
    const handler = (name: string) => lockScreenHandlers(c).find(([a]) => a === name)![1]

    handler('seekbackward')({ action: 'seekbackward', seekOffset: 10 })
    handler('seekbackward')({ action: 'seekbackward' })
    handler('seekforward')({ action: 'seekforward', seekOffset: 5 })
    handler('seekforward')({ action: 'seekforward' })

    expect(c.calls).toEqual(['skip -10', 'skip -15', 'skip 5', 'skip 30'])
  })

  it('seek to the place dragged to on the lock screen, and ignore a drag with no place', () => {
    const c = controls()
    const seekto = lockScreenHandlers(c).find(([a]) => a === 'seekto')![1]

    seekto({ action: 'seekto', seekTime: 1234.5 })
    seekto({ action: 'seekto' })
    seekto({ action: 'seekto', seekTime: 0 })

    expect(c.calls).toEqual(['seek 1234.5', 'seek 0'])
  })

  it('move between chapters with the track buttons', () => {
    const c = controls()
    const find = (n: string) => lockScreenHandlers(c).find(([a]) => a === n)![1]

    find('previoustrack')({ action: 'previoustrack' })
    find('nexttrack')({ action: 'nexttrack' })

    expect(c.calls).toEqual(['prev', 'next'])
  })
})

describe('applyLockScreenControls', () => {
  it('registers each handler and removes them all on cleanup', () => {
    const setActionHandler = vi.fn()

    const cleanup = applyLockScreenControls({ setActionHandler }, controls())
    expect(setActionHandler.mock.calls.map((c) => c[0]).sort()).toEqual(['nexttrack', 'previoustrack', 'seekbackward', 'seekforward', 'seekto'])
    expect(setActionHandler.mock.calls.every((c) => typeof c[1] === 'function')).toBe(true)

    setActionHandler.mockClear()
    cleanup()
    expect(setActionHandler).toHaveBeenCalledTimes(5)
    expect(setActionHandler.mock.calls.every((c) => c[1] === null)).toBe(true)
  })

  it('carries on when a browser does not know an action', () => {
    const setActionHandler = vi.fn((action: string) => {
      if (action === 'seekto') throw new TypeError('not supported')
    })

    expect(() => applyLockScreenControls({ setActionHandler }, controls())).not.toThrow()
    expect(setActionHandler).toHaveBeenCalledTimes(5)
  })
})
