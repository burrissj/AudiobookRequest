interface StarRatingProps {
  /** The current rating, 1-5, or null/0 for none. */
  value: number | null | undefined
  /** Called with 1-5, or 0 when the current rating is tapped again (which clears it). */
  onChange: (stars: number) => void
  size?: number
  disabled?: boolean
  label?: string
}

function Star({ filled, size }: { filled: boolean; size: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true" fill={filled ? 'currentColor' : 'none'} stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round">
      <path d="M12 3.2l2.6 5.5 6 .8-4.4 4.2 1.1 6-5.3-2.9-5.3 2.9 1.1-6L3.4 9.5l6-.8L12 3.2z" />
    </svg>
  )
}

/** Five tappable stars. Tap one to rate; tap the one you already chose to take the rating back. */
export function StarRating({ value, onChange, size = 30, disabled = false, label = 'Your rating' }: StarRatingProps) {
  const current = value ?? 0
  return (
    <div className="star-rating" role="radiogroup" aria-label={label}>
      {[1, 2, 3, 4, 5].map((n) => (
        <button
          key={n}
          type="button"
          role="radio"
          aria-checked={current === n}
          aria-label={`${n} star${n === 1 ? '' : 's'}`}
          className={`star${n <= current ? ' star-on' : ''}`}
          disabled={disabled}
          onClick={() => onChange(current === n ? 0 : n)}
        >
          <Star filled={n <= current} size={size} />
        </button>
      ))}
    </div>
  )
}
