/** percent is a 0-1 fraction. */
export function ProgressBar({ percent }: { percent: number }) {
  const clamped = Math.max(0, Math.min(1, percent))
  return (
    <div
      className="progress-track"
      role="progressbar"
      aria-valuenow={Math.round(clamped * 100)}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <div className="progress-fill" style={{ width: `${clamped * 100}%` }} />
    </div>
  )
}
