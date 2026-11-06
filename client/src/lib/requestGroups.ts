import type { BookRequestDto, BookRequestStatus } from '../api/types'
import { formatReleaseDay } from './release'

export type RequestGroupKey = 'attention' | 'approval' | 'progress' | 'upcoming' | 'ready'

export interface RequestGroup {
  key: RequestGroupKey
  title: string
  items: BookRequestDto[]
}

const GROUP_OF: Record<BookRequestStatus, RequestGroupKey> = {
  Failed: 'attention',
  Denied: 'attention',
  PendingApproval: 'approval',
  Requested: 'progress',
  Searching: 'progress',
  Snatched: 'progress',
  Downloading: 'progress',
  Processing: 'progress',
  Upcoming: 'upcoming',
  Ready: 'ready',
}

// What needs a person's eyes comes first; the (usually longest) list of finished books comes last.
const ORDER: { key: RequestGroupKey; title: string }[] = [
  { key: 'attention', title: 'Needs attention' },
  { key: 'approval', title: 'Awaiting approval' },
  { key: 'progress', title: 'On its way' },
  { key: 'upcoming', title: 'Upcoming releases' },
  { key: 'ready', title: 'Ready to listen' },
]

/** Sorts requests into the sections of the Requests page, in display order, leaving out sections with nothing in them. */
export function groupRequests(requests: BookRequestDto[]): RequestGroup[] {
  return ORDER.map(({ key, title }) => ({ key, title, items: requests.filter((r) => GROUP_OF[r.status] === key) })).filter((g) => g.items.length > 0)
}

const WORDS: Record<BookRequestStatus, string> = {
  Requested: 'Requested',
  Searching: 'Searching…',
  Snatched: 'Snatched',
  Downloading: 'Downloading…',
  Processing: 'Processing…',
  Ready: 'Ready',
  Failed: 'Failed',
  PendingApproval: 'Awaiting approval',
  Denied: 'Declined',
  Upcoming: 'Upcoming',
}

export type StatusTone = 'ok' | 'fail' | 'pending' | 'upcoming'

/** A short pill for a cover: "Downloading 42%", "Ready", "Failed"… */
export function coverStatus(r: BookRequestDto): { text: string; tone: StatusTone } {
  const tone: StatusTone =
    r.status === 'Ready' ? 'ok' : r.status === 'Failed' || r.status === 'Denied' ? 'fail' : r.status === 'Upcoming' ? 'upcoming' : 'pending'
  if (r.status === 'Downloading' && r.downloadProgressPercent != null) {
    return { text: `Downloading ${Math.round(r.downloadProgressPercent * 100)}%`, tone }
  }
  if (r.status === 'Upcoming' && r.releaseDate) {
    return { text: `Out ${formatReleaseDay(r.releaseDate)}`, tone }
  }
  return { text: WORDS[r.status], tone }
}
