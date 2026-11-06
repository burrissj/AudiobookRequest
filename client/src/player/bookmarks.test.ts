// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import {
  addPendingBookmark,
  flushPendingBookmarks,
  loadPendingBookmarks,
  mergeBookmarks,
  PERSONAL_CHANGED,
  pendingBookmarksFor,
  removePendingBookmark,
  type PendingBookmark,
} from './bookmarks'

const post = vi.hoisted(() => vi.fn())
vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, api: { ...actual.api, post } }
})

const mark = (clientId: string, itemId = 'book1', positionSeconds = 100): PendingBookmark => ({
  clientId,
  itemId,
  positionSeconds,
  note: null,
  label: 'Chapter 2',
  createdAt: '2026-10-09T12:00:00Z',
})

describe('waiting bookmarks', () => {
  beforeEach(() => {
    localStorage.clear()
    post.mockReset()
  })
  afterEach(() => vi.restoreAllMocks())

  it('are kept on the device, per book', () => {
    addPendingBookmark(mark('a', 'book1'))
    addPendingBookmark(mark('b', 'book2'))

    expect(pendingBookmarksFor('book1').map((p) => p.clientId)).toEqual(['a'])
    expect(loadPendingBookmarks()).toHaveLength(2)
  })

  it('are replaced, not duplicated, when the same one is saved again (editing its note)', () => {
    addPendingBookmark(mark('a'))
    addPendingBookmark({ ...mark('a'), note: 'edited' })

    expect(loadPendingBookmarks()).toHaveLength(1)
    expect(loadPendingBookmarks()[0].note).toBe('edited')
  })

  it('can be removed', () => {
    addPendingBookmark(mark('a'))
    removePendingBookmark('a')
    expect(loadPendingBookmarks()).toEqual([])
  })

  it('survive damaged storage', () => {
    localStorage.setItem('abr.bookmarks.pending', '{not json')
    expect(loadPendingBookmarks()).toEqual([])
    localStorage.setItem('abr.bookmarks.pending', '{"a":1}')
    expect(loadPendingBookmarks()).toEqual([])
  })
})

describe('mergeBookmarks', () => {
  it('puts waiting bookmarks among the saved ones in book order, marked as waiting', () => {
    const server = [
      { id: 1, positionSeconds: 50, note: null, label: null, createdAt: 'x' },
      { id: 2, positionSeconds: 500, note: 'n', label: null, createdAt: 'x' },
    ]

    const merged = mergeBookmarks(server, [mark('w', 'book1', 200)])

    expect(merged.map((b) => b.positionSeconds)).toEqual([50, 200, 500])
    expect(merged[1].pendingClientId).toBe('w')
    expect(merged[0].pendingClientId).toBeUndefined()
  })
})

describe('flushPendingBookmarks', () => {
  beforeEach(() => {
    localStorage.clear()
    post.mockReset()
  })

  it('delivers each one, with its client id so a repeat is recognised, and clears them', async () => {
    addPendingBookmark(mark('a', 'book1', 10))
    addPendingBookmark(mark('b', 'book2', 20))
    post.mockResolvedValue({})
    const changed = vi.fn()
    window.addEventListener(PERSONAL_CHANGED, changed)

    const delivered = await flushPendingBookmarks()

    expect(delivered).toBe(2)
    expect(post).toHaveBeenCalledWith('/api/listen/items/book1/bookmarks', { positionSeconds: 10, note: null, label: 'Chapter 2', clientId: 'a' })
    expect(loadPendingBookmarks()).toEqual([])
    expect(changed).toHaveBeenCalledTimes(1)
    window.removeEventListener(PERSONAL_CHANGED, changed)
  })

  it('keeps them, and stops trying, when there is still no connection', async () => {
    addPendingBookmark(mark('a'))
    addPendingBookmark(mark('b'))
    post.mockRejectedValue(new TypeError('Failed to fetch'))

    expect(await flushPendingBookmarks()).toBe(0)

    expect(loadPendingBookmarks()).toHaveLength(2)
    expect(post).toHaveBeenCalledTimes(1)
  })

  it('keeps them when the sign-in has lapsed or the server is erroring', async () => {
    addPendingBookmark(mark('a'))
    post.mockRejectedValueOnce(new ApiError(401, 'sign in'))
    await flushPendingBookmarks()
    post.mockRejectedValueOnce(new ApiError(503, 'down'))
    await flushPendingBookmarks()

    expect(loadPendingBookmarks()).toHaveLength(1)
  })

  it('drops one the server refuses for good, and carries on with the rest', async () => {
    addPendingBookmark(mark('gone', 'missing'))
    addPendingBookmark(mark('ok', 'book1'))
    post.mockRejectedValueOnce(new ApiError(404, 'not found')).mockResolvedValueOnce({})

    const delivered = await flushPendingBookmarks()

    expect(delivered).toBe(1)
    expect(loadPendingBookmarks()).toEqual([])
  })

  it('does nothing when nothing is waiting', async () => {
    expect(await flushPendingBookmarks()).toBe(0)
    expect(post).not.toHaveBeenCalled()
  })
})
