import { useEffect, useId, useState, type FormEvent } from 'react'
import { api, ApiError } from '../api/client'
import type { BookMetadataEdit, BookOverride, ListenGroups, ListenItem } from '../api/types'

interface BookDetailsEditorProps {
  item: ListenItem
  /** Called once a correction has been saved or removed, so the lists and the book's page can show it. */
  onSaved: () => void
  onClose: () => void
}

/** Names already in the library, to suggest while typing, so "The Mistborn Saga" isn't retyped as "Mistborn". */
function useNames(by: 'author' | 'series'): string[] {
  const [names, setNames] = useState<string[]>([])
  useEffect(() => {
    let current = true
    api
      .get<ListenGroups>(`/api/listen/groups?by=${by}`)
      .then((d) => current && setNames(d.groups.filter((g) => g.key !== '-').map((g) => g.name)))
      .catch(() => {})
    return () => {
      current = false
    }
  }, [by])
  return names
}

/**
 * The admin's form for fixing what the Library knows about a book: who wrote it and which series it is in. Anything left
 * empty is left to Jellyfin, Audible and the title; "Reset" puts it all back to automatic.
 */
export function BookDetailsEditor({ item, onSaved, onClose }: BookDetailsEditorProps) {
  const id = useId()
  const authors = useNames('author')
  const seriesNames = useNames('series')
  const [loaded, setLoaded] = useState<BookMetadataEdit | null>(null)
  const [author, setAuthor] = useState('')
  const [series, setSeries] = useState('')
  const [number, setNumber] = useState('')
  const [notInSeries, setNotInSeries] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let current = true
    api
      .get<BookMetadataEdit>(`/api/listen/items/${item.id}/metadata`)
      .then((d) => {
        if (!current) return
        setLoaded(d)
        setAuthor(d.override?.author ?? '')
        setSeries(d.override?.series ?? '')
        setNumber(d.override?.seriesNumber != null ? String(d.override.seriesNumber) : '')
        setNotInSeries(d.override?.notInSeries ?? false)
      })
      .catch((err) => current && setError(err instanceof ApiError ? err.message : "Couldn't load this book's details."))
    return () => {
      current = false
    }
  }, [item.id])

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setError(null)
    const body: BookOverride = {
      author: author.trim() || null,
      series: notInSeries ? null : series.trim() || null,
      seriesNumber: notInSeries || number.trim() === '' ? null : Number(number),
      notInSeries,
    }
    if (body.seriesNumber !== null && !Number.isFinite(body.seriesNumber)) {
      setBusy(false)
      setError('The book number must be a number.')
      return
    }
    try {
      await api.put<BookMetadataEdit>(`/api/listen/items/${item.id}/metadata`, body)
      onSaved()
      onClose()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't save that. Try again.")
      setBusy(false)
    }
  }

  async function reset() {
    setBusy(true)
    setError(null)
    try {
      await api.delete(`/api/listen/items/${item.id}/metadata`)
      onSaved()
      onClose()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't reset that. Try again.")
      setBusy(false)
    }
  }

  const audible = loaded?.audible
  const audibleLine = !audible
    ? null
    : !audible.checked
      ? "Audible hasn't been asked about this book yet."
      : audible.series
        ? `Audible lists it in ${audible.series}${audible.number != null ? `, book ${audible.number}` : ''}.`
        : "Audible doesn't list it in a series (or it wasn't found there)."

  return (
    <form className="book-edit" onSubmit={(e) => void save(e)} aria-label="Edit book details">
      <h3>Edit details</h3>
      <p className="subtitle">For everyone who uses this library. Leave a box empty to keep what is found automatically.</p>
      {audibleLine && <p className="subtitle book-edit-hint">{audibleLine}</p>}

      <label htmlFor={`${id}-author`}>Author</label>
      <input
        id={`${id}-author`}
        list={`${id}-authors`}
        value={author}
        onChange={(e) => setAuthor(e.target.value)}
        placeholder={item.author ?? 'No author on file'}
        maxLength={200}
        autoCapitalize="words"
      />
      <datalist id={`${id}-authors`}>
        {authors.map((n) => (
          <option key={n} value={n} />
        ))}
      </datalist>

      <label className="book-edit-check">
        <input type="checkbox" checked={notInSeries} onChange={(e) => setNotInSeries(e.target.checked)} />
        This book is not part of a series
      </label>

      <label htmlFor={`${id}-series`}>Series</label>
      <input
        id={`${id}-series`}
        list={`${id}-seriesnames`}
        value={notInSeries ? '' : series}
        onChange={(e) => setSeries(e.target.value)}
        placeholder={item.series ?? 'Not in a series'}
        disabled={notInSeries}
        maxLength={200}
        autoCapitalize="words"
      />
      <datalist id={`${id}-seriesnames`}>
        {seriesNames.map((n) => (
          <option key={n} value={n} />
        ))}
      </datalist>

      <label htmlFor={`${id}-number`}>Book number in the series</label>
      <input
        id={`${id}-number`}
        type="number"
        inputMode="decimal"
        min={0}
        step="any"
        value={notInSeries ? '' : number}
        onChange={(e) => setNumber(e.target.value)}
        placeholder={item.seriesNumber != null ? String(item.seriesNumber) : 'e.g. 2'}
        disabled={notInSeries}
      />

      {error && <p className="error">{error}</p>}

      <div className="book-edit-actions">
        <button type="submit" className="primary" disabled={busy || loaded === null}>
          Save
        </button>
        <button type="button" onClick={onClose} disabled={busy}>
          Cancel
        </button>
        {loaded?.override && (
          <button type="button" className="link-btn" onClick={() => void reset()} disabled={busy}>
            Reset to automatic
          </button>
        )}
      </div>
    </form>
  )
}
