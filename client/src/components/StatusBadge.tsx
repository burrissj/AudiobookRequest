import type { BookRequestStatus } from '../api/types'

const STATUS_LABEL: Record<BookRequestStatus, string> = {
  Requested: 'Requested',
  Searching: 'Searching…',
  Snatched: 'Snatched',
  Downloading: 'Downloading…',
  Processing: 'Processing…',
  Ready: 'Ready to listen',
  Failed: 'Failed',
  PendingApproval: 'Awaiting approval',
  Denied: 'Declined',
  Upcoming: 'Upcoming',
}

const STATUS_CLASS: Record<BookRequestStatus, string> = {
  Requested: 'badge-pending',
  Searching: 'badge-pending',
  Snatched: 'badge-pending',
  Downloading: 'badge-pending',
  Processing: 'badge-pending',
  Ready: 'badge-ok',
  Failed: 'badge-fail',
  PendingApproval: 'badge-pending',
  Denied: 'badge-fail',
  Upcoming: 'badge-upcoming',
}

export function StatusBadge({ status }: { status: BookRequestStatus }) {
  return <span className={`badge ${STATUS_CLASS[status]}`}>{STATUS_LABEL[status]}</span>
}
