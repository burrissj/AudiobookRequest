import { afterEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from './client'
import { describeConnectionProblem } from './connection'

describe('describeConnectionProblem', () => {
  afterEach(() => vi.unstubAllGlobals())

  it('says so plainly when the server answered with an error', () => {
    const p = describeConnectionProblem(new ApiError(502, 'Bad Gateway'))
    expect(p.summary).toContain('502')
    expect(p.detail).toBe('HTTP 502: Bad Gateway')
  })

  it('recognises something other than the app answering (a proxy or login page)', () => {
    const p = describeConnectionProblem(new SyntaxError('Unexpected token < in JSON'))
    expect(p.summary).toContain("wasn't Audiobook Request")
  })

  it('recognises an offline device', () => {
    vi.stubGlobal('navigator', { onLine: false })
    expect(describeConnectionProblem(new TypeError('Failed to fetch')).summary).toContain('offline')
  })

  it('treats a failed request with no response as unreachable, keeping the browser message for diagnosis', () => {
    vi.stubGlobal('navigator', { onLine: true })
    const p = describeConnectionProblem(new TypeError('Failed to fetch'))
    expect(p.summary).toContain("Couldn't reach the server")
    expect(p.detail).toContain('Failed to fetch')
  })

  it('copes with errors that are not Error objects', () => {
    vi.stubGlobal('navigator', { onLine: true })
    expect(describeConnectionProblem('boom').summary).toContain("Couldn't reach")
  })
})
