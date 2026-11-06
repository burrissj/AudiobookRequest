// @vitest-environment jsdom
import { renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useKeyboardOpen } from './useKeyboardOpen'

class FakeViewport {
  height = 800
  scale = 1
  private listeners = new Set<() => void>()
  addEventListener(_type: string, cb: () => void) {
    this.listeners.add(cb)
  }
  removeEventListener(_type: string, cb: () => void) {
    this.listeners.delete(cb)
  }
  resizeTo(height: number) {
    this.height = height
    this.listeners.forEach((cb) => cb())
  }
}

describe('useKeyboardOpen', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
    document.documentElement.className = ''
  })

  function setup(innerHeight = 800) {
    const vv = new FakeViewport()
    vi.stubGlobal('innerHeight', innerHeight)
    Object.defineProperty(window, 'visualViewport', { configurable: true, value: vv })
    return vv
  }

  it('marks the page while the visible area is squeezed by a keyboard, and clears it after', () => {
    const vv = setup()
    renderHook(() => useKeyboardOpen())
    expect(document.documentElement.classList.contains('kb-open')).toBe(false)

    vv.resizeTo(450)
    expect(document.documentElement.classList.contains('kb-open')).toBe(true)

    vv.resizeTo(800)
    expect(document.documentElement.classList.contains('kb-open')).toBe(false)
  })

  it('is NOT triggered by a text box merely having focus (the bug that hid the tab bar on desktop)', () => {
    setup()
    renderHook(() => useKeyboardOpen())
    const input = document.createElement('input')
    document.body.append(input)

    input.focus()
    input.dispatchEvent(new FocusEvent('focusin', { bubbles: true }))

    expect(document.activeElement).toBe(input)
    expect(document.documentElement.classList.contains('kb-open')).toBe(false)
    input.remove()
  })

  it('cleans up after itself', () => {
    const vv = setup()
    const { unmount } = renderHook(() => useKeyboardOpen())
    vv.resizeTo(400)
    expect(document.documentElement.classList.contains('kb-open')).toBe(true)

    unmount()

    expect(document.documentElement.classList.contains('kb-open')).toBe(false)
  })

  it('does nothing where the browser has no visual viewport', () => {
    Object.defineProperty(window, 'visualViewport', { configurable: true, value: undefined })
    expect(() => renderHook(() => useKeyboardOpen())).not.toThrow()
  })
})
