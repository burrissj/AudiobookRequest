import { useEffect, useState } from 'react'
import { api } from '../api/client'
import type { AudiobookMetadata } from '../api/types'

// What has been found this visit, so opening a book again (or a sheet closing and reopening) shows it at once.
// Only finds are kept: a miss may just be a slow or missing connection, so it is asked again next time.
const found = new Map<string, AudiobookMetadata>()
const inFlight = new Map<string, Promise<AudiobookMetadata | null>>()

function keyOf(title: string, author: string): string {
  return `${title}|${author}`
}

function lookup(key: string, title: string, author: string): Promise<AudiobookMetadata | null> {
  const known = found.get(key)
  if (known) return Promise.resolve(known)
  const running = inFlight.get(key)
  if (running) return running
  // A book with no author on file is looked up by its title alone.
  const params = new URLSearchParams(author ? { title, author } : { title })
  const request = api
    .get<AudiobookMetadata>(`/api/books/metadata?${params}`)
    .then((data) => {
      // The server answers with an empty shell when it can't find the book: nothing worth keeping.
      const hasContent = data.description != null || data.runtimeMinutes != null || data.narrators.length > 0
      if (hasContent) found.set(key, data)
      return data
    })
    .catch(() => null)
    .finally(() => inFlight.delete(key))
  inFlight.set(key, request)
  return request
}

/** Starts looking a book up early (say, when a finger lands on it) so the page it opens already has the answer. */
export function prefetchAudiobookMetadata(title: string, author: string | null | undefined) {
  if (title) void lookup(keyOf(title, author ?? ''), title, author ?? '')
}

/**
 * The description, narrators, length and so on for a book, looked up by title and author (the author may be missing).
 * What was fetched is stored with the book it belongs to and shown only while that is still the book on screen, so moving
 * to another book starts from "loading" without anything being reset inside an effect. Nothing found (or no signal) is
 * simply `metadata: null`.
 */
export function useAudiobookMetadata(title: string | undefined, author: string | undefined) {
  const key = title ? keyOf(title, author ?? '') : null
  const [result, setResult] = useState<{ key: string; data: AudiobookMetadata | null } | null>(null)

  useEffect(() => {
    if (!key || !title) return
    let current = true
    void lookup(key, title, author ?? '').then((data) => current && setResult({ key, data }))
    return () => {
      current = false
    }
  }, [key, title, author])

  const remembered = key ? (found.get(key) ?? null) : null
  return {
    metadata: remembered ?? (result !== null && result.key === key ? result.data : null),
    loading: key !== null && remembered === null && result?.key !== key,
  }
}
