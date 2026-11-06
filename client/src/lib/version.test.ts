import { describe, expect, it } from 'vitest'
import { compareVersions, entriesToShow } from './version'

const log = [{ version: '0.10.0' }, { version: '0.9.0' }, { version: '0.8.0' }, { version: '0.7.1' }, { version: '0.7.0' }]

describe('compareVersions', () => {
  it('compares numerically, not as text', () => {
    expect(compareVersions('0.10.0', '0.9.0')).toBe(1)
    expect(compareVersions('0.9.0', '0.10.0')).toBe(-1)
    expect(compareVersions('1.0', '1.0.0')).toBe(0)
  })
})

describe('entriesToShow', () => {
  it('shows only the latest release to someone who has never seen the notice', () => {
    expect(entriesToShow(log, null, '0.10.0').map((e) => e.version)).toEqual(['0.10.0'])
  })

  it('shows everything newer than what was last seen, newest first', () => {
    expect(entriesToShow(log, '0.8.0', '0.10.0').map((e) => e.version)).toEqual(['0.10.0', '0.9.0'])
  })

  it('shows nothing when up to date', () => {
    expect(entriesToShow(log, '0.10.0', '0.10.0')).toEqual([])
  })

  it('shows nothing after a downgrade', () => {
    expect(entriesToShow(log, '0.11.0', '0.10.0')).toEqual([])
  })

  it('caps how many it shows', () => {
    expect(entriesToShow(log, '0.1.0', '0.10.0')).toHaveLength(3)
  })
})
