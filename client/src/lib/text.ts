export function formatRuntime(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60
  return hours > 0 ? `${hours}h ${mins}m` : `${mins}m`
}

export function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1)
}

// Strips HTML tags *and* decodes entities (e.g. "&apos;" -> "'") by letting the browser's own HTML
// parser do both at once -- a naive tag-stripping regex leaves entities raw. Paragraph/line breaks are
// inserted before parsing so the structure survives as plain text (textContent alone collapses them).
export function htmlToText(html: string): string {
  const withBreaks = html.replace(/<\/p>/gi, '\n\n').replace(/<br\s*\/?>/gi, '\n')
  const doc = new DOMParser().parseFromString(withBreaks, 'text/html')
  return (doc.body.textContent ?? '').trim()
}
