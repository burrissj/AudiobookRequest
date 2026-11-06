import type { ImportJob } from '../../api/types'

interface ImportQueueProps {
  jobs: ImportJob[]
  /** Stops the one converting, or takes a waiting one out of line. */
  onCancel: (job: ImportJob) => void
  /** Reopens a failed or cancelled book so it can be identified and queued again. */
  onRetry: (job: ImportJob) => void
  onClear: () => void
}

/** The conversion queue: what is waiting, what is converting now, and how the recent ones ended. */
export function ImportQueue({ jobs, onCancel, onRetry, onClear }: ImportQueueProps) {
  if (jobs.length === 0) return null

  const open = jobs.filter((j) => j.status === 'Queued' || j.status === 'Running')
  const finished = jobs.length - open.length
  const waiting = jobs.filter((j) => j.status === 'Queued')

  return (
    <section className="health-item import-queue" aria-label="Conversion queue">
      <div className="import-queue-head">
        <strong>Conversion queue</strong>
        <small>{open.length === 0 ? 'Nothing waiting' : `${open.length} to go`}</small>
        {finished > 0 && (
          <button className="link-btn" onClick={onClear}>
            Clear finished
          </button>
        )}
      </div>
      <ul className="import-queue-list">
        {jobs.map((j) => (
          <li key={j.jobId} className={`import-queue-row import-queue-${j.status.toLowerCase()}`}>
            <span className="import-row-text">
              <strong>{j.name}</strong>
              <JobLine job={j} place={waiting.indexOf(j) + 1} />
            </span>
            {(j.status === 'Queued' || j.status === 'Running') && (
              <button onClick={() => onCancel(j)}>{j.status === 'Running' ? 'Stop' : 'Remove'}</button>
            )}
            {(j.status === 'Failed' || j.status === 'Cancelled') && <button onClick={() => onRetry(j)}>Try again</button>}
          </li>
        ))}
      </ul>
    </section>
  )
}

function JobLine({ job, place }: { job: ImportJob; place: number }) {
  switch (job.status) {
    case 'Queued':
      return <small>Waiting its turn (number {place} in line)</small>
    case 'Running':
      return (
        <small className="import-stage-line">
          <span className="import-spinner" aria-hidden="true" /> {job.stage}…
        </small>
      )
    case 'Succeeded':
      return (
        <small className="success">
          Imported. Filed at <code>{job.result?.finalPath}</code>
          {job.result?.note ? ` (${job.result.note})` : ''}
        </small>
      )
    case 'Cancelled':
      return <small>{job.error ?? 'Cancelled.'}</small>
    default:
      return <small className="error">Failed: {job.error ?? 'unknown error'}</small>
  }
}
