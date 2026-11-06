import { describe, expect, it } from 'vitest'
import { DEFAULT_PREFS, parsePrefs, smartRewindSeconds } from './prefs'

describe('parsePrefs', () => {
  it('uses defaults when nothing is saved or the data is junk', () => {
    expect(parsePrefs(null)).toEqual(DEFAULT_PREFS)
    expect(parsePrefs('not json')).toEqual(DEFAULT_PREFS)
    expect(parsePrefs('42')).toEqual(DEFAULT_PREFS)
  })

  it('keeps valid choices and drops values that are not on offer', () => {
    const p = parsePrefs(JSON.stringify({ skipBack: 45, skipForward: 7, resumeRewind: 20, autoPlayNext: false }))
    expect(p).toEqual({ skipBack: 45, skipForward: DEFAULT_PREFS.skipForward, resumeRewind: 20, autoPlayNext: false })
  })

  it('ignores values of the wrong type', () => {
    const p = parsePrefs(JSON.stringify({ skipBack: '30', autoPlayNext: 'yes' }))
    expect(p).toEqual(DEFAULT_PREFS)
  })
})

describe('smartRewindSeconds', () => {
  it('does nothing for a brief pause', () => {
    expect(smartRewindSeconds(10, 5_000)).toBe(0)
    expect(smartRewindSeconds(10, 14_999)).toBe(0)
  })

  it('steps back the chosen amount after a short break', () => {
    expect(smartRewindSeconds(10, 15_000)).toBe(10)
    expect(smartRewindSeconds(10, 4 * 60_000)).toBe(10)
  })

  it('steps back three times as far after a long absence, capped at a minute', () => {
    expect(smartRewindSeconds(10, 5 * 60_000)).toBe(30)
    expect(smartRewindSeconds(30, 60 * 60_000)).toBe(60)
  })

  it('does nothing when switched off', () => {
    expect(smartRewindSeconds(0, 60 * 60_000)).toBe(0)
  })
})
