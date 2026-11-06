// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ImportCandidate, ImportFolder, ImportIdentify, ImportJob, ImportMatch, ImportPreview, ImportScan } from '../../api/types'
import { AdminImport } from './AdminImport'

const calls = vi.hoisted(() => ({ get: vi.fn(), post: vi.fn(), put: vi.fn(), del: vi.fn() }))
vi.mock('../../api/client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('../../api/client')>()
  return { ...actual, api: { ...actual.api, get: calls.get, post: calls.post, put: calls.put, delete: calls.del } }
})

afterEach(cleanup)

const candidate = (over: Partial<ImportCandidate> = {}): ImportCandidate => ({
  id: 'c1',
  kind: 'Folder',
  name: 'Frank Herbert - Dune',
  relativePath: 'Frank Herbert - Dune',
  fileCount: 3,
  totalBytes: 5 * 1024 * 1024,
  imported: false,
  importedAs: null,
  ...over,
})

const match = (over: Partial<ImportMatch> = {}): ImportMatch => ({
  asin: 'B002V1OF70',
  title: 'Dune',
  author: 'Frank Herbert',
  series: 'The Dune Sequence',
  seriesNumber: '12',
  coverUrl: 'http://img/dune.jpg',
  runtimeMinutes: 1262,
  narrators: ['Scott Brick'],
  score: 9,
  lengthNote: null,
  ...over,
})

const identify = (matches: ImportMatch[]): ImportIdentify => ({
  id: 'c1',
  name: 'Frank Herbert - Dune',
  files: [],
  totalSeconds: 75720,
  guessTitle: 'Dune',
  guessAuthor: 'Frank Herbert',
  query: 'Dune',
  matches,
  problems: [],
})

const folder = (over: Partial<ImportFolder> = {}): ImportFolder => ({
  folder: 'D:\\To sort',
  libraryPath: 'D:\\Library',
  namingPattern: '{Author}/{Title}/{Title}',
  problem: null,
  ...over,
})

interface World {
  folder: ImportFolder
  scan: ImportScan
  jobs: ImportJob[]
  matches: ImportMatch[]
  preview: ImportPreview
}

const job = (over: Partial<ImportJob> = {}): ImportJob => ({
  jobId: 'j1',
  candidateId: 'c1',
  name: 'Frank Herbert - Dune',
  status: 'Succeeded',
  stage: 'Done',
  error: null,
  result: { finalPath: 'D:\\Library\\Frank Herbert\\Dune\\Dune.m4b', movedOriginals: false, note: null },
  queuedAt: '2026-10-13T12:00:00Z',
  startedAt: '2026-10-13T12:00:01Z',
  finishedAt: '2026-10-13T12:00:09Z',
  ...over,
})

let world: World

beforeEach(() => {
  world = {
    folder: folder(),
    scan: { folder: 'D:\\To sort', candidates: [candidate()] },
    jobs: [],
    matches: [match()],
    preview: { relativePath: 'Frank Herbert\\Dune\\Dune.m4b', fullPath: 'D:\\Library\\Frank Herbert\\Dune\\Dune.m4b', fileExists: false, alreadyInJellyfin: false },
  }
  for (const m of Object.values(calls)) m.mockReset()
  calls.get.mockImplementation(async (url: string) => {
    if (url === '/api/admin/import') return world.folder
    if (url === '/api/admin/import/scan') return world.scan
    if (url === '/api/admin/import/jobs') return world.jobs
    if (url.startsWith('/api/admin/import/preview')) return world.preview
    throw new Error(`unexpected GET ${url}`)
  })
  calls.post.mockImplementation(async (url: string) => {
    if (url.endsWith('/identify')) return identify(world.matches)
    if (url.endsWith('/import')) {
      world.jobs = [...world.jobs, job({ status: 'Queued', stage: 'Waiting in line', result: null, startedAt: null, finishedAt: null })]
      return {}
    }
    throw new Error(`unexpected POST ${url}`)
  })
})

describe('Import tool: the book list', () => {
  it('lists the books to sort and how many there are', async () => {
    world.scan.candidates = [candidate(), candidate({ id: 'c2', name: 'loose book', relativePath: 'loose book.m4a', fileCount: 1, kind: 'File' })]
    render(<AdminImport />)

    expect(await screen.findByText('2 books to sort.')).toBeTruthy()
    expect(screen.getByText('Frank Herbert - Dune')).toBeTruthy()
    expect(screen.getByText('loose book')).toBeTruthy()
  })

  it('hides books already imported until asked, and says how many there are', async () => {
    world.scan.candidates = [candidate(), candidate({ id: 'c2', name: 'Old Book', relativePath: 'Old Book', imported: true, importedAs: 'x' })]
    render(<AdminImport />)

    expect(await screen.findByText('1 book to sort, 1 already imported.')).toBeTruthy()
    expect(screen.queryByText('Old Book')).toBeNull()

    fireEvent.click(screen.getByLabelText('Show the ones already imported'))

    expect(screen.getByText('Old Book')).toBeTruthy()
    expect(screen.getByText('Imported')).toBeTruthy()
  })

  it('says so when the folder has no audiobooks in it', async () => {
    world.scan.candidates = []
    render(<AdminImport />)

    expect(await screen.findByText(/No audiobooks found in that folder/)).toBeTruthy()
  })

  it('shows why importing cannot work yet, instead of hiding the tool', async () => {
    world.folder = folder({ folder: null, problem: 'Set your library folder in Settings first.' })
    render(<AdminImport />)

    expect(await screen.findByText('Set your library folder in Settings first.')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Scan this folder' })).toBeTruthy()
  })

  it('saves a folder typed in, with the quotes of a pasted path left for the server to strip', async () => {
    world.folder = folder({ folder: null })
    calls.put.mockResolvedValue(folder({ folder: 'D:\\New' }))
    render(<AdminImport />)

    fireEvent.change(await screen.findByLabelText(/Folder to import from/), { target: { value: '"D:\\New"' } })
    fireEvent.click(screen.getByRole('button', { name: 'Scan this folder' }))

    await waitFor(() => expect(calls.put).toHaveBeenCalledWith('/api/admin/import/folder', { path: '"D:\\New"' }))
  })

  it('shows what went wrong when the folder cannot be read', async () => {
    calls.get.mockImplementation(async (url: string) => {
      if (url === '/api/admin/import') return world.folder
      if (url === '/api/admin/import/job') return undefined
      const { ApiError } = await import('../../api/client')
      throw new ApiError(400, "That folder doesn't exist.")
    })
    render(<AdminImport />)

    expect(await screen.findByText("That folder doesn't exist.")).toBeTruthy()
  })
})

describe('Import tool: identifying and importing one book', () => {
  async function openFirstBook() {
    render(<AdminImport />)
    fireEvent.click(await screen.findByRole('button', { name: 'Start with the first one' }))
  }

  it('offers the matches, best first, each with its length', async () => {
    world.matches = [match(), match({ asin: 'B2', title: 'Dune Messiah', runtimeMinutes: 537, score: 3 })]
    await openFirstBook()

    expect(await screen.findByText('Dune Messiah')).toBeTruthy()
    expect(screen.getByText('Best match')).toBeTruthy()
    expect(screen.getByText('21h 2m')).toBeTruthy()
    expect(screen.getAllByRole('button', { name: 'Use this' })).toHaveLength(2)
  })

  it('carries the chosen match into the confirmation, with where it will be filed', async () => {
    await openFirstBook()
    fireEvent.click(await screen.findByRole('button', { name: /Use this|Best match/ }))

    expect(await screen.findByText(/Frank Herbert\\Dune\\Dune\.m4b/)).toBeTruthy()
    expect((screen.getAllByRole('textbox')[0] as HTMLInputElement).value).toBe('Dune')
  })

  it('lets you type the details yourself when none of the matches is right', async () => {
    world.matches = []
    await openFirstBook()

    fireEvent.click(await screen.findByRole('button', { name: /None of these/ }))

    expect(await screen.findByRole('button', { name: 'Import this book' })).toBeTruthy()
  })

  it('adds the book to the queue with the details and original-files choice that were set, then goes on to the next book', async () => {
    world.scan.candidates = [candidate(), candidate({ id: 'c2', name: 'loose book', relativePath: 'loose book', fileCount: 1 })]
    await openFirstBook()
    fireEvent.click(await screen.findByRole('button', { name: /Use this|Best match/ }))
    await screen.findByText(/Frank Herbert\\Dune\\Dune\.m4b/)

    fireEvent.change(screen.getByRole('combobox'), { target: { value: 'move' } })
    fireEvent.click(screen.getByRole('button', { name: 'Import this book' }))

    await waitFor(() =>
      expect(calls.post).toHaveBeenCalledWith(
        '/api/admin/import/candidates/c1/import',
        expect.objectContaining({ title: 'Dune', author: 'Frank Herbert', sourceAction: 'move', overwrite: false, coverUrl: 'http://img/dune.jpg' }),
      ),
    )
    // straight on to the next book, with the first one visible in the queue
    expect(await screen.findByText('loose book')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Skip this book' })).toBeTruthy()
    expect(await screen.findByLabelText('Conversion queue')).toBeTruthy()
    expect(screen.getByText('Waiting its turn (number 1 in line)')).toBeTruthy()
  })

  it('goes back to the list, saying how many are in the queue, after the last book is queued', async () => {
    await openFirstBook()
    fireEvent.click(await screen.findByRole('button', { name: /Use this|Best match/ }))
    await screen.findByText(/Frank Herbert\\Dune\\Dune\.m4b/)
    fireEvent.click(screen.getByRole('button', { name: 'Import this book' }))

    expect(await screen.findByText(/0 books to sort, 1 in the queue/)).toBeTruthy()
    expect(screen.getByText('In the queue')).toBeTruthy()
  })

  it('will not queue a book over an existing file until it is explicitly allowed', async () => {
    world.preview = { ...world.preview, fileExists: true }
    await openFirstBook()
    fireEvent.click(await screen.findByRole('button', { name: /Use this|Best match/ }))

    const go = (await screen.findByRole('button', { name: 'Import this book' })) as HTMLButtonElement
    const allow = await screen.findByRole('checkbox') // appears once the preview has found the file there
    expect(go.disabled).toBe(true)

    fireEvent.click(allow)
    await waitFor(() => expect(go.disabled).toBe(false))
  })
})

describe('Import tool: the conversion queue', () => {
  const two = () => {
    world.scan.candidates = [candidate(), candidate({ id: 'c2', name: 'loose book', relativePath: 'loose book', fileCount: 1 })]
    world.jobs = [
      job({ jobId: 'j1', candidateId: 'c1', status: 'Running', stage: 'Converting', result: null, finishedAt: null }),
      job({ jobId: 'j2', candidateId: 'c2', name: 'loose book', status: 'Queued', stage: 'Waiting in line', result: null, startedAt: null, finishedAt: null }),
    ]
  }

  it('shows what is converting and what is waiting, and keeps those books out of the list to sort', async () => {
    two()
    render(<AdminImport />)

    expect(await screen.findByText(/Converting…/)).toBeTruthy()
    expect(screen.getByText('Waiting its turn (number 1 in line)')).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Stop' })).toBeTruthy()
    expect(screen.getByRole('button', { name: 'Remove' })).toBeTruthy()
    expect(await screen.findByText(/0 books to sort, 2 in the queue/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'Start with the first one' })).toBeNull()
  })

  it('stops the book converting, and takes a waiting one out of line', async () => {
    two()
    calls.del.mockResolvedValue(undefined)
    render(<AdminImport />)

    fireEvent.click(await screen.findByRole('button', { name: 'Stop' }))
    await waitFor(() => expect(calls.del).toHaveBeenCalledWith('/api/admin/import/jobs/j1'))

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }))
    await waitFor(() => expect(calls.del).toHaveBeenCalledWith('/api/admin/import/jobs/j2'))
  })

  it('shows a book that failed, with the reason, and reopens it to try again', async () => {
    world.jobs = [job({ status: 'Failed', stage: 'Failed', error: "'01.mp3' can't be read as audio.", result: null })]
    render(<AdminImport />)

    expect(await screen.findByText(/can't be read as audio/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('button', { name: /None of these/ })).toBeTruthy() // back at identifying that book
  })

  it('shows where a finished book was filed, and clears the finished ones', async () => {
    world.jobs = [job()]
    calls.del.mockResolvedValue({ cleared: 1 })
    render(<AdminImport />)

    expect(await screen.findByText(/D:\\Library\\Frank Herbert\\Dune\\Dune\.m4b/)).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'Clear finished' }))

    await waitFor(() => expect(calls.del).toHaveBeenCalledWith('/api/admin/import/jobs'))
  })

  it('refreshes the folder list when a book finishes while the page is open', async () => {
    world.scan.candidates = [candidate()]
    world.jobs = [job({ status: 'Running', stage: 'Converting', result: null, finishedAt: null })]
    render(<AdminImport />)
    await screen.findByText(/Converting…/)
    const scansBefore = calls.get.mock.calls.filter(([u]) => u === '/api/admin/import/scan').length

    world.jobs = [job()]
    world.scan = { ...world.scan, candidates: [candidate({ imported: true })] }

    await waitFor(() => expect(calls.get.mock.calls.filter(([u]) => u === '/api/admin/import/scan').length).toBeGreaterThan(scansBefore), { timeout: 5000 })
  })
})
