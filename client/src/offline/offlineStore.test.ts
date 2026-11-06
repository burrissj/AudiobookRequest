import { describe, expect, it } from 'vitest'
import { chunkBytes, chunkCount, totalFromContentRange } from './offlineStore'

const MB = 1024 * 1024

describe('chunking', () => {
  it('splits a file into 4 MB chunks with a smaller last one', () => {
    expect(chunkCount(10 * MB)).toBe(3)
    expect(chunkBytes(10 * MB, 0)).toEqual({ start: 0, end: 4 * MB - 1 })
    expect(chunkBytes(10 * MB, 2)).toEqual({ start: 8 * MB, end: 10 * MB - 1 })
  })

  it('has exactly one chunk for a file that is an exact multiple', () => {
    expect(chunkCount(4 * MB)).toBe(1)
    expect(chunkBytes(4 * MB, 0)).toEqual({ start: 0, end: 4 * MB - 1 })
  })

  it('covers every byte exactly once', () => {
    const size = 13 * MB + 12345
    let next = 0
    for (let n = 0; n < chunkCount(size); n++) {
      const { start, end } = chunkBytes(size, n)
      expect(start).toBe(next)
      next = end + 1
    }
    expect(next).toBe(size)
  })
})

describe('totalFromContentRange', () => {
  it('reads the total size', () => {
    expect(totalFromContentRange('bytes 0-4194303/638731591')).toBe(638731591)
  })

  it('returns null when it cannot', () => {
    expect(totalFromContentRange(null)).toBeNull()
    expect(totalFromContentRange('bytes 0-9/*')).toBeNull()
  })
})
