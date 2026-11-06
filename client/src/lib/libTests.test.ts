import { describe, expect, it } from 'vitest'
import { libraryParams } from './listenQuery'
import { marqueeSeconds, MIN_OVERFLOW_PX } from './marquee'
import { hasTag, suggestedTags } from './tags'

describe('libraryParams', () => {
  it('asks for the chosen view', () => {
    expect(libraryParams('recent', '', null, 40).toString()).toBe('limit=40&filter=recent')
    expect(libraryParams('rated', '', null, 40).get('filter')).toBe('rated')
    expect(libraryParams('finished', '', null, 40).get('filter')).toBe('finished')
  })

  it('searches through everything whichever view is showing', () => {
    const p = libraryParams('finished', 'dune', 'Favorites', 40)
    expect(p.get('filter')).toBe('all')
    expect(p.get('search')).toBe('dune')
    expect(p.get('tag')).toBeNull()
  })

  it('asks for the books with a tag', () => {
    const p = libraryParams('tag', '', 'Sci-fi/Fantasy', 40)
    expect(p.get('tag')).toBe('Sci-fi/Fantasy')
    expect(p.get('filter')).toBe('all')
  })

  it('a tag view with no tag chosen is just the whole library', () => {
    const p = libraryParams('tag', '', null, 40)
    expect(p.get('tag')).toBeNull()
    expect(p.get('filter')).toBe('all')
  })

  it('adds a starting point only when paging', () => {
    expect(libraryParams('all', '', null, 40).has('start')).toBe(false)
    expect(libraryParams('all', '', null, 40, 80).get('start')).toBe('80')
  })
})

describe('marqueeSeconds', () => {
  it('is never quicker than 5 seconds or slower than 40', () => {
    expect(marqueeSeconds(1)).toBe(5)
    expect(marqueeSeconds(100000)).toBe(40)
  })

  it('takes longer for more text to reveal', () => {
    expect(marqueeSeconds(600)).toBeGreaterThan(marqueeSeconds(200))
  })

  it('ignores sub-pixel overflow', () => {
    expect(MIN_OVERFLOW_PX).toBeGreaterThanOrEqual(2)
  })
})

describe('tags', () => {
  it('matches a tag whatever its capitalisation or leading hashes', () => {
    expect(hasTag(['Sci-Fi', 'Favorites'], 'sci-fi')).toBe(true)
    expect(hasTag(['Sci-Fi'], '  #SCI-FI ')).toBe(true)
    expect(hasTag(['Sci-Fi'], 'fantasy')).toBe(false)
    expect(hasTag(['Sci-Fi'], '')).toBe(false)
    expect(hasTag(['Sci-Fi'], '#')).toBe(false)
  })

  it('suggests the person’s other tags, most used first, skipping ones already on the book', () => {
    const all = [
      { tag: 'Favorites', count: 9 },
      { tag: 'Sci-Fi', count: 5 },
      { tag: 'Re-listen', count: 2 },
    ]
    expect(suggestedTags(all, ['sci-fi'])).toEqual(['Favorites', 'Re-listen'])
    expect(suggestedTags(all, [], 2)).toEqual(['Favorites', 'Sci-Fi'])
    expect(suggestedTags([], [])).toEqual([])
  })
})
