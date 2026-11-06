import { describe, expect, it } from 'vitest'
import { keyboardCoversScreen } from './useKeyboardOpen'

describe('keyboardCoversScreen', () => {
  it('is false when the visible area is the whole page (a text field having focus is not enough)', () => {
    expect(keyboardCoversScreen(800, 800, 1)).toBe(false)
  })

  it('is true when something like a keyboard takes a large part of the screen', () => {
    expect(keyboardCoversScreen(800, 480, 1)).toBe(true)
  })

  it('ignores a browser toolbar sliding in or out', () => {
    expect(keyboardCoversScreen(800, 740, 1)).toBe(false)
  })

  it('ignores pinch-zoom, which also shrinks the visible area', () => {
    expect(keyboardCoversScreen(800, 400, 2)).toBe(false)
  })
})
