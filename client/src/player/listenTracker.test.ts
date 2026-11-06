import { describe, expect, it } from 'vitest'
import { ListenTracker, listenDelta, localDateString } from './listenTracker'

describe('listenDelta', () => {
  it('counts ordinary listening at normal speed', () => {
    expect(listenDelta(100, 0, 100.5, 500, 1)).toEqual({ content: 0.5, wall: 0.5 })
  })

  it('counts faster listening as more book per second', () => {
    expect(listenDelta(100, 0, 101.5, 1000, 1.5)).toEqual({ content: 1.5, wall: 1 })
  })

  it('ignores a seek forward', () => {
    expect(listenDelta(100, 0, 2000, 500, 1)).toBeNull()
  })

  it('ignores a seek backward', () => {
    expect(listenDelta(100, 0, 40, 500, 1)).toBeNull()
  })

  it('ignores long gaps, such as a frozen background tab', () => {
    expect(listenDelta(100, 0, 160, 60_000, 1)).toBeNull()
  })

  it('ignores a clock that did not advance', () => {
    expect(listenDelta(100, 1000, 100.2, 1000, 1)).toBeNull()
  })

  it('does not credit more than the playback speed allows', () => {
    // 10 seconds of book in 1 second of real time is not 3x listening.
    expect(listenDelta(0, 0, 10, 1000, 3)).toBeNull()
  })
})

describe('ListenTracker', () => {
  it('accumulates samples and skips jumps', () => {
    const t = new ListenTracker()
    t.sample(0, 0, 1)
    t.sample(0.25, 250, 1)
    t.sample(0.5, 500, 1)
    t.sample(900, 750, 1) // seek
    t.sample(900.25, 1000, 1)
    expect(t.content).toBeCloseTo(0.75)
    expect(t.wall).toBeCloseTo(0.75)
  })

  it('does not count across a broken span (pause, seek, new book)', () => {
    const t = new ListenTracker()
    t.sample(10, 0, 1)
    t.breakSpan()
    t.sample(10.5, 500, 1) // the first sample after a break only starts a new span
    expect(t.content).toBe(0)
    t.sample(11, 1000, 1)
    expect(t.content).toBeCloseTo(0.5)
  })

  it('subtracts only what was delivered, keeping what arrived meanwhile', () => {
    const t = new ListenTracker()
    t.sample(0, 0, 1)
    t.sample(1, 1000, 1)
    const sent = t.snapshot()
    t.sample(2, 2000, 1) // more listening while the report was in flight
    t.subtract(sent)
    expect(t.content).toBeCloseTo(1)
    expect(t.wall).toBeCloseTo(1)
  })

  it('keeps everything when a report fails (nothing is subtracted)', () => {
    const t = new ListenTracker()
    t.sample(0, 0, 1)
    t.sample(1, 1000, 1)
    t.snapshot()
    expect(t.content).toBeCloseTo(1)
  })

  it('never goes negative', () => {
    const t = new ListenTracker()
    t.subtract({ content: 5, wall: 5 })
    expect(t.content).toBe(0)
    expect(t.wall).toBe(0)
  })
})

describe('localDateString', () => {
  it('formats the local calendar day with zero padding', () => {
    expect(localDateString(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05')
    expect(localDateString(new Date(2026, 11, 31, 0, 1))).toBe('2026-12-31')
  })
})
