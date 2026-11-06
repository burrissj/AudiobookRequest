import type { BookRequestDto } from '../api/types'

/** True when Chaptarr downloaded a request's files but refuses to import them (typically matched to a different catalog entry). */
export function isImportBlocked(request: BookRequestDto): boolean {
  return (
    !!request.diagnostic &&
    /^Import (blocked|failed)/.test(request.diagnostic) &&
    ['Downloading', 'Snatched', 'Searching'].includes(request.status)
  )
}
