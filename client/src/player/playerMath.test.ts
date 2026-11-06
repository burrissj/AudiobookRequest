import { describe, expect, it } from 'vitest'
import {
  chapterIndexAt,
  clampSpeed,
  formatClock,
  formatDuration,
  formatSpeed,
  formatSpeedExact,
  fractionFromSpeed,
  MAX_SPEED,
  MIN_SPEED,
  remainingAtSpeed,
  SPEED_PRESETS,
  speedFromFraction,
  stepSpeed,
} from './playerMath'

describe('speed', () => {
  it('steps in quarters and stops at 0.25x and 3x', () => {
    expect(stepSpeed(1, 1)).toBe(1.25)
    expect(stepSpeed(1, -1)).toBe(0.75)
    expect(stepSpeed(MAX_SPEED, 1)).toBe(MAX_SPEED)
    expect(stepSpeed(MIN_SPEED, -1)).toBe(MIN_SPEED)
  })

  it('walks the whole range one notch at a time', () => {
    let s = MIN_SPEED
    const seen = [s]
    while (s < MAX_SPEED) {
      s = stepSpeed(s, 1)
      seen.push(s)
    }
    expect(seen).toHaveLength(12)
    expect(seen[seen.length - 1]).toBe(3)
  })

  it('keeps fine-tuned speeds to the nearest 0.05 and clamps to 0.25x-3x', () => {
    expect(clampSpeed(1.1)).toBe(1.1)
    expect(clampSpeed(1.12)).toBe(1.1)
    expect(clampSpeed(1.13)).toBe(1.15)
    expect(clampSpeed(10)).toBe(3)
    expect(clampSpeed(0)).toBe(0.25)
    expect(clampSpeed(-4)).toBe(0.25)
    expect(clampSpeed(NaN)).toBe(1)
    expect(clampSpeed(Infinity)).toBe(1)
  })

  it('never leaves floating-point residue in a speed', () => {
    for (let h = 25; h <= 300; h += 5) {
      const s = clampSpeed(h / 100)
      expect(String(s)).toBe(String(h / 100))
    }
    expect(clampSpeed(0.1 + 0.2 + 0.85)).toBe(1.15)
  })

  it('moves the buttons to the next quarter from a fine-tuned speed', () => {
    expect(stepSpeed(1.15, 1)).toBe(1.25)
    expect(stepSpeed(1.15, -1)).toBe(1)
    expect(stepSpeed(1.05, 1)).toBe(1.25)
    expect(stepSpeed(1.2, -1)).toBe(1)
    expect(stepSpeed(1.45, 1)).toBe(1.5)
    expect(stepSpeed(0.3, -1)).toBe(0.25)
    expect(stepSpeed(2.9, 1)).toBe(3)
  })

  it('still moves a full quarter from a speed that is already on a notch', () => {
    expect(stepSpeed(1, 1)).toBe(1.25)
    expect(stepSpeed(1, -1)).toBe(0.75)
    expect(stepSpeed(1.5, 1)).toBe(1.75)
  })

  it('places a speed along the slider, slowest first', () => {
    expect(fractionFromSpeed(MIN_SPEED)).toBe(0)
    expect(fractionFromSpeed(MAX_SPEED)).toBe(1)
    expect(fractionFromSpeed(1)).toBeCloseTo(0.75 / 2.75)
  })

  it('turns a point on the slider into a speed, snapped and kept in range', () => {
    expect(speedFromFraction(0)).toBe(MIN_SPEED)
    expect(speedFromFraction(1)).toBe(MAX_SPEED)
    expect(speedFromFraction(-1)).toBe(MIN_SPEED)
    expect(speedFromFraction(5)).toBe(MAX_SPEED)
    expect(speedFromFraction(NaN)).toBe(MIN_SPEED)
    expect(speedFromFraction(0.75 / 2.75)).toBe(1)
  })

  it('round-trips every offered speed through the slider', () => {
    for (let h = 25; h <= 300; h += 5) {
      const speed = h / 100
      expect(speedFromFraction(fractionFromSpeed(speed))).toBe(speed)
    }
  })

  it('offers presets that are all inside the range, in order, including normal speed', () => {
    expect(SPEED_PRESETS).toContain(1)
    expect([...SPEED_PRESETS].sort((a, b) => a - b)).toEqual(SPEED_PRESETS)
    expect(SPEED_PRESETS.every((s) => s >= MIN_SPEED && s <= MAX_SPEED && clampSpeed(s) === s)).toBe(true)
  })

  it('shows the exact speed with two decimals', () => {
    expect(formatSpeedExact(1)).toBe('1.00×')
    expect(formatSpeedExact(1.15)).toBe('1.15×')
    expect(formatSpeedExact(0.25)).toBe('0.25×')
    expect(formatSpeedExact(99)).toBe('3.00×')
  })

  it('labels speeds without trailing zeros', () => {
    expect(formatSpeed(1)).toBe('1x')
    expect(formatSpeed(1.25)).toBe('1.25x')
    expect(formatSpeed(0.5)).toBe('0.5x')
    expect(formatSpeed(1.15)).toBe('1.15x')
    expect(formatSpeed(3)).toBe('3x')
  })

  it('works out time left at the chosen speed', () => {
    expect(remainingAtSpeed(7200, 3600, 2)).toBe(1800)
    expect(remainingAtSpeed(100, 200, 1)).toBe(0)
  })
})

describe('time formatting', () => {
  it('formats clocks', () => {
    expect(formatClock(0)).toBe('0:00')
    expect(formatClock(65)).toBe('1:05')
    expect(formatClock(3725)).toBe('1:02:05')
    expect(formatClock(-5)).toBe('0:00')
    expect(formatClock(NaN)).toBe('0:00')
  })

  it('formats durations', () => {
    expect(formatDuration(0)).toBe('0m')
    expect(formatDuration(42 * 60)).toBe('42m')
    expect(formatDuration(3600)).toBe('1h')
    expect(formatDuration(3600 * 5 + 12 * 60)).toBe('5h 12m')
  })
})

describe('chapterIndexAt', () => {
  const chapters = [
    { name: 'One', startSeconds: 0 },
    { name: 'Two', startSeconds: 100 },
    { name: 'Three', startSeconds: 250 },
  ]

  it('finds the chapter playing', () => {
    expect(chapterIndexAt(chapters, 0)).toBe(0)
    expect(chapterIndexAt(chapters, 99)).toBe(0)
    expect(chapterIndexAt(chapters, 100)).toBe(1)
    expect(chapterIndexAt(chapters, 9999)).toBe(2)
  })

  it('has no chapter when the book has none', () => {
    expect(chapterIndexAt([], 50)).toBe(-1)
  })
})
