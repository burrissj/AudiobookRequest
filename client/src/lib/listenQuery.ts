/**
 * The views of the Listen tab. "saved" is the books on this device; "authors" and "series" sort the library into piles
 * (asked of the server by their own calls, not through libraryParams); the rest are lists of books asked of the server.
 */
export type ListenFilter = 'resume' | 'recent' | 'all' | 'rated' | 'finished' | 'saved' | 'tag' | 'authors' | 'series'

/**
 * What to ask the server for a view of the library. A search always looks through everything (so a book is found
 * whichever list it is in); "tag" is the person's own list for one chosen tag.
 */
export function libraryParams(filter: ListenFilter, term: string, tag: string | null, limit: number, start = 0): URLSearchParams {
  const params = new URLSearchParams({ limit: String(limit) })
  if (term) {
    params.set('filter', 'all')
    params.set('search', term)
  } else if (filter === 'tag' && tag) {
    params.set('filter', 'all')
    params.set('tag', tag)
  } else {
    params.set('filter', filter === 'tag' || filter === 'saved' || filter === 'authors' || filter === 'series' ? 'all' : filter)
  }
  if (start > 0) params.set('start', String(start))
  return params
}
