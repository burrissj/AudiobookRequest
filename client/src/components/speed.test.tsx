// @vitest-environment jsdom
import { cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { SpeedPanel } from './SpeedPanel'
import { SpeedSlider } from './SpeedSlider'

afterEach(cleanup)

// The slider is 330px tall with a 30px handle, so the handle's centre travels 15px..315px (300px).
function sliderWithBox() {
  const onChange = vi.fn()
  const view = render(<SpeedSlider speed={2} onChange={onChange} />)
  const slider = screen.getByRole('slider')
  slider.getBoundingClientRect = () => ({ top: 100, left: 0, right: 64, bottom: 430, width: 64, height: 330, x: 0, y: 100, toJSON() {} })
  return { onChange, slider, ...view }
}

describe('SpeedSlider', () => {
  it('describes itself to assistive technology', () => {
    render(<SpeedSlider speed={1.15} onChange={() => {}} />)
    const slider = screen.getByRole('slider', { name: 'Fine-tune playback speed' })

    expect(slider.getAttribute('aria-valuemin')).toBe('0.25')
    expect(slider.getAttribute('aria-valuemax')).toBe('3')
    expect(slider.getAttribute('aria-valuenow')).toBe('1.15')
    expect(slider.getAttribute('aria-valuetext')).toBe('1.15×')
    expect(slider.getAttribute('aria-orientation')).toBe('vertical')
  })

  it('has a dot for each fine step along its track', () => {
    const { container } = render(<SpeedSlider speed={1} onChange={() => {}} />)
    expect(container.querySelectorAll('.speed-tick')).toHaveLength(56)
  })

  it('puts the handle where the speed is: slowest at the top, fastest at the bottom', () => {
    const { container, rerender } = render(<SpeedSlider speed={0.25} onChange={() => {}} />)
    const handle = () => container.querySelector('.speed-handle') as HTMLElement
    expect(handle().style.top).toMatch(/^calc\(0 \*/)

    rerender(<SpeedSlider speed={3} onChange={() => {}} />)
    expect(handle().style.top).toMatch(/^calc\(1 \*/)
  })

  it('sets the speed from where the track is touched', () => {
    const { onChange, slider } = sliderWithBox()

    fireEvent.pointerDown(slider, { clientY: 100 + 15, pointerId: 1 })
    expect(onChange).toHaveBeenLastCalledWith(0.25)

    fireEvent.pointerDown(slider, { clientY: 100 + 315, pointerId: 1 })
    expect(onChange).toHaveBeenLastCalledWith(3)

    // 1.0x sits 0.75 of 2.75 of the way down the 300px the handle travels
    fireEvent.pointerDown(slider, { clientY: 100 + 15 + (0.75 / 2.75) * 300, pointerId: 1 })
    expect(onChange).toHaveBeenLastCalledWith(1)
  })

  it('stays within range when dragged beyond the ends', () => {
    const { onChange, slider } = sliderWithBox()

    fireEvent.pointerDown(slider, { clientY: -500, pointerId: 1 })
    expect(onChange).toHaveBeenLastCalledWith(0.25)
    fireEvent.pointerMove(slider, { clientY: 5000, pointerId: 1 })
    expect(onChange).toHaveBeenLastCalledWith(3)
  })

  it('follows a drag, and stops following once the finger lifts', () => {
    const { onChange, slider } = sliderWithBox()

    fireEvent.pointerDown(slider, { clientY: 100 + 15, pointerId: 1 })
    fireEvent.pointerMove(slider, { clientY: 100 + 165, pointerId: 1 })
    const callsWhileDragging = onChange.mock.calls.length
    expect(callsWhileDragging).toBeGreaterThan(1)

    fireEvent.pointerUp(slider, { pointerId: 1 })
    fireEvent.pointerMove(slider, { clientY: 100 + 300, pointerId: 1 })
    expect(onChange.mock.calls.length).toBe(callsWhileDragging)
  })

  it('ignores a move with no finger down', () => {
    const { onChange, slider } = sliderWithBox()
    fireEvent.pointerMove(slider, { clientY: 200, pointerId: 1 })
    expect(onChange).not.toHaveBeenCalled()
  })

  it('does not report a change when the touch lands on the speed it already has', () => {
    const onChange = vi.fn()
    render(<SpeedSlider speed={0.25} onChange={onChange} />)
    const slider = screen.getByRole('slider')
    slider.getBoundingClientRect = () => ({ top: 0, left: 0, right: 64, bottom: 330, width: 64, height: 330, x: 0, y: 0, toJSON() {} })

    fireEvent.pointerDown(slider, { clientY: 0, pointerId: 1 })

    expect(onChange).not.toHaveBeenCalled()
  })

  it('moves a fine step with the arrow keys, following how it looks (down is faster)', () => {
    const onChange = vi.fn()
    render(<SpeedSlider speed={1} onChange={onChange} />)
    const slider = screen.getByRole('slider')

    fireEvent.keyDown(slider, { key: 'ArrowDown' })
    expect(onChange).toHaveBeenLastCalledWith(1.05)
    fireEvent.keyDown(slider, { key: 'ArrowRight' })
    expect(onChange).toHaveBeenLastCalledWith(1.05)
    fireEvent.keyDown(slider, { key: 'ArrowUp' })
    expect(onChange).toHaveBeenLastCalledWith(0.95)
    fireEvent.keyDown(slider, { key: 'ArrowLeft' })
    expect(onChange).toHaveBeenLastCalledWith(0.95)
  })

  it('moves a quarter with Page Up/Down and jumps to the ends with Home/End', () => {
    const onChange = vi.fn()
    render(<SpeedSlider speed={1} onChange={onChange} />)
    const slider = screen.getByRole('slider')

    fireEvent.keyDown(slider, { key: 'PageDown' })
    expect(onChange).toHaveBeenLastCalledWith(1.25)
    fireEvent.keyDown(slider, { key: 'PageUp' })
    expect(onChange).toHaveBeenLastCalledWith(0.75)
    fireEvent.keyDown(slider, { key: 'Home' })
    expect(onChange).toHaveBeenLastCalledWith(0.25)
    fireEvent.keyDown(slider, { key: 'End' })
    expect(onChange).toHaveBeenLastCalledWith(3)
  })

  it('does not go past the ends from the keyboard, and ignores other keys', () => {
    const onChange = vi.fn()
    render(<SpeedSlider speed={3} onChange={onChange} />)
    const slider = screen.getByRole('slider')

    fireEvent.keyDown(slider, { key: 'ArrowDown' })
    fireEvent.keyDown(slider, { key: 'a' })

    expect(onChange).not.toHaveBeenCalled()
  })
})

describe('SpeedPanel', () => {
  it('shows the current speed to two decimals', () => {
    render(<SpeedPanel speed={1.15} onChange={() => {}} onClose={() => {}} />)
    expect(document.querySelector('.speed-current strong')?.textContent).toBe('1.15×')
  })

  it('lists the presets, with normal speed named', () => {
    render(<SpeedPanel speed={1} onChange={() => {}} onClose={() => {}} />)

    expect(screen.getByRole('button', { name: '1.00× (normal speed)' })).toBeTruthy()
    for (const label of ['0.50×', '0.75×', '1.25×', '1.50×', '1.75×', '2.00×', '2.50×', '3.00×']) {
      expect(screen.getByRole('button', { name: label })).toBeTruthy()
    }
  })

  it('marks only the preset that matches the current speed', () => {
    render(<SpeedPanel speed={1.5} onChange={() => {}} onClose={() => {}} />)

    expect(screen.getByRole('button', { name: '1.50×' }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByRole('button', { name: '1.25×' }).getAttribute('aria-pressed')).toBe('false')
  })

  it('marks no preset for a fine-tuned speed in between', () => {
    render(<SpeedPanel speed={1.35} onChange={() => {}} onClose={() => {}} />)

    const pressed = screen.getAllByRole('button').filter((b) => b.getAttribute('aria-pressed') === 'true')
    expect(pressed).toHaveLength(0)
  })

  it('applies a preset when tapped', () => {
    const onChange = vi.fn()
    render(<SpeedPanel speed={1} onChange={onChange} onClose={() => {}} />)

    fireEvent.click(screen.getByRole('button', { name: '1.75×' }))

    expect(onChange).toHaveBeenCalledWith(1.75)
  })

  it('shares the speed with the fine-tune slider', () => {
    const onChange = vi.fn()
    render(<SpeedPanel speed={1} onChange={onChange} onClose={() => {}} />)

    fireEvent.keyDown(screen.getByRole('slider'), { key: 'ArrowDown' })

    expect(onChange).toHaveBeenCalledWith(1.05)
  })

  it('closes from the arrow, from a tap outside, and from Escape — but not from a tap inside', () => {
    const onClose = vi.fn()
    const { container } = render(<SpeedPanel speed={1} onChange={() => {}} onClose={onClose} />)

    fireEvent.click(screen.getByRole('dialog'))
    expect(onClose).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Close speed picker' }))
    expect(onClose).toHaveBeenCalledTimes(1)

    fireEvent.click(container.querySelector('.speed-backdrop')!)
    expect(onClose).toHaveBeenCalledTimes(2)

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(onClose).toHaveBeenCalledTimes(3)
  })

  it('stops listening for Escape once closed', () => {
    const onClose = vi.fn()
    const { unmount } = render(<SpeedPanel speed={1} onChange={() => {}} onClose={onClose} />)
    unmount()

    fireEvent.keyDown(window, { key: 'Escape' })

    expect(onClose).not.toHaveBeenCalled()
  })
})
