// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BookRequestDto } from '../api/types'
import { ListenTile } from '../components/ListenTile'
import { coverStatus, groupRequests } from '../lib/requestGroups'
import { Requests } from './Requests'

afterEach(cleanup)

const hooks = vi.hoisted(() => ({
  requests: [] as unknown[],
  user: { username: 'sam', role: 'User' } as { username: string; role: string },
  cancelRequest: vi.fn(),
  actOnRequest: vi.fn(),
  get: vi.fn(),
}))

vi.mock('../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../api/client')>()
  return { ...actual, api: { ...actual.api, get: hooks.get } }
})

vi.mock('../auth/useAuth', () => ({ useAuth: () => ({ user: hooks.user }) }))
vi.mock('../state/useRequests', () => ({
  useRequests: () => ({
    requests: hooks.requests,
    refreshing: false,
    refreshRequests: vi.fn(),
    error: null,
    cancellingId: null,
    cancelRequest: hooks.cancelRequest,
    actingId: null,
    actOnRequest: hooks.actOnRequest,
  }),
}))
// The links and buttons that talk to Jellyfin or the player are not what is being tested here.
vi.mock('../components/JellyfinLinks', () => ({ GetAppLinks: () => null, ListenButton: () => null }))
vi.mock('../components/PlayHereButton', () => ({ PlayHereButton: () => null }))
vi.mock('../components/AppHeader', () => ({ AppHeader: ({ title }: { title: string }) => <h1>{title}</h1> }))
vi.mock('../offline/downloadManager', () => ({
  useOffline: () => ({ supported: true, saved: [], active: [], usage: null, unavailable: [] }),
  enqueueDownload: vi.fn(),
  cancelDownload: vi.fn(),
  removeSaved: vi.fn(),
}))

const req = (over: Partial<BookRequestDto> & { id: number }): BookRequestDto => ({
  chaptarrForeignBookId: `f${over.id}`,
  title: `Book ${over.id}`,
  author: 'An Author',
  series: null,
  coverUrl: null,
  status: 'Ready',
  failureReason: null,
  downloadProgressPercent: null,
  downloadSizeBytes: null,
  downloadSizeLeftBytes: null,
  downloadClientName: null,
  requestedByUsername: 'sam',
  requestedAt: '2026-10-01T12:00:00Z',
  snatchedAt: null,
  readyAt: null,
  diagnostic: null,
  wishlisted: false,
  releaseDate: null,
  nextSearchAt: null,
  ...over,
})

describe('groupRequests', () => {
  it('puts what needs attention first and the finished books last, leaving out empty sections', () => {
    const groups = groupRequests([
      req({ id: 1, status: 'Ready' }),
      req({ id: 2, status: 'Downloading' }),
      req({ id: 3, status: 'Failed' }),
      req({ id: 4, status: 'Denied' }),
      req({ id: 5, status: 'Searching' }),
    ])

    expect(groups.map((g) => g.title)).toEqual(['Needs attention', 'On its way', 'Ready to listen'])
    expect(groups[0].items.map((r) => r.id)).toEqual([3, 4])
    expect(groups[1].items.map((r) => r.id)).toEqual([2, 5])
  })

  it('has a section for requests waiting on an admin', () => {
    expect(groupRequests([req({ id: 1, status: 'PendingApproval' })]).map((g) => g.title)).toEqual(['Awaiting approval'])
  })

  it('keeps books that are not out yet in their own section, between "On its way" and the finished books', () => {
    const groups = groupRequests([
      req({ id: 1, status: 'Ready' }),
      req({ id: 2, status: 'Upcoming', releaseDate: '2027-01-05T00:00:00+00:00' }),
      req({ id: 3, status: 'Searching' }),
    ])

    expect(groups.map((g) => g.title)).toEqual(['On its way', 'Upcoming releases', 'Ready to listen'])
    expect(groups[1].items.map((r) => r.id)).toEqual([2])
  })

  it('has no sections when there are no requests', () => {
    expect(groupRequests([])).toEqual([])
  })
})

describe('coverStatus', () => {
  it('shows download progress, and colours good and bad news', () => {
    expect(coverStatus(req({ id: 1, status: 'Downloading', downloadProgressPercent: 0.426 }))).toEqual({ text: 'Downloading 43%', tone: 'pending' })
    expect(coverStatus(req({ id: 1, status: 'Downloading' }))).toEqual({ text: 'Downloading…', tone: 'pending' })
    expect(coverStatus(req({ id: 1, status: 'Ready' }))).toEqual({ text: 'Ready', tone: 'ok' })
    expect(coverStatus(req({ id: 1, status: 'Failed' }))).toEqual({ text: 'Failed', tone: 'fail' })
    expect(coverStatus(req({ id: 1, status: 'Denied' }))).toEqual({ text: 'Declined', tone: 'fail' })
  })

  it('puts the release date on the cover of a book that is not out yet', () => {
    expect(coverStatus(req({ id: 1, status: 'Upcoming', releaseDate: '2099-11-05T00:00:00+00:00' }))).toEqual({
      text: 'Out Nov 5, 2099',
      tone: 'upcoming',
    })
    expect(coverStatus(req({ id: 1, status: 'Upcoming', releaseDate: null }))).toEqual({ text: 'Upcoming', tone: 'upcoming' })
  })
})

describe('Requests page', () => {
  beforeEach(() => {
    hooks.get.mockReset()
    hooks.get.mockRejectedValue(new Error('no metadata'))
    hooks.user = { username: 'sam', role: 'User' }
    hooks.requests = []
    hooks.actOnRequest.mockReset()
    hooks.cancelRequest.mockReset()
  })

  const renderPage = () =>
    render(
      <MemoryRouter>
        <Requests />
      </MemoryRouter>,
    )

  it('shows each section as a grid of covers with a status on each', () => {
    hooks.requests = [req({ id: 1, status: 'Ready' }), req({ id: 2, status: 'Downloading', downloadProgressPercent: 0.5 }), req({ id: 3, status: 'Failed' })]
    renderPage()

    expect(screen.getAllByRole('heading', { level: 2 }).map((h) => h.textContent)).toEqual(['Needs attention', 'On its way', 'Ready to listen'])
    expect(screen.getByText('Downloading 50%')).toBeTruthy()
    expect(screen.getByText('Failed')).toBeTruthy()
    expect(screen.getByText('Ready')).toBeTruthy()
    expect(screen.getByRole('button', { name: /Book 2 by An Author/ })).toBeTruthy()
  })

  it('opens the whole story of a request from its cover, and closes it again', () => {
    hooks.requests = [req({ id: 3, status: 'Failed', failureReason: 'No release found' })]
    renderPage()

    fireEvent.click(screen.getByRole('button', { name: /Book 3 by An Author/ }))

    const sheet = screen.getByRole('dialog', { name: 'Book 3' })
    expect(within(sheet).getByText('No release found')).toBeTruthy()
    expect(within(sheet).getByRole('button', { name: 'Search again' })).toBeTruthy()

    fireEvent.click(within(sheet).getByRole('button', { name: 'Close' }))
    expect(screen.queryByRole('dialog')).toBeNull()
  })

  it('does the thing a button in the sheet says', () => {
    hooks.requests = [req({ id: 3, status: 'Failed' })]
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Book 3 by An Author/ }))

    fireEvent.click(screen.getByRole('button', { name: 'Search again' }))

    expect(hooks.actOnRequest).toHaveBeenCalledWith(3, 'search-again')
  })

  it('shows only your own requests until you ask for everyone, and names who asked', () => {
    hooks.requests = [req({ id: 1, requestedByUsername: 'sam' }), req({ id: 2, requestedByUsername: 'pat', title: 'Pats Book' })]
    renderPage()

    expect(screen.queryByRole('button', { name: /Pats Book/ })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Everyone' }))

    expect(screen.getByRole('button', { name: /Pats Book/ })).toBeTruthy()
    expect(screen.getByText('pat')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Pats Book by/ }))
    expect(within(screen.getByRole('dialog')).getByText('Requested by pat')).toBeTruthy()
  })

  it('explains a book that is not out yet, with its date, and offers a check instead of a wishlist', () => {
    hooks.requests = [
      req({ id: 4, status: 'Upcoming', releaseDate: '2099-11-05T00:00:00+00:00', nextSearchAt: '2099-01-01T12:00:00Z', wishlisted: false }),
    ]
    renderPage()
    expect(screen.getByText('Upcoming releases')).toBeTruthy()
    expect(screen.getByText('Out Nov 5, 2099')).toBeTruthy()

    fireEvent.click(screen.getByRole('button', { name: /Book 4 by An Author/ }))
    const sheet = screen.getByRole('dialog', { name: 'Book 4' })

    expect(within(sheet).getByText(/Not out yet: due Nov 5, 2099/)).toBeTruthy()
    expect(within(sheet).queryByRole('button', { name: 'Keep trying' })).toBeNull()
    expect(within(sheet).queryByRole('button', { name: 'Search again' })).toBeNull()
    fireEvent.click(within(sheet).getByRole('button', { name: 'Check now' }))
    expect(hooks.actOnRequest).toHaveBeenCalledWith(4, 'search-again')
  })

  it('shows what the book is about and how far along the request is', async () => {
    hooks.get.mockResolvedValue({
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
    })
    hooks.requests = [req({ id: 6, status: 'Downloading', downloadProgressPercent: 0.4, downloadSizeBytes: 2e9, downloadClientName: 'Deluge' })]
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Book 6 by An Author/ }))
    const sheet = screen.getByRole('dialog', { name: 'Book 6' })

    expect(await within(sheet).findByText('A sweeping tale.')).toBeTruthy()
    expect(within(sheet).getByText('Jim Dale')).toBeTruthy()
    expect(within(sheet).getByText('10h 0m')).toBeTruthy()
    expect(within(sheet).getByText(/Requested .* by sam/)).toBeTruthy()
    expect(within(sheet).getByText(/40% of 2\.0 GB via Deluge/)).toBeTruthy()

    const steps = within(within(sheet).getByRole('list', { name: 'Request progress' })).getAllByRole('listitem')
    expect(steps.map((s) => s.textContent)).toEqual(['Requested', 'Searching', 'Downloading', 'Processing', 'Ready'].map((l, i) => (i < 2 ? `✓${l}` : l)))
    expect(steps.filter((s) => s.getAttribute('aria-current') === 'step').map((s) => s.textContent)).toEqual(['Downloading'])
  })

  it('still opens when the description cannot be found, and has no steps for a request that is not on the road', () => {
    hooks.requests = [req({ id: 7, status: 'Failed', failureReason: 'No indexer had it' })]
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: /Book 7 by An Author/ }))
    const sheet = screen.getByRole('dialog', { name: 'Book 7' })

    expect(within(sheet).getByText(/Requested .* by sam/)).toBeTruthy()
    expect(within(sheet).queryByRole('list', { name: 'Request progress' })).toBeNull()
  })

  it('does not let someone manage another persons request', () => {
    hooks.requests = [req({ id: 2, requestedByUsername: 'pat', status: 'Failed' })]
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Everyone' }))
    fireEvent.click(screen.getByRole('button', { name: /Book 2 by/ }))

    expect(screen.queryByRole('button', { name: 'Cancel request' })).toBeNull()
  })

  it('lets an admin manage anyones request', () => {
    hooks.user = { username: 'root', role: 'Admin' }
    hooks.requests = [req({ id: 2, requestedByUsername: 'pat', status: 'Failed' })]
    renderPage()
    fireEvent.click(screen.getByRole('button', { name: 'Everyone' }))
    fireEvent.click(screen.getByRole('button', { name: /Book 2 by/ }))

    expect(screen.getByRole('button', { name: 'Cancel request' })).toBeTruthy()
  })

  it('shows a long section in part, and the rest on request', () => {
    hooks.requests = Array.from({ length: 25 }, (_, i) => req({ id: i + 1 }))
    renderPage()

    expect(screen.getAllByRole('button', { name: /by An Author/ })).toHaveLength(18)
    fireEvent.click(screen.getByRole('button', { name: 'Show 7 more' }))

    expect(screen.getAllByRole('button', { name: /by An Author/ })).toHaveLength(25)
    expect(screen.queryByRole('button', { name: /Show .* more/ })).toBeNull()
  })

  it('says so when there is nothing yet', () => {
    renderPage()
    expect(screen.getByText(/No requests yet/)).toBeTruthy()
  })
})

describe('ListenTile', () => {
  const tile = (over: Partial<React.ComponentProps<typeof ListenTile>> = {}) =>
    render(
      <ul>
        <ListenTile id="b1" hasCover={false} title="Dune" author="Frank Herbert" meta="3h 20m left" active={false} onOpen={() => {}} {...over} />
      </ul>,
    )

  it('shows the book with its author and time left, and opens it when tapped', () => {
    const onOpen = vi.fn()
    tile({ onOpen })

    expect(screen.getByText('Frank Herbert')).toBeTruthy()
    expect(screen.getByText('3h 20m left')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Open Dune by Frank Herbert' }))

    expect(onOpen).toHaveBeenCalledOnce()
  })

  it('shows how far through you are, but not once it is finished', () => {
    const { container, rerender } = tile({ fraction: 0.4 })
    expect((container.querySelector('.listen-tile-bar > span') as HTMLElement).style.width).toBe('40%')

    rerender(
      <ul>
        <ListenTile id="b1" hasCover={false} title="Dune" author={null} meta="Finished" fraction={1} finished active={false} onOpen={() => {}} />
      </ul>,
    )
    expect(container.querySelector('.listen-tile-bar')).toBeNull()
    expect(screen.getByTitle('Finished')).toBeTruthy()
  })

  it('shows your star rating', () => {
    tile({ rating: 4 })
    expect(screen.getByText('★ 4')).toBeTruthy()
  })

  it('marks the book that is playing now', () => {
    const { container } = tile({ active: true })
    expect(container.querySelector('.listen-tile-btn.active')).not.toBeNull()
  })

  it('offers saving the book for offline listening', () => {
    tile()
    expect(screen.getByRole('button', { name: /Download Dune for offline listening/ })).toBeTruthy()
  })
})
