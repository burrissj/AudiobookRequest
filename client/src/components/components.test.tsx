// @vitest-environment jsdom
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { DownloadButton } from './DownloadButton'
import { MarqueeText } from './MarqueeText'
import { StarRating } from './StarRating'
import { RequireAuth } from '../auth/RequireAuth'
import { SetupGate } from '../setup/SetupGate'

afterEach(cleanup)

describe('StarRating', () => {
  it('shows five stars with the chosen rating marked', () => {
    render(<StarRating value={3} onChange={() => {}} />)

    const stars = screen.getAllByRole('radio')
    expect(stars).toHaveLength(5)
    expect(stars.map((s) => s.getAttribute('aria-checked'))).toEqual(['false', 'false', 'true', 'false', 'false'])
  })

  it('rates when a star is tapped', () => {
    const onChange = vi.fn()
    render(<StarRating value={null} onChange={onChange} />)

    fireEvent.click(screen.getByRole('radio', { name: '4 stars' }))

    expect(onChange).toHaveBeenCalledWith(4)
  })

  it('takes the rating back when the chosen star is tapped again', () => {
    const onChange = vi.fn()
    render(<StarRating value={4} onChange={onChange} />)

    fireEvent.click(screen.getByRole('radio', { name: '4 stars' }))

    expect(onChange).toHaveBeenCalledWith(0)
  })

  it('names itself for screen readers, and can be switched off', () => {
    render(<StarRating value={2} onChange={() => {}} disabled label="Your rating" />)

    expect(screen.getByRole('radiogroup', { name: 'Your rating' })).toBeTruthy()
    expect(screen.getAllByRole('radio').every((s) => (s as HTMLButtonElement).disabled)).toBe(true)
  })

  it('uses the singular for one star', () => {
    render(<StarRating value={null} onChange={() => {}} />)
    expect(screen.getByRole('radio', { name: '1 star' })).toBeTruthy()
  })
})

describe('MarqueeText', () => {
  let observed: (() => void) | null = null

  beforeEach(() => {
    observed = null
    vi.stubGlobal(
      'ResizeObserver',
      class {
        constructor(cb: () => void) {
          observed = cb
        }
        observe() {}
        disconnect() {}
      },
    )
  })
  afterEach(() => vi.unstubAllGlobals())

  function sizes(textWidth: number, boxWidth: number) {
    Object.defineProperty(HTMLElement.prototype, 'scrollWidth', {
      configurable: true,
      get() {
        return (this as HTMLElement).classList.contains('marquee-inner') ? textWidth : 0
      },
    })
    Object.defineProperty(HTMLElement.prototype, 'clientWidth', {
      configurable: true,
      get() {
        return (this as HTMLElement).classList.contains('marquee') ? boxWidth : 0
      },
    })
  }

  it('leaves text that fits perfectly still', () => {
    sizes(100, 200)
    render(<MarqueeText text="Short" />)
    act(() => observed?.())

    expect(screen.getByText('Short').getAttribute('data-scroll')).toBe('false')
  })

  it('scrolls text that is too long, by exactly how much is hidden', () => {
    sizes(500, 200)
    render(<MarqueeText text="The Throne of Broken Gods (2 of 2) [Dramatized Adaptation]" />)
    act(() => observed?.())

    const inner = screen.getByText(/Throne of Broken Gods/)
    expect(inner.getAttribute('data-scroll')).toBe('true')
    expect(inner.style.getPropertyValue('--marquee-shift')).toBe('-300px')
    expect(parseFloat(inner.style.getPropertyValue('--marquee-seconds'))).toBeGreaterThanOrEqual(5)
  })

  it('does not scroll for a pixel or two of rounding', () => {
    sizes(201, 200)
    render(<MarqueeText text="Nearly fits" />)
    act(() => observed?.())

    expect(screen.getByText('Nearly fits').getAttribute('data-scroll')).toBe('false')
  })

  it('shows the full title on hover when it is cut off', () => {
    sizes(500, 200)
    const { container } = render(<MarqueeText text="A very long title indeed" />)
    act(() => observed?.())

    expect(container.querySelector('.marquee')?.getAttribute('title')).toBe('A very long title indeed')
  })

  it('renders as the element asked for', () => {
    sizes(10, 200)
    const { container } = render(<MarqueeText as="strong" text="Bold" />)
    expect(container.querySelector('strong.marquee')).toBeTruthy()
  })

  it('still shows the text where ResizeObserver does not exist', () => {
    vi.unstubAllGlobals()
    vi.stubGlobal('ResizeObserver', undefined)
    render(<MarqueeText text="Plain" />)
    expect(screen.getByText('Plain')).toBeTruthy()
  })
})

// ---- DownloadButton, with the download manager replaced by something we control ----

const manager = vi.hoisted(() => ({
  snapshot: { supported: true, saved: [] as { id: string }[], active: [] as { id: string; state: string; doneBytes: number; totalBytes: number; message?: string }[], usage: null, unavailable: [] as string[] },
  enqueueDownload: vi.fn(),
  cancelDownload: vi.fn(),
  removeSaved: vi.fn(),
}))
vi.mock('../offline/downloadManager', () => ({
  useOffline: () => manager.snapshot,
  enqueueDownload: manager.enqueueDownload,
  cancelDownload: manager.cancelDownload,
  removeSaved: manager.removeSaved,
}))

describe('DownloadButton', () => {
  beforeEach(() => {
    manager.snapshot = { supported: true, saved: [], active: [], usage: null, unavailable: [] }
    manager.enqueueDownload.mockReset()
    manager.cancelDownload.mockReset()
    manager.removeSaved.mockReset()
  })

  it('offers a download, and starts it', () => {
    render(<DownloadButton itemId="b1" title="Dune" />)

    fireEvent.click(screen.getByRole('button', { name: /Download Dune/ }))

    expect(manager.enqueueDownload).toHaveBeenCalledWith('b1', 'Dune')
  })

  it('shows progress and cancels on tap', () => {
    manager.snapshot.active = [{ id: 'b1', state: 'downloading', doneBytes: 50, totalBytes: 200 }]
    render(<DownloadButton itemId="b1" title="Dune" />)

    const button = screen.getByRole('button', { name: /Downloading 25%/ })
    expect(button.textContent).toBe('25%')
    fireEvent.click(button)

    expect(manager.cancelDownload).toHaveBeenCalledWith('b1')
  })

  it('says a download is waiting its turn', () => {
    manager.snapshot.active = [{ id: 'b1', state: 'queued', doneBytes: 0, totalBytes: 0 }]
    render(<DownloadButton itemId="b1" title="Dune" />)
    expect(screen.getByRole('button', { name: /Queued/ }).textContent).toBe('Queued')
  })

  it('shows a failure with its reason and lets it be retried', () => {
    manager.snapshot.active = [{ id: 'b1', state: 'error', doneBytes: 0, totalBytes: 0, message: 'Not enough free space on this device for this book.' }]
    render(<DownloadButton itemId="b1" title="Dune" />)

    expect(screen.getByText(/Not enough free space/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: /Retry/ }))
    expect(manager.enqueueDownload).toHaveBeenCalledWith('b1', 'Dune')
  })

  it('shows a saved book, and removes it only after asking', () => {
    manager.snapshot.saved = [{ id: 'b1' }]
    const confirm = vi.spyOn(window, 'confirm')
    render(<DownloadButton itemId="b1" title="Dune" />)
    const button = screen.getByRole('button', { name: /saved for offline listening/ })

    confirm.mockReturnValueOnce(false)
    fireEvent.click(button)
    expect(manager.removeSaved).not.toHaveBeenCalled()

    confirm.mockReturnValueOnce(true)
    fireEvent.click(button)
    expect(manager.removeSaved).toHaveBeenCalledWith('b1')
    confirm.mockRestore()
  })

  it('says so, instead of a useless button, for a book that has to be converted', () => {
    manager.snapshot.unavailable = ['b1']
    render(<DownloadButton itemId="b1" title="Dune" />)

    expect(screen.queryByRole('button')).toBeNull()
    expect(screen.getByText(/Can’t be saved/)).toBeTruthy()
  })

  it('renders nothing where this browser cannot save books', () => {
    manager.snapshot.supported = false
    const { container } = render(<DownloadButton itemId="b1" title="Dune" />)
    expect(container.innerHTML).toBe('')
  })
})

// ---- the Tailscale bug: a failed connection must never be mistaken for "not set up" or "signed out" ----

const setup = vi.hoisted(() => ({ value: { setupCompleted: false, loading: false, problem: null as null | { summary: string; detail: string }, retry: vi.fn(), markCompleted: vi.fn() } }))
vi.mock('../setup/useSetup', () => ({ useSetup: () => setup.value }))

const auth = vi.hoisted(() => ({ value: { user: null as null | { username: string; role: string }, loading: false, problem: null as null | { summary: string; detail: string }, retry: vi.fn(), login: vi.fn(), logout: vi.fn() } }))
vi.mock('../auth/useAuth', () => ({ useAuth: () => auth.value }))

function renderWithRoutes(ui: React.ReactNode, path = '/') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/setup" element={<p>SETUP PAGE</p>} />
        <Route path="/login" element={<p>LOGIN PAGE</p>} />
        <Route path="*" element={ui} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('SetupGate', () => {
  beforeEach(() => {
    setup.value = { setupCompleted: false, loading: false, problem: null, retry: vi.fn(), markCompleted: vi.fn() }
  })

  it('sends a server that really is not set up to Setup', () => {
    renderWithRoutes(<SetupGate>APP</SetupGate>)
    expect(screen.getByText('SETUP PAGE')).toBeTruthy()
  })

  it('does NOT send anyone to Setup just because the server could not be reached', () => {
    setup.value.problem = { summary: "Couldn't reach the server.", detail: 'TypeError: Failed to fetch' }

    renderWithRoutes(<SetupGate>APP</SetupGate>)

    expect(screen.queryByText('SETUP PAGE')).toBeNull()
    expect(screen.getByText("Can't connect")).toBeTruthy()
    expect(screen.getByText(/Failed to fetch/)).toBeTruthy()
  })

  it('shows the app when set up and reachable', () => {
    setup.value.setupCompleted = true
    renderWithRoutes(<SetupGate>APP</SetupGate>)
    expect(screen.getByText('APP')).toBeTruthy()
  })

  it('retries when asked', () => {
    setup.value.problem = { summary: 'x', detail: 'y' }
    renderWithRoutes(<SetupGate>APP</SetupGate>)

    fireEvent.click(screen.getByRole('button', { name: /Try again/ }))

    expect(setup.value.retry).toHaveBeenCalled()
  })
})

describe('RequireAuth', () => {
  beforeEach(() => {
    auth.value = { user: null, loading: false, problem: null, retry: vi.fn(), login: vi.fn(), logout: vi.fn() }
  })

  it('sends someone who is signed out to the login page', () => {
    renderWithRoutes(<RequireAuth>PRIVATE</RequireAuth>)
    expect(screen.getByText('LOGIN PAGE')).toBeTruthy()
  })

  it('does NOT send anyone to the login page just because the server could not be reached', () => {
    auth.value.problem = { summary: 'Something answered, but it was not the app.', detail: 'not json' }

    renderWithRoutes(<RequireAuth>PRIVATE</RequireAuth>)

    expect(screen.queryByText('LOGIN PAGE')).toBeNull()
    expect(screen.getByText("Can't connect")).toBeTruthy()
  })

  it('shows the page to someone signed in', () => {
    auth.value.user = { username: 'JB', role: 'User' }
    renderWithRoutes(<RequireAuth>PRIVATE</RequireAuth>)
    expect(screen.getByText('PRIVATE')).toBeTruthy()
  })

  it('keeps showing the page if a later check hiccups while signed in', () => {
    auth.value.user = { username: 'JB', role: 'User' }
    auth.value.problem = { summary: 'x', detail: 'y' }
    renderWithRoutes(<RequireAuth>PRIVATE</RequireAuth>)
    expect(screen.getByText('PRIVATE')).toBeTruthy()
  })
})
