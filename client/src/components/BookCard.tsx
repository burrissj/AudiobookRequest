import type { ReactNode } from 'react'

interface BookCardProps {
  coverUrl: string | null
  title: string
  authorName: string
  seriesTitle?: string | null
  caption?: string
  onClick?: () => void
  children?: ReactNode
}

/** Shared row layout for a book: cover, title/author/series, optional caption, and a right-side action/status slot. */
export function BookCard({ coverUrl, title, authorName, seriesTitle, caption, onClick, children }: BookCardProps) {
  return (
    <li className={`book-row${onClick ? ' book-row-clickable' : ''}`} onClick={onClick}>
      {coverUrl && <img src={coverUrl} alt="" className="book-cover" />}
      <div className="book-info">
        <strong>{title}</strong>
        <span>{authorName}</span>
        {seriesTitle && <span className="subtitle">{seriesTitle}</span>}
        {caption && <span className="book-caption">{caption}</span>}
      </div>
      {children}
    </li>
  )
}
