import type { BookRequestStatus } from '../api/types'

const STEPS = ['Requested', 'Searching', 'Downloading', 'Processing', 'Ready'] as const

/** Which step a request is on, or null for the ones that aren't on the road (waiting for approval or release, failed, declined). */
function stepIndex(status: BookRequestStatus): number | null {
  switch (status) {
    case 'Requested':
      return 0
    case 'Searching':
      return 1
    case 'Snatched':
    case 'Downloading':
      return 2
    case 'Processing':
      return 3
    case 'Ready':
      return 4
    default:
      return null
  }
}

/** The road a request travels, with the step it is on marked: Requested, Searching, Downloading, Processing, Ready. */
export function RequestSteps({ status }: { status: BookRequestStatus }) {
  const current = stepIndex(status)
  if (current === null) return null
  return (
    <ol className="request-steps" aria-label="Request progress">
      {STEPS.map((label, i) => (
        <li key={label} className={i < current ? 'step-done' : i === current ? 'step-current' : ''} aria-current={i === current ? 'step' : undefined}>
          <span className="step-dot" aria-hidden="true">
            {i < current || (i === current && status === 'Ready') ? '✓' : ''}
          </span>
          <span className="step-label">{label}</span>
        </li>
      ))}
    </ol>
  )
}
