// @vitest-environment jsdom
import { act, cleanup, render } from '@testing-library/react'
import { useEffect } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BookRequestDto } from '../api/types'
import { RequestsProvider } from './RequestsContext'
import { useRequests, type BookSearchResultLike, type RequestsContextValue } from './useRequests'

const api = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), delete: vi.fn() }))

vi.mock('../api/client', () => ({
  api,
  ApiError: class ApiError extends Error {},
}))
vi.mock('../api/notifications', () => ({ useRequestUpdates: () => {} }))

afterEach(cleanup)

const created = { id: 9, status: 'Upcoming' } as BookRequestDto

function book(over: Partial<BookSearchResultLike> = {}): BookSearchResultLike {
  return { foreignBookId: 'fb:1', title: 'Soon', authorName: 'An Author', seriesTitle: null, coverUrl: null, releaseDate: null, ...over }
}

let ctx: RequestsContextValue

// Handed to the test from an effect, so it always holds the latest value after each update.
function Probe() {
  const value = useRequests()
  useEffect(() => {
    ctx = value
  })
  return null
}

async function mount() {
  await act(async () => {
    render(
      <RequestsProvider>
        <Probe />
      </RequestsProvider>,
    )
  })
}

const dayOffset = (days: number) => new Date(Date.now() + days * 86_400_000).toISOString().slice(0, 10)

describe('requestBook with a release date', () => {
  beforeEach(() => {
    api.get.mockReset().mockImplementation((url: string) => (url === '/api/books/requests' ? Promise.resolve([]) : Promise.resolve(null)))
    api.post.mockReset().mockResolvedValue(created)
  })

  const postedRequests = () => api.post.mock.calls.filter(([url]) => url === '/api/books/requests')

  it('asks first when the book is not out yet, naming the date, and requests it once confirmed', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await mount()
    const release = dayOffset(30)

    await act(() => ctx.requestBook(book({ releaseDate: `${release}T00:00:00+00:00` })))

    expect(confirm).toHaveBeenCalledTimes(1)
    expect(confirm.mock.calls[0][0]).toContain("isn't released yet")
    expect(confirm.mock.calls[0][0]).toContain('"Soon"')
    expect(postedRequests()).toHaveLength(1)
    // the server gets the date, as an instant it can read
    expect(postedRequests()[0][1]).toMatchObject({ foreignBookId: 'fb:1', releaseDate: `${release}T00:00:00Z` })
    expect(ctx.requests.map((r) => r.id)).toEqual([9])
  })

  it('does nothing when the person says no', async () => {
    vi.spyOn(window, 'confirm').mockReturnValue(false)
    await mount()

    await act(() => ctx.requestBook(book({ releaseDate: dayOffset(30) })))

    expect(postedRequests()).toHaveLength(0)
    expect(ctx.requests).toEqual([])
    expect(ctx.requestingId).toBeNull()
  })

  it('does not ask about a book that is already out, but still sends its date', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await mount()
    const release = dayOffset(-30)

    await act(() => ctx.requestBook(book({ releaseDate: release })))

    expect(confirm).not.toHaveBeenCalled()
    expect(postedRequests()[0][1]).toMatchObject({ releaseDate: `${release}T00:00:00Z` })
  })

  it('does not ask when there is no date at all', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await mount()

    await act(() => ctx.requestBook(book({ releaseDate: undefined })))

    expect(confirm).not.toHaveBeenCalled()
    expect(postedRequests()[0][1]).toMatchObject({ releaseDate: null })
  })

  it('asks about the unreleased book before the similar-edition question, so each is answered on its own', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true)
    await mount()

    await act(() => ctx.requestBook(book({ releaseDate: dayOffset(10), similarInLibraryTitle: 'Soon (Unabridged)' })))

    expect(confirm).toHaveBeenCalledTimes(2)
    expect(confirm.mock.calls[0][0]).toContain("isn't released yet")
    expect(confirm.mock.calls[1][0]).toContain('You may already have this')
  })
})
