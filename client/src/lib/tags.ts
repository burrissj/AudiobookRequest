/** True if the list already has this tag, ignoring capitalisation (the server treats "Sci-Fi" and "sci-fi" as one tag). */
export function hasTag(tags: string[], candidate: string): boolean {
  const key = candidate.trim().replace(/^#+/, '').trim().toLowerCase()
  return key !== '' && tags.some((t) => t.toLowerCase() === key)
}

/** The tags the person already uses elsewhere that this book doesn't have yet -- one tap to apply. */
export function suggestedTags(all: { tag: string; count: number }[], mine: string[], max = 6): string[] {
  return all
    .filter((t) => !hasTag(mine, t.tag))
    .slice(0, max)
    .map((t) => t.tag)
}
