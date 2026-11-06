import { useCallback, useEffect, useState } from 'react'
import { api, ApiError } from '../api/client'
import type { Bookmark, PersonalBook, TagCount } from '../api/types'
import {
  addPendingBookmark,
  announcePersonalChange,
  mergeBookmarks,
  newClientId,
  pendingBookmarksFor,
  PERSONAL_CHANGED,
  removePendingBookmark,
} from '../player/bookmarks'

const EMPTY: PersonalBook = { rating: null, tags: [], bookmarks: [] }

/** Words for what went wrong, for the small message under the controls. */
function reason(err: unknown, fallback: string): string {
  if (err instanceof ApiError) return err.message || fallback
  return navigator.onLine === false ? 'You are offline. Try again when you are back online.' : fallback
}

/**
 * The signed-in person's own rating, tags and bookmarks for one book, and the ways to change them. Changes show
 * at once and are undone if the server refuses. Bookmarks also work with no signal: they are kept on the device
 * and sent when the connection returns.
 */
export function usePersonalBook(itemId: string | null) {
  // Stored with the book they belong to, so moving to another book starts clean without clearing anything by hand.
  const [loaded, setLoaded] = useState<{ itemId: string; data: PersonalBook } | null>(null)
  const [message, setMessage] = useState<{ itemId: string; text: string } | null>(null)
  const [allTags, setAllTags] = useState<TagCount[]>([])
  const [reload, setReload] = useState(0)

  const data = loaded !== null && loaded.itemId === itemId ? loaded.data : null
  const error = message !== null && message.itemId === itemId ? message.text : null
  const view: PersonalBook = data ? { ...data, bookmarks: mergeBookmarks(data.bookmarks, itemId ? pendingBookmarksFor(itemId) : []) } : EMPTY

  // Other screens (or the offline queue delivering) can change things: refresh when told.
  useEffect(() => {
    const onChange = () => setReload((n) => n + 1)
    window.addEventListener(PERSONAL_CHANGED, onChange)
    return () => window.removeEventListener(PERSONAL_CHANGED, onChange)
  }, [])

  useEffect(() => {
    if (!itemId) return
    let current = true
    api
      .get<PersonalBook>(`/api/listen/items/${itemId}/mine`)
      .then((d) => current && setLoaded({ itemId, data: d }))
      .catch(() => current && setLoaded((prev) => (prev?.itemId === itemId ? prev : { itemId, data: EMPTY })))
    api
      .get<TagCount[]>('/api/listen/tags')
      .then((t) => current && setAllTags(t))
      .catch(() => {})
    return () => {
      current = false
    }
  }, [itemId, reload])

  const change = useCallback(
    (update: (d: PersonalBook) => PersonalBook) => {
      if (!itemId) return
      setLoaded((prev) => ({ itemId, data: update(prev?.itemId === itemId ? prev.data : EMPTY) }))
    },
    [itemId],
  )

  const fail = useCallback(
    (err: unknown, fallback: string) => {
      if (itemId) setMessage({ itemId, text: reason(err, fallback) })
      setReload((n) => n + 1) // put the screen back to what the server really has
    },
    [itemId],
  )

  const setRating = useCallback(
    async (stars: number) => {
      if (!itemId) return
      setMessage(null)
      change((d) => ({ ...d, rating: stars === 0 ? null : stars }))
      try {
        await api.put(`/api/listen/items/${itemId}/rating`, { stars })
        announcePersonalChange()
      } catch (err) {
        fail(err, "Couldn't save your rating.")
      }
    },
    [itemId, change, fail],
  )

  const addTag = useCallback(
    async (raw: string) => {
      if (!itemId || raw.trim() === '') return
      setMessage(null)
      try {
        const result = await api.post<{ tag: string }>(`/api/listen/items/${itemId}/tags`, { tag: raw })
        change((d) => (d.tags.some((t) => t.toLowerCase() === result.tag.toLowerCase()) ? d : { ...d, tags: [...d.tags, result.tag] }))
        announcePersonalChange()
      } catch (err) {
        fail(err, "Couldn't add that tag.")
      }
    },
    [itemId, change, fail],
  )

  const removeTag = useCallback(
    async (tag: string) => {
      if (!itemId) return
      setMessage(null)
      change((d) => ({ ...d, tags: d.tags.filter((t) => t !== tag) }))
      try {
        await api.delete(`/api/listen/items/${itemId}/tags?tag=${encodeURIComponent(tag)}`)
        announcePersonalChange()
      } catch (err) {
        fail(err, "Couldn't remove that tag.")
      }
    },
    [itemId, change, fail],
  )

  const addBookmark = useCallback(
    async (positionSeconds: number, label: string | null, note: string | null = null) => {
      if (!itemId) return
      setMessage(null)
      const clientId = newClientId()
      try {
        const saved = await api.post<Bookmark>(`/api/listen/items/${itemId}/bookmarks`, { positionSeconds, note, label, clientId })
        change((d) => ({ ...d, bookmarks: [...d.bookmarks, saved] }))
        announcePersonalChange()
      } catch (err) {
        if (err instanceof ApiError) {
          fail(err, "Couldn't save that bookmark.")
          return
        }
        // No signal: keep it on this device and send it later.
        addPendingBookmark({ clientId, itemId, positionSeconds, note, label, createdAt: new Date().toISOString() })
        setReload((n) => n + 1)
      }
    },
    [itemId, change, fail],
  )

  const editBookmarkNote = useCallback(
    async (bookmark: Bookmark, note: string) => {
      if (!itemId) return
      setMessage(null)
      const text = note.trim() === '' ? null : note.trim()
      if (bookmark.pendingClientId) {
        // Not delivered yet: change the waiting copy.
        const waiting = pendingBookmarksFor(itemId).find((p) => p.clientId === bookmark.pendingClientId)
        if (waiting) addPendingBookmark({ ...waiting, note: text })
        setReload((n) => n + 1)
        return
      }
      change((d) => ({ ...d, bookmarks: d.bookmarks.map((b) => (b.id === bookmark.id ? { ...b, note: text } : b)) }))
      try {
        await api.put(`/api/listen/bookmarks/${bookmark.id}`, { note: text })
      } catch (err) {
        fail(err, "Couldn't save that note.")
      }
    },
    [itemId, change, fail],
  )

  const removeBookmark = useCallback(
    async (bookmark: Bookmark) => {
      if (!itemId) return
      setMessage(null)
      if (bookmark.pendingClientId) {
        removePendingBookmark(bookmark.pendingClientId)
        setReload((n) => n + 1)
        return
      }
      change((d) => ({ ...d, bookmarks: d.bookmarks.filter((b) => b.id !== bookmark.id) }))
      try {
        await api.delete(`/api/listen/bookmarks/${bookmark.id}`)
        announcePersonalChange()
      } catch (err) {
        fail(err, "Couldn't delete that bookmark.")
      }
    },
    [itemId, change, fail],
  )

  return { personal: view, loaded: data !== null, error, allTags, setRating, addTag, removeTag, addBookmark, editBookmarkNote, removeBookmark }
}
