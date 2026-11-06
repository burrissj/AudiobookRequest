/** Compares "0.10.0"-style versions numerically (so 0.10.0 is newer than 0.9.0). */
export function compareVersions(a: string, b: string): number {
  const pa = a.split('.').map((n) => parseInt(n, 10) || 0)
  const pb = b.split('.').map((n) => parseInt(n, 10) || 0)
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const d = (pa[i] ?? 0) - (pb[i] ?? 0)
    if (d !== 0) return d < 0 ? -1 : 1
  }
  return 0
}

interface ChangelogLike {
  version: string
}

/**
 * Which release notes to show: everything newer than the version the person last saw, newest first, at most
 * `max`. Someone who has never seen the notice gets just the latest release rather than the whole history.
 */
export function entriesToShow<T extends ChangelogLike>(changelog: T[], seen: string | null, current: string, max = 3): T[] {
  if (seen !== null && compareVersions(seen, current) >= 0) return []
  const newer = seen === null ? changelog.slice(0, 1) : changelog.filter((e) => compareVersions(e.version, seen) > 0)
  return newer.slice(0, max)
}
