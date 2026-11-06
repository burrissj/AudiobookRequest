import { createContext, useContext } from 'react'
import type { BookRequestDto, BookSearchResult, ExpandSeriesResponse } from '../api/types'

export type RequestAction = 'search-again' | 'approve' | 'deny' | 'wishlist'

export interface RequestsContextValue {
  requests: BookRequestDto[]
  requestedBookIds: Set<string>
  requestingId: string | null
  error: string | null
  requestBook: (result: BookSearchResultLike) => Promise<void>
  refreshing: boolean
  refreshRequests: () => Promise<void>
  cancellingId: number | null
  cancelRequest: (id: number) => Promise<void>
  actingId: number | null
  actOnRequest: (id: number, action: RequestAction, body?: unknown) => Promise<void>
  /** Requests every book of a series that isn't already owned or requested; null if the call failed. */
  requestSeries: (seriesTitle: string, seriesAsin: string, authorName: string) => Promise<ExpandSeriesResponse | null>
}

// Both search results and recommendations have this same shape; either can be requested.
export interface BookSearchResultLike
  extends Pick<BookSearchResult, 'foreignBookId' | 'title' | 'authorName' | 'seriesTitle' | 'coverUrl'> {
  similarInLibraryTitle?: string | null
  /** When the book comes out; a date still ahead asks the person to confirm before requesting. */
  releaseDate?: string | null
}

export const RequestsContext = createContext<RequestsContextValue | undefined>(undefined)

export function useRequests() {
  const ctx = useContext(RequestsContext)
  if (!ctx) throw new Error('useRequests must be used within a RequestsProvider')
  return ctx
}
