export function formatHours(minutes: number): string {
  const hours = Math.round(minutes / 60)
  if (hours < 1) return '<1 HOUR'
  return hours === 1 ? '1 HOUR' : `${hours} HOURS`
}

/** "1.4 GB" / "230 MB". */
export function formatBytes(bytes: number): string {
  if (bytes >= 1024 ** 3) return `${(bytes / 1024 ** 3).toFixed(1)} GB`
  return `${Math.max(1, Math.round(bytes / 1024 ** 2))} MB`
}
