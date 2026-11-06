const base = { viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round', strokeLinejoin: 'round' } as const

export function PlayIcon({ size = 24 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor" aria-hidden="true">
      <path d="M8 5v14l11-7z" />
    </svg>
  )
}

export function PauseIcon({ size = 24 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor" aria-hidden="true">
      <rect x="6" y="5" width="4" height="14" rx="1" />
      <rect x="14" y="5" width="4" height="14" rx="1" />
    </svg>
  )
}

export function SkipBackIcon({ size = 28, seconds = 30 }: { size?: number; seconds?: number }) {
  return (
    <svg {...base} width={size} height={size} aria-hidden="true">
      <path d="M3 12a9 9 0 1 0 3-6.7" />
      <path d="M3 4v5h5" />
      <text x="12" y="16" textAnchor="middle" fontSize="8" fill="currentColor" stroke="none" fontWeight="700">{seconds}</text>
    </svg>
  )
}

export function SkipForwardIcon({ size = 28, seconds = 30 }: { size?: number; seconds?: number }) {
  return (
    <svg {...base} width={size} height={size} aria-hidden="true">
      <path d="M21 12a9 9 0 1 1-3-6.7" />
      <path d="M21 4v5h-5" />
      <text x="12" y="16" textAnchor="middle" fontSize="8" fill="currentColor" stroke="none" fontWeight="700">{seconds}</text>
    </svg>
  )
}

export function PrevChapterIcon({ size = 24 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor" aria-hidden="true">
      <rect x="5" y="5" width="2.5" height="14" rx="1" />
      <path d="M19 5v14L9 12z" />
    </svg>
  )
}

export function NextChapterIcon({ size = 24 }: { size?: number }) {
  return (
    <svg viewBox="0 0 24 24" width={size} height={size} fill="currentColor" aria-hidden="true">
      <rect x="16.5" y="5" width="2.5" height="14" rx="1" />
      <path d="M5 5v14l10-7z" />
    </svg>
  )
}

export function ChevronDownIcon({ size = 24 }: { size?: number }) {
  return (
    <svg {...base} width={size} height={size} aria-hidden="true">
      <path d="M6 9l6 6 6-6" />
    </svg>
  )
}

export function HeadphonesIcon({ size = 22 }: { size?: number }) {
  return (
    <svg {...base} width={size} height={size} aria-hidden="true">
      <path d="M4 15v-3a8 8 0 0 1 16 0v3" />
      <rect x="3" y="14" width="4" height="7" rx="1.5" fill="currentColor" />
      <rect x="17" y="14" width="4" height="7" rx="1.5" fill="currentColor" />
    </svg>
  )
}

export function DownloadIcon({ size = 20 }: { size?: number }) {
  return (
    <svg {...base} width={size} height={size} aria-hidden="true">
      <path d="M12 4v11" />
      <path d="M7 11l5 5 5-5" />
      <path d="M5 20h14" />
    </svg>
  )
}

export function CheckCircleIcon({ size = 20 }: { size?: number }) {
  return (
    <svg {...base} width={size} height={size} aria-hidden="true">
      <circle cx="12" cy="12" r="9" />
      <path d="M8 12.5l3 3 5-6" />
    </svg>
  )
}
