/** How long one leg of the scroll takes: slow enough to read, never quicker than 5 seconds, never longer than 40. */
export function marqueeSeconds(overflowPx: number): number {
  return Math.min(40, Math.max(5, overflowPx / 28 + 3.5))
}

// Sub-pixel rounding makes text look 1px too wide; don't animate for that.
export const MIN_OVERFLOW_PX = 3
