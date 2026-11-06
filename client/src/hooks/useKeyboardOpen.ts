import { useEffect } from 'react'

/** How much shorter than the page the visible area must be before we call it a keyboard (not a toolbar or zoom). */
export const KEYBOARD_MIN_PX = 140

/** True when the visible area is much shorter than the page's own height: an on-screen keyboard is covering it. */
export function keyboardCoversScreen(innerHeight: number, visualHeight: number, visualScale: number): boolean {
  // Pinch-zoom also shrinks the visual viewport, so measure in unscaled pixels.
  return innerHeight - visualHeight * visualScale > KEYBOARD_MIN_PX
}

/**
 * Marks <html> with "kb-open" while the on-screen keyboard is up. On iPhones a fixed bottom bar doesn't ride up
 * with the keyboard -- it floats over the field or jumps around -- so the CSS hides the tab bar and mini player
 * while typing.
 *
 * Detected only by the visible area shrinking. It used to also count "a text field has focus", but that is true on
 * a desktop with no keyboard at all, and whenever a page focuses its own field (the Browse search box does), which
 * left the tab bar hidden after switching tabs.
 */
export function useKeyboardOpen() {
  useEffect(() => {
    const vv = window.visualViewport
    if (!vv) return
    const root = document.documentElement

    const update = () => root.classList.toggle('kb-open', keyboardCoversScreen(window.innerHeight, vv.height, vv.scale))
    update()

    vv.addEventListener('resize', update)
    return () => {
      vv.removeEventListener('resize', update)
      root.classList.remove('kb-open')
    }
  }, [])
}
