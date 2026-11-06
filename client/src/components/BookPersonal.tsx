import { useState, type FormEvent } from 'react'
import type { usePersonalBook } from '../hooks/usePersonalBook'
import { hasTag, suggestedTags } from '../lib/tags'
import { formatClock } from '../player/playerMath'
import { usePlayer } from '../state/usePlayer'

/**
 * Tags and bookmarks for a book: yours alone, kept on the server, working offline for bookmarks. In the player they
 * can mark and jump to places; on a book's detail sheet (`browsing`) the book isn't the one playing, so they only
 * show where the bookmarks are and let the notes be edited.
 */
export function BookPersonal({
  state,
  browsing = false,
  only,
}: {
  state: ReturnType<typeof usePersonalBook>
  browsing?: boolean
  /** Show just one of the two sections (the book's page puts the tags above the description and the bookmarks below it). */
  only?: 'tags' | 'bookmarks'
}) {
  const player = usePlayer()
  const { personal, loaded, error, allTags, addTag, removeTag, addBookmark, editBookmarkNote, removeBookmark } = state
  const [tagText, setTagText] = useState('')
  const [editing, setEditing] = useState<string | null>(null)
  const [noteText, setNoteText] = useState('')

  const suggestions = suggestedTags(allTags, personal.tags)
  const currentChapter = player.current && player.chapterIndex >= 0 ? player.current.chapters[player.chapterIndex]?.name : null

  function submitTag(e: FormEvent) {
    e.preventDefault()
    const text = tagText
    if (text.trim() === '' || hasTag(personal.tags, text)) {
      setTagText('')
      return
    }
    setTagText('')
    void addTag(text)
  }

  function startEdit(key: string, note: string | null) {
    setEditing(key)
    setNoteText(note ?? '')
  }

  function submitNote(e: FormEvent, bookmark: (typeof personal.bookmarks)[number]) {
    e.preventDefault()
    void editBookmarkNote(bookmark, noteText)
    setEditing(null)
  }

  return (
    <div className="personal">
      {error && <p className="error personal-error">{error}</p>}

      {only !== 'tags' && (
      <section className="personal-section" aria-label="Bookmarks">
        <div className="personal-head">
          <h3>Bookmarks{personal.bookmarks.length > 0 ? ` (${personal.bookmarks.length})` : ''}</h3>
          {!browsing && (
            <button className="personal-add" onClick={() => void addBookmark(player.position, currentChapter ?? null)} disabled={!loaded && navigator.onLine}>
              + Bookmark at {formatClock(player.position)}
            </button>
          )}
        </div>
        {personal.bookmarks.length === 0 && (
          <p className="subtitle personal-empty">{browsing ? 'No bookmarks yet. Add them while you listen.' : 'Mark a place to come back to.'}</p>
        )}
        <ul className="bookmark-list">
          {personal.bookmarks.map((b) => {
            const key = b.pendingClientId ?? String(b.id)
            return (
              <li key={key} className="bookmark-row">
                {browsing ? (
                  <div className="bookmark-go bookmark-static">
                    <strong>{formatClock(b.positionSeconds)}</strong>
                    <span>
                      {b.label ?? 'Bookmark'}
                      {b.pendingClientId ? ' · will sync when online' : ''}
                    </span>
                  </div>
                ) : (
                  <button className="bookmark-go" onClick={() => player.seek(b.positionSeconds)} aria-label={`Go to ${formatClock(b.positionSeconds)}`}>
                    <strong>{formatClock(b.positionSeconds)}</strong>
                    <span>
                      {b.label ?? 'Bookmark'}
                      {b.pendingClientId ? ' · will sync when online' : ''}
                    </span>
                  </button>
                )}
                {editing === key ? (
                  <form className="bookmark-note-form" onSubmit={(e) => submitNote(e, b)}>
                    <input
                      value={noteText}
                      onChange={(e) => setNoteText(e.target.value)}
                      maxLength={200}
                      placeholder="Note"
                      aria-label="Bookmark note"
                      autoFocus
                      enterKeyHint="done"
                    />
                    <button type="submit">Save</button>
                    <button type="button" onClick={() => setEditing(null)}>
                      Cancel
                    </button>
                  </form>
                ) : (
                  <>
                    {b.note && <p className="bookmark-note">{b.note}</p>}
                    <div className="bookmark-actions">
                      <button className="link-btn" onClick={() => startEdit(key, b.note)}>
                        {b.note ? 'Edit note' : 'Add note'}
                      </button>
                      <button className="link-btn" onClick={() => void removeBookmark(b)}>
                        Delete
                      </button>
                    </div>
                  </>
                )}
              </li>
            )
          })}
        </ul>
      </section>
      )}

      {only !== 'bookmarks' && (
      <section className="personal-section" aria-label="Tags">
        <div className="personal-head">
          <h3>Tags</h3>
        </div>
        {personal.tags.length > 0 && (
          <ul className="tag-list">
            {personal.tags.map((t) => (
              <li key={t} className="tag-chip">
                <span>{t}</span>
                <button onClick={() => void removeTag(t)} aria-label={`Remove tag ${t}`}>
                  ×
                </button>
              </li>
            ))}
          </ul>
        )}
        <form className="tag-form" onSubmit={submitTag}>
          <input
            value={tagText}
            onChange={(e) => setTagText(e.target.value)}
            placeholder="Add a tag (Favorites, Re-listen…)"
            aria-label="Add a tag"
            maxLength={30}
            autoCapitalize="none"
            autoCorrect="off"
            enterKeyHint="done"
          />
          <button type="submit" disabled={tagText.trim() === '' || !loaded}>
            Add
          </button>
        </form>
        {suggestions.length > 0 && (
          <div className="tag-suggestions" aria-label="Your other tags">
            {suggestions.map((t) => (
              <button key={t} className="tag-suggestion" onClick={() => void addTag(t)}>
                + {t}
              </button>
            ))}
          </div>
        )}
      </section>
      )}
    </div>
  )
}
