import { useRef, type KeyboardEvent, type PointerEvent } from 'react'
import {
  clampSpeed,
  FINE_SPEED_STEP,
  formatSpeedExact,
  fractionFromSpeed,
  MAX_SPEED,
  MIN_SPEED,
  SPEED_STEP,
  speedFromFraction,
} from '../player/playerMath'

// The handle's height, in px. The handle's centre travels the track minus half a handle at each end, so the pointer
// is measured over that same stretch and the handle sits exactly under the finger.
const HANDLE_PX = 30

// One dot per fine step, so the track reads as a ruler: 0.25x to 3x in 0.05 steps.
const TICKS = Math.round((MAX_SPEED - MIN_SPEED) / FINE_SPEED_STEP) + 1

interface SpeedSliderProps {
  speed: number
  onChange: (speed: number) => void
}

/**
 * A tall slider for tuning the speed in 0.05 steps, slowest at the top and fastest at the bottom (like the
 * speed picker in Libby). Drag the handle or tap the track; with a keyboard, the arrow keys move one fine step,
 * Page Up/Down a quarter, Home/End the ends.
 */
export function SpeedSlider({ speed, onChange }: SpeedSliderProps) {
  const track = useRef<HTMLDivElement>(null)
  const dragging = useRef(false)
  const current = clampSpeed(speed)

  function moveTo(clientY: number) {
    const box = track.current?.getBoundingClientRect()
    if (!box) return
    const usable = box.height - HANDLE_PX
    if (usable <= 0) return
    const next = speedFromFraction((clientY - box.top - HANDLE_PX / 2) / usable)
    if (next !== current) onChange(next)
  }

  function onPointerDown(e: PointerEvent<HTMLDivElement>) {
    dragging.current = true
    e.currentTarget.setPointerCapture?.(e.pointerId)
    moveTo(e.clientY)
  }

  function onPointerMove(e: PointerEvent<HTMLDivElement>) {
    if (dragging.current) moveTo(e.clientY)
  }

  function stopDragging() {
    dragging.current = false
  }

  function onKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    const moves: Record<string, number> = {
      ArrowDown: FINE_SPEED_STEP,
      ArrowRight: FINE_SPEED_STEP,
      ArrowUp: -FINE_SPEED_STEP,
      ArrowLeft: -FINE_SPEED_STEP,
      PageDown: SPEED_STEP,
      PageUp: -SPEED_STEP,
    }
    let next: number | null = null
    if (e.key in moves) next = clampSpeed(current + moves[e.key])
    else if (e.key === 'Home') next = MIN_SPEED
    else if (e.key === 'End') next = MAX_SPEED
    if (next === null) return
    e.preventDefault()
    if (next !== current) onChange(next)
  }

  return (
    <div
      ref={track}
      className="speed-slider"
      role="slider"
      tabIndex={0}
      aria-orientation="vertical"
      aria-label="Fine-tune playback speed"
      aria-valuemin={MIN_SPEED}
      aria-valuemax={MAX_SPEED}
      aria-valuenow={current}
      aria-valuetext={formatSpeedExact(current)}
      onPointerDown={onPointerDown}
      onPointerMove={onPointerMove}
      onPointerUp={stopDragging}
      onPointerCancel={stopDragging}
      onKeyDown={onKeyDown}
    >
      <div className="speed-ticks" aria-hidden="true" style={{ top: HANDLE_PX / 2, bottom: HANDLE_PX / 2 }}>
        {Array.from({ length: TICKS }, (_, i) => (
          <span key={i} className="speed-tick" />
        ))}
      </div>
      <div className="speed-handle" aria-hidden="true" style={{ height: HANDLE_PX, top: `calc((100% - ${HANDLE_PX}px) * ${fractionFromSpeed(current)})` }}>
        <span />
        <span />
      </div>
    </div>
  )
}
