import { describe, expect, it } from 'vitest'
import { formatNextCheck, formatReleaseDay, isUnreleased, laterRelease, releaseDay, releaseInstant } from './release'

const NOW = new Date('2026-10-28T12:00:00Z')

describe('releaseDay', () => {
  it('reads the calendar day from either a plain date or a full timestamp', () => {
    expect(releaseDay('2026-11-05')).toBe('2026-11-05')
    expect(releaseDay('2026-11-05T00:00:00+00:00')).toBe('2026-11-05')
  })

  it('has no day for nothing or for rubbish', () => {
    expect(releaseDay(null)).toBeNull()
    expect(releaseDay(undefined)).toBeNull()
    expect(releaseDay('')).toBeNull()
    expect(releaseDay('soon')).toBeNull()
  })
})

describe('isUnreleased', () => {
  it('is true only while the release day is still ahead', () => {
    expect(isUnreleased('2026-10-29', NOW)).toBe(true)
    expect(isUnreleased('2026-10-28', NOW)).toBe(false) // out from midnight UTC on its day, as on the server
    expect(isUnreleased('2026-10-01', NOW)).toBe(false)
  })

  it('treats a missing or garbled date as released, so it never blocks a request', () => {
    expect(isUnreleased(null, NOW)).toBe(false)
    expect(isUnreleased('later', NOW)).toBe(false)
  })

  it('does not slip a day for a timestamp at midnight UTC', () => {
    expect(isUnreleased('2026-10-29T00:00:00+00:00', NOW)).toBe(true)
    expect(isUnreleased('2026-10-28T00:00:00+00:00', NOW)).toBe(false)
  })
})

describe('releaseInstant', () => {
  it('turns a day into the midnight-UTC instant the server reads', () => {
    expect(releaseInstant('2026-11-05')).toBe('2026-11-05T00:00:00Z')
    expect(releaseInstant('2026-11-05T09:30:00-05:00')).toBe('2026-11-05T00:00:00Z')
    expect(releaseInstant(null)).toBeNull()
  })
})

describe('laterRelease', () => {
  it('prefers the later date, since the audiobook can follow the print edition', () => {
    expect(laterRelease('2026-09-01T00:00:00+00:00', '2026-12-01')).toBe('2026-12-01')
    expect(laterRelease('2027-02-01', '2026-12-01')).toBe('2027-02-01')
  })

  it('uses whichever date exists', () => {
    expect(laterRelease(null, '2026-12-01')).toBe('2026-12-01')
    expect(laterRelease('2026-12-01', undefined)).toBe('2026-12-01')
    expect(laterRelease(null, null)).toBeNull()
  })
})

describe('formatReleaseDay', () => {
  it('leaves the year off for this year and adds it for another', () => {
    expect(formatReleaseDay('2026-11-05', NOW)).toBe('Nov 5')
    expect(formatReleaseDay('2027-01-05T00:00:00Z', NOW)).toBe('Jan 5, 2027')
    expect(formatReleaseDay(null, NOW)).toBe('')
  })
})

describe('formatNextCheck', () => {
  it('says shortly when the check is due, and otherwise when', () => {
    expect(formatNextCheck(null, NOW)).toBe('')
    expect(formatNextCheck('2026-10-28T11:00:00Z', NOW)).toBe('shortly')
    expect(formatNextCheck('2026-10-28T12:30:00Z', NOW)).toMatch(/^today around/)
    expect(formatNextCheck('2026-11-20T12:00:00Z', NOW)).toBe('Nov 20')
  })
})
