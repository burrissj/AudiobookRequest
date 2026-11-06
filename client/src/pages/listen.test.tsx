// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BookMetadataEdit, ListenGroup, ListenGroups, ListenItem, ListenLibrary } from '../api/types'
import { Listen } from './Listen'

afterEach(cleanup)

const mocks = vi.hoisted(() => ({
  get: vi.fn(),
  play: vi.fn(),
  current: null as { id: string } | null,
  mine: { rating: null, tags: [], bookmarks: [] } as unknown,
  metadata: null as unknown,
  overview: null as string | null,
  role: 'User' as 'User' | 'Admin',
  edit: { override: null, audible: { checked: true, series: null, number: null } } as BookMetadataEdit,
  put: vi.fn(),
  del: vi.fn(),
  groups: [] as ListenGroup[],
  pending: 0,
  groupBooks: [] as ListenItem[],
}))

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, api: { ...actual.api, get: mocks.get, put: mocks.put, delete: mocks.del } }
})
vi.mock('../auth/useAuth', () => ({ useAuth: () => ({ user: { role: mocks.role } }) }))
vi.mock('../state/usePlayer', () => ({ usePlayer: () => ({ current: mocks.current, play: mocks.play }) }))
vi.mock('../hooks/useOnline', () => ({ useOnline: () => true }))
vi.mock('../components/AppHeader', () => ({ AppHeader: ({ title }: { title: string }) => <h1>{title}</h1> }))
vi.mock('../components/JellyfinLinks', () => ({ GetAppLinks: () => null }))
vi.mock('../components/SavedBooks', () => ({ SavedBooks: () => null }))
vi.mock('../offline/downloadManager', () => ({
  useOffline: () => ({ supported: true, saved: [], active: [], usage: null, unavailable: [] }),
  enqueueDownload: vi.fn(),
  cancelDownload: vi.fn(),
  removeSaved: vi.fn(),
}))

const book = (id: string, over: Partial<ListenItem> = {}): ListenItem => ({
  id,
  title: `Book ${id}`,
  author: 'An Author',
  durationSeconds: 36000,
  positionSeconds: 0,
  played: false,
  hasCover: false,
  ...over,
})

/** Answers the library calls the page makes, by the view asked for. */
function serve(views: { resume?: ListenItem[]; recent?: ListenItem[]; all?: ListenItem[] }, totals: Partial<Record<string, number>> = {}) {
  mocks.get.mockImplementation(async (url: string) => {
    if (url === '/api/listen/tags') return [{ tag: 'Favorites', count: 2 }]
    if (url.endsWith('/mine')) return mocks.mine
    if (url.endsWith('/metadata')) return mocks.edit
    if (/^\/api\/listen\/items\/[^/]+$/.test(url)) return { overview: mocks.overview }
    if (url.startsWith('/api/books/metadata')) return mocks.metadata
    if (url.startsWith('/api/listen/groups/books')) return { items: mocks.groupBooks, total: mocks.groupBooks.length } satisfies ListenLibrary
    if (url.startsWith('/api/listen/groups')) return { groups: mocks.groups, pending: mocks.pending } satisfies ListenGroups
    const params = new URL(url, 'http://x').searchParams
    const filter = (params.get('filter') ?? 'all') as 'resume' | 'recent' | 'all'
    const items = views[filter] ?? []
    const start = Number(params.get('start') ?? 0)
    const limit = Number(params.get('limit') ?? 18)
    const lib: ListenLibrary = { items: items.slice(start, start + limit), total: totals[filter] ?? items.length }
    return lib
  })
}

const renderPage = (mode: 'listen' | 'library' = 'listen', route = '/') =>
  render(
    <MemoryRouter initialEntries={[route]}>
      <Listen mode={mode} />
    </MemoryRouter>,
  )

describe('Listen page', () => {
  beforeEach(() => {
    mocks.get.mockReset()
    mocks.play.mockReset()
    mocks.current = null
    mocks.mine = { rating: null, tags: [], bookmarks: [] }
    mocks.metadata = null
    mocks.overview = null
    mocks.role = 'User'
    mocks.edit = { override: null, audible: { checked: true, series: null, number: null } }
    mocks.put.mockReset()
    mocks.del.mockReset()
    mocks.groups = []
    mocks.pending = 0
    mocks.groupBooks = []
    localStorage.clear()
  })

  it('opens on what you are listening to, with a shelf of what was added lately that leaves those out', async () => {
    serve({ resume: [book('a', { positionSeconds: 3600 })], recent: [book('a'), book('b'), book('c')] })
    renderPage()

    expect(await screen.findByRole('heading', { name: 'Continue listening' })).toBeTruthy()
    const shelfHeading = await screen.findByRole('heading', { name: 'Recently added' })
    const shelf = shelfHeading.closest('section') as HTMLElement
    expect(within(shelf).getAllByRole('button', { name: /^Open / }).map((b) => b.getAttribute('aria-label'))).toEqual(['Open Book b by An Author', 'Open Book c by An Author'])
  })

  it('shows how much of the book is left, and opens its page when tapped without starting it', async () => {
    serve({ resume: [book('a', { positionSeconds: 3600 })], recent: [] })
    renderPage()

    expect(await screen.findByText('9h left')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Open Book a by An Author' }))

    expect(await screen.findByRole('dialog', { name: 'Book a' })).toBeTruthy()
    expect(mocks.play).not.toHaveBeenCalled()
  })

  it("the book's page has the description, your rating, tags and bookmark notes, and plays from there", async () => {
    serve({ resume: [book('a', { positionSeconds: 3600 })], recent: [] })
    mocks.metadata = {
      description: '<p>A sweeping tale.</p>',
      narrators: ['Jim Dale'],
      genres: ['Fantasy'],
      runtimeMinutes: 600,
      formatType: null,
      language: null,
      publisherName: null,
      seriesTitle: null,
      seriesAsin: null,
      bundleBookCount: null,
    }
    mocks.mine = {
      rating: 4,
      tags: ['Re-listen'],
      bookmarks: [{ id: 1, positionSeconds: 125, note: 'Great opening', label: 'Chapter 2' }],
    }
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Open Book a by An Author' }))

    const sheet = await screen.findByRole('dialog', { name: 'Book a' })
    expect(await within(sheet).findByText('A sweeping tale.')).toBeTruthy()
    expect(within(sheet).getByText('Jim Dale')).toBeTruthy()
    expect(within(sheet).getByText('Re-listen')).toBeTruthy()
    expect(within(sheet).getByText('Great opening')).toBeTruthy()
    expect(within(sheet).getByRole('radio', { name: '4 stars' }).getAttribute('aria-checked')).toBe('true')
    expect(within(sheet).queryByRole('button', { name: /^\+ Bookmark at/ })).toBeNull() // that is for the book being played

    fireEvent.click(within(sheet).getByRole('button', { name: 'Resume' }))

    expect(mocks.play).toHaveBeenCalledWith('a')
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it("shows Jellyfin's own description of the book, and says so when no description can be found", async () => {
    serve({ resume: [book('c')], recent: [] })
    mocks.overview = 'From the home server.'
    mocks.metadata = { description: null, narrators: [], genres: [], runtimeMinutes: null, formatType: null, language: null, publisherName: null, seriesTitle: null, seriesAsin: null, bundleBookCount: null }
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Open Book c by An Author' }))

    const sheet = await screen.findByRole('dialog', { name: 'Book c' })
    expect(await within(sheet).findByText('From the home server.')).toBeTruthy()
    expect(within(sheet).queryByText(/No description found/)).toBeNull()
  })

  it('says so when no description can be found anywhere', async () => {
    serve({ resume: [book('d')], recent: [] })
    mocks.metadata = { description: null, narrators: [], genres: [], runtimeMinutes: null, formatType: null, language: null, publisherName: null, seriesTitle: null, seriesAsin: null, bundleBookCount: null }
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Open Book d by An Author' }))

    expect(await screen.findByText('No description found for this book.')).toBeTruthy()
  })

  it('still looks a book up, by its title alone, when Jellyfin has no author for it', async () => {
    serve({ resume: [book('e', { author: null })], recent: [] })
    mocks.metadata = { description: 'Found by title.', narrators: [], genres: [], runtimeMinutes: null, formatType: null, language: null, publisherName: null, seriesTitle: null, seriesAsin: null, bundleBookCount: null }
    renderPage()
    fireEvent.click(await screen.findByRole('button', { name: 'Open Book e' }))

    expect(await screen.findByText('Found by title.')).toBeTruthy()
    const asked = mocks.get.mock.calls.map((c) => c[0] as string).find((u) => u.startsWith('/api/books/metadata'))
    expect(asked).toBe('/api/books/metadata?title=Book+e')
  })

  it('"See all" goes to the Library, on its recently-added list', async () => {
    serve({ resume: [book('a', { positionSeconds: 60 })], recent: [book('a'), book('b')] })
    renderPage()

    const link = await screen.findByRole('link', { name: 'See all ›' })
    expect(link.getAttribute('href')).toBe('/listen/library?filter=recent')
  })

  it('the Library lists everything, 36 at a time, with its own filters and tags', async () => {
    const all = Array.from({ length: 40 }, (_, i) => book(`r${i}`))
    serve({ all })
    renderPage('library')

    await waitFor(() => expect(screen.getAllByRole('button', { name: /^Open / })).toHaveLength(36))
    expect(screen.queryByRole('heading', { name: 'Continue listening' })).toBeNull()
    expect(screen.getByRole('tab', { name: 'A–Z' })).toBeTruthy()
    expect(screen.queryByRole('tab', { name: 'Continue' })).toBeNull()
    expect(screen.getByRole('button', { name: /# Favorites/ })).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: 'Show more (4 left)' }))
    await waitFor(() => expect(screen.getAllByRole('button', { name: /^Open / })).toHaveLength(40))
  })

  it('the Library can open straight onto the recently-added list', async () => {
    serve({ recent: [book('n1'), book('n2')], all: [book('z')] })
    renderPage('library', '/listen/library?filter=recent')

    expect(await screen.findByRole('button', { name: 'Open Book n1 by An Author' })).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Open Book z by An Author' })).toBeNull()
  })

  it('when nothing has been started it says so, points to the Library and still offers what was added lately', async () => {
    serve({ resume: [], recent: [book('x'), book('y')] })
    renderPage()

    expect(await screen.findByText(/You haven't started a book yet/)).toBeTruthy()
    const libraryLinks = screen.getAllByRole('link', { name: 'Library' }) // the tab, and the one in the message
    expect(libraryLinks).toHaveLength(2)
    expect(libraryLinks.every((l) => l.getAttribute('href') === '/listen/library')).toBe(true)
    expect(screen.queryByRole('heading', { name: 'Continue listening' })).toBeNull()
    expect(await screen.findByRole('heading', { name: 'Recently added' })).toBeTruthy()
  })

  it('marks a finished book and shows a book rating on the cover', async () => {
    serve({ all: [book('x', { played: true, myRating: 5 })] })
    renderPage('library')

    expect(await screen.findByText('★ 5')).toBeTruthy()
    expect(screen.getByTitle('Finished')).toBeTruthy() // the tick on the cover
    expect(screen.getAllByText('Finished')).toHaveLength(2) // the filter chip, and the line under the book
  })

  it('switches between the grid and the bookshelf, and remembers the choice', async () => {
    serve({ all: [book('x')] })
    const first = renderPage('library')
    await screen.findByRole('button', { name: 'Open Book x by An Author' })
    expect(first.container.querySelector('.listen-grid')?.className).not.toContain('listen-bookshelf')

    fireEvent.click(screen.getByRole('button', { name: 'Shelf' }))
    expect(first.container.querySelector('.listen-grid')?.className).toContain('listen-bookshelf')
    expect(screen.getByRole('button', { name: 'Shelf' }).getAttribute('aria-pressed')).toBe('true')

    cleanup()
    const again = renderPage('library')
    await screen.findByRole('button', { name: 'Open Book x by An Author' })
    expect(again.container.querySelector('.listen-grid')?.className).toContain('listen-bookshelf')
  })

  it('highlights the book that is playing now', async () => {
    mocks.current = { id: 'x' }
    serve({ resume: [], recent: [book('x'), book('y')] })
    renderPage()

    const playing = await screen.findByRole('button', { name: 'Open Book x by An Author' })
    expect(playing.className).toContain('active')
    expect(screen.getByRole('button', { name: 'Open Book y by An Author' }).className).not.toContain('active')
  })

  describe('correcting a book', () => {
    async function openEditor() {
      renderPage('library')
      fireEvent.click(await screen.findByRole('button', { name: 'Open Book a by An Author' }))
      const sheet = await screen.findByRole('dialog', { name: 'Book a' })
      fireEvent.click(await within(sheet).findByRole('button', { name: 'Edit details' }))
      return within(await screen.findByRole('form', { name: 'Edit book details' }))
    }

    it('is only offered to admins', async () => {
      serve({ all: [book('a')] })
      renderPage('library')
      fireEvent.click(await screen.findByRole('button', { name: 'Open Book a by An Author' }))

      const sheet = await screen.findByRole('dialog', { name: 'Book a' })
      await within(sheet).findByText('Your rating')
      expect(within(sheet).queryByRole('button', { name: 'Edit details' })).toBeNull()
    })

    it('saves an author and a series with its number for the book, and says what Audible found', async () => {
      mocks.role = 'Admin'
      serve({ all: [book('a')] })
      mocks.edit = { override: null, audible: { checked: true, series: 'Wrong Saga', number: 3 } }
      mocks.put.mockResolvedValue(mocks.edit)
      const form = await openEditor()

      expect(await form.findByText('Audible lists it in Wrong Saga, book 3.')).toBeTruthy()
      fireEvent.change(form.getByLabelText('Author'), { target: { value: '  Amber V. Nicole ' } })
      fireEvent.change(form.getByLabelText('Series'), { target: { value: 'Gods & Monsters' } })
      fireEvent.change(form.getByLabelText('Book number in the series'), { target: { value: '2' } })
      fireEvent.click(form.getByRole('button', { name: 'Save' }))

      await waitFor(() =>
        expect(mocks.put).toHaveBeenCalledWith('/api/listen/items/a/metadata', {
          author: 'Amber V. Nicole',
          series: 'Gods & Monsters',
          seriesNumber: 2,
          notInSeries: false,
        }),
      )
      await waitFor(() => expect(screen.queryByRole('form', { name: 'Edit book details' })).toBeNull()) // the form closes
    })

    it('can say a book is in no series', async () => {
      mocks.role = 'Admin'
      serve({ all: [book('a')] })
      mocks.put.mockResolvedValue(mocks.edit)
      const form = await openEditor()

      fireEvent.change(form.getByLabelText('Series'), { target: { value: 'Typed first' } })
      fireEvent.click(form.getByRole('checkbox', { name: 'This book is not part of a series' }))
      expect((form.getByLabelText('Series') as HTMLInputElement).disabled).toBe(true)
      fireEvent.click(form.getByRole('button', { name: 'Save' }))

      await waitFor(() =>
        expect(mocks.put).toHaveBeenCalledWith('/api/listen/items/a/metadata', { author: null, series: null, seriesNumber: null, notInSeries: true }),
      )
    })

    it('shows what was saved before, and "Reset to automatic" removes it', async () => {
      mocks.role = 'Admin'
      serve({ all: [book('a')] })
      mocks.edit = { override: { author: 'Mine', series: 'My Saga', seriesNumber: 4, notInSeries: false }, audible: { checked: false, series: null, number: null } }
      mocks.del.mockResolvedValue(undefined)
      const form = await openEditor()

      await waitFor(() => expect((form.getByLabelText('Series') as HTMLInputElement).value).toBe('My Saga'))
      expect((form.getByLabelText('Author') as HTMLInputElement).value).toBe('Mine')
      expect(form.getByText("Audible hasn't been asked about this book yet.")).toBeTruthy()
      fireEvent.click(form.getByRole('button', { name: 'Reset to automatic' }))

      await waitFor(() => expect(mocks.del).toHaveBeenCalledWith('/api/listen/items/a/metadata'))
    })

    it("shows the server's reason when a correction is refused, and keeps the form open", async () => {
      mocks.role = 'Admin'
      serve({ all: [book('a')] })
      const { ApiError } = await import('../api/client')
      mocks.put.mockRejectedValue(new ApiError(400, 'Give the series a name to number the book in it.'))
      const form = await openEditor()

      fireEvent.change(form.getByLabelText('Book number in the series'), { target: { value: '2' } })
      fireEvent.click(form.getByRole('button', { name: 'Save' }))

      expect(await form.findByText('Give the series a name to number the book in it.')).toBeTruthy()
      expect(screen.getByRole('form', { name: 'Edit book details' })).toBeTruthy()
    })

    it("the book's page shows its series and number as the Library has them", async () => {
      serve({ all: [book('a', { series: 'Gods & Monsters', seriesNumber: 2 })] })
      renderPage('library')
      fireEvent.click(await screen.findByRole('button', { name: 'Open Book a by An Author' }))

      const sheet = await screen.findByRole('dialog', { name: 'Book a' })
      expect(await within(sheet).findByText('Gods & Monsters · Book 2')).toBeTruthy()
    })
  })

  describe('grouping the Library', () => {
    const authors: ListenGroup[] = [
      { key: 'ann', name: 'Ann Author', count: 3, finished: 1, coverIds: [] },
      { key: 'bob', name: 'Bob Writer', count: 1, finished: 0, coverIds: [] },
    ]

    it('lists the authors with how many books each has, and opens one to show its books in order', async () => {
      serve({ all: [book('z')] })
      mocks.groups = authors
      mocks.groupBooks = [book('a1', { series: 'Dragon Street', seriesNumber: 1 }), book('a2', { series: 'Dragon Street', seriesNumber: 2 })]
      renderPage('library')

      fireEvent.click(await screen.findByRole('tab', { name: 'Authors' }))
      const list = await screen.findByRole('list', { name: 'Authors' })
      expect(within(list).getByText('Ann Author')).toBeTruthy()
      expect(within(list).getByText('3 books · 1 finished')).toBeTruthy()
      expect(within(list).getByText('1 book')).toBeTruthy()
      expect(screen.queryByRole('button', { name: /^Open / })).toBeNull() // no books until a pile is opened

      fireEvent.click(within(list).getByRole('button', { name: /Ann Author/ }))

      expect(await screen.findByRole('heading', { name: 'Ann Author' })).toBeTruthy()
      expect(screen.getByText('Book 1 · 10h')).toBeTruthy()
      expect(screen.getByText('Book 2 · 10h')).toBeTruthy()
      const asked = mocks.get.mock.calls.map((c) => c[0] as string).find((u) => u.startsWith('/api/listen/groups/books'))
      expect(asked).toBe('/api/listen/groups/books?by=author&key=ann')

      fireEvent.click(screen.getByRole('button', { name: '‹ Authors' }))
      expect(await screen.findByRole('list', { name: 'Authors' })).toBeTruthy()
    })

    it('a book opened from a pile shows its page like any other', async () => {
      serve({})
      mocks.groups = authors
      mocks.groupBooks = [book('a1')]
      renderPage('library', '/listen/library?filter=authors&group=ann')

      fireEvent.click(await screen.findByRole('button', { name: 'Open Book a1 by An Author' }))

      expect(await screen.findByRole('dialog', { name: 'Book a1' })).toBeTruthy()
    })

    it('shows the series with who wrote them, and the books that belong to none', async () => {
      serve({})
      mocks.groups = [
        { key: 'dragon street', name: 'Dragon Street', count: 2, finished: 0, coverIds: [], by: 'Ann Author' },
        { key: '-', name: 'Not in a series', count: 4, finished: 0, coverIds: [] },
      ]
      renderPage('library', '/listen/library?filter=series')

      const list = await screen.findByRole('list', { name: 'Series' })
      expect(within(list).getByText('Ann Author · 2 books')).toBeTruthy()
      expect(within(list).getByText('Not in a series')).toBeTruthy()
    })

    it('says so while series are still being looked up, and asks again until they are', async () => {
      serve({})
      mocks.groups = [{ key: 'saga', name: 'Saga', count: 2, finished: 0, coverIds: [] }]
      mocks.pending = 5
      const timers = vi.spyOn(window, 'setTimeout')
      renderPage('library', '/listen/library?filter=series')

      expect(await screen.findByText('Still looking up series for 5 books — until then they are grouped by their titles.')).toBeTruthy()
      await waitFor(() => expect(timers.mock.calls.some((c) => c[1] === 20_000)).toBe(true)) // the next ask is scheduled
      timers.mockRestore()
    })

    it('does not mention the lookup once every book has been asked about, or on the Authors list', async () => {
      serve({})
      mocks.groups = authors
      renderPage('library', '/listen/library?filter=authors')
      await screen.findByRole('list', { name: 'Authors' })
      expect(screen.queryByText(/Still looking up/)).toBeNull()

      cleanup()
      mocks.pending = 3 // pending books matter only for series
      renderPage('library', '/listen/library?filter=authors')
      await screen.findByRole('list', { name: 'Authors' })
      expect(screen.queryByText(/Still looking up/)).toBeNull()
    })

    it('searching narrows the piles by name instead of searching books', async () => {
      serve({})
      mocks.groups = authors
      renderPage('library', '/listen/library?filter=authors')

      const list = await screen.findByRole('list', { name: 'Authors' })
      fireEvent.change(screen.getByRole('searchbox', { name: 'Search authors' }), { target: { value: 'bob' } })

      expect(within(list).queryByText('Ann Author')).toBeNull()
      expect(within(list).getByText('Bob Writer')).toBeTruthy()
      expect(mocks.get.mock.calls.some((c) => (c[0] as string).includes('search='))).toBe(false)
      expect(screen.getByRole('tab', { name: 'Authors' })).toBeTruthy() // the filters stay while searching
    })

    it('says so when a pile is gone', async () => {
      serve({})
      mocks.groups = authors
      const { ApiError } = await import('../api/client')
      mocks.get.mockImplementation(async (url: string) => {
        if (url.startsWith('/api/listen/groups/books')) throw new ApiError(404, 'Not found')
        if (url.startsWith('/api/listen/groups')) return { groups: authors, pending: 0 } satisfies ListenGroups
        if (url === '/api/listen/tags') return []
        return { items: [], total: 0 }
      })
      renderPage('library', '/listen/library?filter=authors&group=gone')

      expect(await screen.findByText('That is no longer in your library.')).toBeTruthy()
    })
  })
})
