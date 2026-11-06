import { useEffect, useRef, useState, type CSSProperties, type ElementType } from 'react'
import { MIN_OVERFLOW_PX, marqueeSeconds } from '../lib/marquee'

interface MarqueeTextProps {
  text: string
  /** The element to render (a heading, <strong>...). */
  as?: ElementType
  className?: string
}

/**
 * A single line of text that, when it is too long for its space, slowly slides to show the rest and back again
 * (pausing at each end). Two books with the same cover and a title that differs only at the end -- "... (1 of 2)"
 * and "... (2 of 2)" -- can then be told apart without opening either. Text that fits stays still. For people who
 * ask their device for reduced motion it wraps onto more lines instead of moving.
 */
export function MarqueeText({ text, as, className = '' }: MarqueeTextProps) {
  const Tag = as ?? 'span'
  const outer = useRef<HTMLElement>(null)
  const inner = useRef<HTMLSpanElement>(null)
  const [overflow, setOverflow] = useState(0)

  useEffect(() => {
    const box = outer.current
    const line = inner.current
    if (!box || !line || typeof ResizeObserver === 'undefined') return
    // The observer reports once when it starts watching and again whenever the size changes (rotation, new text).
    const observer = new ResizeObserver(() => {
      const extra = line.scrollWidth - box.clientWidth
      setOverflow(extra > MIN_OVERFLOW_PX ? Math.ceil(extra) : 0)
    })
    observer.observe(box)
    observer.observe(line)
    return () => observer.disconnect()
  }, [text])

  const style = overflow > 0 ? ({ '--marquee-shift': `-${overflow}px`, '--marquee-seconds': `${marqueeSeconds(overflow)}s` } as CSSProperties) : undefined

  return (
    <Tag ref={outer} className={`marquee ${className}`.trim()} title={overflow > 0 ? text : undefined}>
      <span ref={inner} className="marquee-inner" data-scroll={overflow > 0 ? 'true' : 'false'} style={style}>
        {text}
      </span>
    </Tag>
  )
}
