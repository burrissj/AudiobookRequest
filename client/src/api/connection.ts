import { ApiError } from './client'

export interface ConnectionProblemInfo {
  /** One plain sentence for the person. */
  summary: string
  /** What the browser actually saw, for diagnosing (shown small). */
  detail: string
}

/**
 * Turns a failed request into something a person can act on. The important distinction: "the server
 * answered and said no" versus "nothing (or something else) answered" -- the second one must never be
 * mistaken for "this server isn't set up yet".
 */
export function describeConnectionProblem(err: unknown): ConnectionProblemInfo {
  if (err instanceof ApiError) {
    return {
      summary: `The server answered with an error (${err.status}).`,
      detail: `HTTP ${err.status}${err.message ? `: ${err.message}` : ''}`,
    }
  }
  if (err instanceof SyntaxError) {
    return {
      summary: "Something answered, but it wasn't Audiobook Request. A VPN, proxy or login page may be in the way.",
      detail: 'The response was not valid JSON.',
    }
  }
  if (typeof navigator !== 'undefined' && navigator.onLine === false) {
    return { summary: "This device is offline.", detail: 'The browser reports no network connection.' }
  }
  return {
    summary: "Couldn't reach the server.",
    detail: err instanceof Error ? `${err.name}: ${err.message}` : 'The request failed before any response arrived.',
  }
}
