import { useEffect } from 'react'
import { clampSpeed, formatSpeedExact, SPEED_PRESETS } from '../player/playerMath'
import { SpeedSlider } from './SpeedSlider'

function GaugeIcon() {
  return (
    <svg viewBox="0 0 24 24" width="30" height="30" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" aria-hidden="true">
      <path d="M4.5 17a8.5 8.5 0 1 1 15 0" strokeDasharray="1.2 3.2" />
      <path d="M12 15.5l4-4.5" />
      <circle cx="12" cy="15.5" r="1.3" fill="currentColor" stroke="none" />
    </svg>
  )
}

interface SpeedPanelProps {
  speed: number
  onChange: (speed: number) => void
  onClose: () => void
}

/**
 * The playback-speed picker: the current speed, one-tap presets, and a fine-tune slider for anything in between.
 * Closes on the arrow, a tap outside, or Escape.
 */
export function SpeedPanel({ speed, onChange, onClose }: SpeedPanelProps) {
  const current = clampSpeed(speed)

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  return (
    <div className="speed-backdrop" onClick={onClose}>
      <div className="speed-panel" role="dialog" aria-modal="true" aria-label="Playback speed" onClick={(e) => e.stopPropagation()}>
        <h2 className="speed-title">Playback Speed</h2>

        <div className="speed-columns">
          <div className="speed-left">
            <p className="speed-label">Current speed</p>
            <p className="speed-current" aria-live="polite">
              <GaugeIcon />
              <strong>{formatSpeedExact(current)}</strong>
            </p>

            <p className="speed-label speed-label-presets">Choose a preset</p>
            <ul className="speed-presets">
              {SPEED_PRESETS.map((preset) => (
                <li key={preset}>
                  <button className={current === preset ? 'active' : ''} aria-pressed={current === preset} onClick={() => onChange(preset)}>
                    {formatSpeedExact(preset)}
                    {preset === 1 ? ' (normal speed)' : ''}
                  </button>
                </li>
              ))}
            </ul>
          </div>

          <div className="speed-right">
            <p className="speed-label">Fine-tune</p>
            <SpeedSlider speed={current} onChange={onChange} />
          </div>
        </div>

        <button className="speed-close" onClick={onClose} aria-label="Close speed picker">
          <svg viewBox="0 0 24 24" width="28" height="28" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <path d="M6 9l6 6 6-6" />
          </svg>
        </button>
      </div>
    </div>
  )
}
