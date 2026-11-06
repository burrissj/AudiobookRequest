interface ListenCoverProps {
  id: string
  hasCover: boolean
  title: string
  /** Pixel width to ask Jellyfin for (it resizes server-side so phones don't download full-size art). */
  width?: number
  className?: string
}

/** A book's cover, streamed through the app so no Jellyfin login or address is needed in the browser. */
export function ListenCover({ id, hasCover, title, width = 300, className = '' }: ListenCoverProps) {
  if (!hasCover) {
    return (
      <div className={`listen-cover listen-cover-empty ${className}`} aria-hidden="true">
        {title.slice(0, 1).toUpperCase()}
      </div>
    )
  }
  return <img className={`listen-cover ${className}`} src={`/api/listen/items/${id}/image?w=${width}`} alt="" loading="lazy" />
}
