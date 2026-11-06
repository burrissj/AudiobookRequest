import { useEffect, useState, type FormEvent } from 'react'
import { api, ApiError } from '../../api/client'
import type { ImportCandidate, ImportIdentify, ImportMatch, ImportPreview } from '../../api/types'
import { formatBytes } from '../../lib/format'
import { formatClock, formatDuration } from '../../player/playerMath'

type Step = 'identify' | 'confirm'

interface ImportWizardProps {
  candidate: ImportCandidate
  hasNext: boolean
  onSkip: () => void
  /** The book was added to the conversion queue; the page moves on to the next one. */
  onQueued: () => void
  onBack: () => void
}

interface Chosen {
  title: string
  author: string
  series: string
  coverUrl: string | null
}

/** One book: identify it, confirm the details, and add it to the conversion queue. */
export function ImportWizard({ candidate, hasNext, onSkip, onQueued, onBack }: ImportWizardProps) {
  const [step, setStep] = useState<Step>('identify')
  const [chosen, setChosen] = useState<Chosen>({ title: '', author: '', series: '', coverUrl: null })

  return (
    <div className="import-wizard">
      <div className="import-wizard-head">
        <button className="link-btn" onClick={onBack}>
          ← All books
        </button>
        <strong>{candidate.name}</strong>
      </div>

      {step === 'identify' && (
        <IdentifyStep
          candidate={candidate}
          hasNext={hasNext}
          onSkip={onSkip}
          onPick={(c) => {
            setChosen(c)
            setStep('confirm')
          }}
        />
      )}
      {step === 'confirm' && (
        <ConfirmStep candidate={candidate} chosen={chosen} onChange={setChosen} onBack={() => setStep('identify')} onQueued={onQueued} />
      )}
    </div>
  )
}

// ---------------------------------------------------------------------------------------------------------------

function IdentifyStep({ candidate, hasNext, onSkip, onPick }: { candidate: ImportCandidate; hasNext: boolean; onSkip: () => void; onPick: (c: Chosen) => void }) {
  // Results are kept with the search that produced them; a new search simply isn't shown until its answer arrives.
  const [searchKey, setSearchKey] = useState(0)
  const [query, setQuery] = useState<{ title: string | null; author: string | null }>({ title: null, author: null })
  const [result, setResult] = useState<{ key: number; data: ImportIdentify | null; error: string | null } | null>(null)
  const [titleText, setTitleText] = useState('')
  const [authorText, setAuthorText] = useState('')
  const [filled, setFilled] = useState(false)

  useEffect(() => {
    let current = true
    api
      .post<ImportIdentify>(`/api/admin/import/candidates/${candidate.id}/identify`, { query: query.title, author: query.author })
      .then((data) => {
        if (!current) return
        setResult({ key: searchKey, data, error: null })
        // The first answer pre-fills the boxes with what was guessed; later ones leave what the admin typed.
        if (!filled) {
          setTitleText(data.query)
          setAuthorText(data.guessAuthor ?? '')
          setFilled(true)
        }
      })
      .catch((err) => current && setResult({ key: searchKey, data: null, error: err instanceof ApiError ? err.message : "Couldn't look at that book." }))
    return () => {
      current = false
    }
    // `filled` is read only to decide whether to pre-fill; changing it must not search again.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [candidate.id, searchKey, query])

  const loading = result?.key !== searchKey
  const data = !loading ? result?.data : null

  function search(e: FormEvent) {
    e.preventDefault()
    setQuery({ title: titleText, author: authorText })
    setSearchKey((n) => n + 1)
  }

  return (
    <div>
      {loading && <p className="subtitle">Looking at the files and searching…</p>}
      {!loading && result?.error && <p className="error">{result.error}</p>}

      {data && (
        <>
          <details className="import-files">
            <summary>
              {data.files.length} file{data.files.length === 1 ? '' : 's'} · {formatDuration(data.totalSeconds)} · {formatBytes(candidate.totalBytes)}
            </summary>
            <ul>
              {data.files.map((f) => (
                <li key={f.name} className={f.problem ? 'error' : undefined}>
                  {f.name} <small>{f.problem ?? `${formatClock(f.seconds)} · ${f.codec ?? '?'}`}</small>
                </li>
              ))}
            </ul>
          </details>
          {data.problems.length > 0 && (
            <p className="error">
              {data.problems.length === 1 ? 'A file' : `${data.problems.length} files`} can't be read as audio, so this book can't be imported until that's sorted out.
            </p>
          )}
        </>
      )}

      <form className="import-search" onSubmit={search}>
        <label>
          Title
          <input value={titleText} onChange={(e) => setTitleText(e.target.value)} placeholder="Book title" autoCapitalize="none" autoCorrect="off" />
        </label>
        <label>
          Author
          <input value={authorText} onChange={(e) => setAuthorText(e.target.value)} placeholder="Author (optional)" autoCapitalize="none" autoCorrect="off" />
        </label>
        <button type="submit" disabled={loading || titleText.trim() === ''}>
          Search again
        </button>
      </form>

      {data && data.matches.length === 0 && <p className="subtitle">Nothing found for that. Change the title or author above, or enter the details yourself.</p>}

      {data && data.matches.length > 0 && (
        <ul className="import-matches">
          {data.matches.map((m, i) => (
            <MatchRow key={m.asin} match={m} best={i === 0 && m.score >= 5} onUse={() => onPick({ title: m.title, author: m.author, series: m.series ?? '', coverUrl: m.coverUrl })} />
          ))}
        </ul>
      )}

      <div className="import-actions">
        <button onClick={() => onPick({ title: data?.guessTitle ?? titleText, author: data?.guessAuthor ?? authorText, series: '', coverUrl: null })} disabled={loading}>
          None of these: I'll type the details
        </button>
        <button className="link-btn" onClick={onSkip}>
          {hasNext ? 'Skip to the next book' : 'Skip this book'}
        </button>
      </div>
    </div>
  )
}

function MatchRow({ match, best, onUse }: { match: ImportMatch; best: boolean; onUse: () => void }) {
  return (
    <li className={`import-match${best ? ' import-match-best' : ''}`}>
      {match.coverUrl ? <img src={match.coverUrl} alt="" loading="lazy" className="import-cover" /> : <div className="import-cover import-cover-empty" />}
      <div className="import-match-text">
        <strong>{match.title}</strong>
        <small>{match.author}</small>
        {match.series && (
          <small>
            {match.series}
            {match.seriesNumber ? ` #${match.seriesNumber}` : ''}
          </small>
        )}
        {match.narrators.length > 0 && <small>Read by {match.narrators.slice(0, 2).join(', ')}</small>}
        {match.runtimeMinutes ? <small>{formatDuration(match.runtimeMinutes * 60)}</small> : null}
        {match.lengthNote && <small className="import-length">{match.lengthNote}</small>}
        {best && <small className="import-best">Best match</small>}
      </div>
      <button className="primary" onClick={onUse}>
        Use this
      </button>
    </li>
  )
}

// ---------------------------------------------------------------------------------------------------------------

function ConfirmStep({
  candidate,
  chosen,
  onChange,
  onBack,
  onQueued,
}: {
  candidate: ImportCandidate
  chosen: Chosen
  onChange: (c: Chosen) => void
  onBack: () => void
  onQueued: () => void
}) {
  const [sourceAction, setSourceAction] = useState<'keep' | 'move'>('keep')
  const [confirmed, setConfirmed] = useState(false)
  const [starting, setStarting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [preview, setPreview] = useState<{ key: string; data: ImportPreview | null; error: string | null } | null>(null)

  const title = chosen.title.trim()
  const author = chosen.author.trim()
  const series = chosen.series.trim()
  const key = `${title}|${author}|${series}`
  const ready = title !== '' && author !== ''

  // Where it will go, refreshed shortly after the details stop changing.
  useEffect(() => {
    if (!ready) return
    let current = true
    const handle = window.setTimeout(() => {
      const params = new URLSearchParams({ title, author })
      if (series) params.set('series', series)
      api
        .get<ImportPreview>(`/api/admin/import/preview?${params}`)
        .then((data) => current && setPreview({ key, data, error: null }))
        .catch((err) => current && setPreview({ key, data: null, error: err instanceof ApiError ? err.message : "Couldn't work out where it would go." }))
    }, 350)
    return () => {
      current = false
      window.clearTimeout(handle)
    }
  }, [key, ready, title, author, series])

  const current = preview?.key === key ? preview : null
  const needsConfirm = !!current?.data && (current.data.fileExists || candidate.imported || current.data.alreadyInJellyfin)

  async function start() {
    setStarting(true)
    setError(null)
    try {
      await api.post(`/api/admin/import/candidates/${candidate.id}/import`, {
        title,
        author,
        series: series || null,
        coverUrl: chosen.coverUrl,
        sourceAction,
        overwrite: !!current?.data && (current.data.fileExists || candidate.imported),
      })
      onQueued()
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "Couldn't add it to the queue.")
      setStarting(false)
    }
  }

  return (
    <div className="import-confirm">
      <label>
        Title
        <input value={chosen.title} onChange={(e) => onChange({ ...chosen, title: e.target.value })} maxLength={200} />
      </label>
      <label>
        Author
        <input value={chosen.author} onChange={(e) => onChange({ ...chosen, author: e.target.value })} maxLength={200} />
      </label>
      <label>
        Series <small>(optional)</small>
        <input value={chosen.series} onChange={(e) => onChange({ ...chosen, series: e.target.value })} maxLength={200} />
      </label>
      {chosen.coverUrl && (
        <div className="import-cover-row">
          <img src={chosen.coverUrl} alt="" className="import-cover" />
          <small>This cover will be embedded in the file.</small>
        </div>
      )}

      <div className="health-item import-preview">
        {!ready && <p className="subtitle">Enter a title and an author.</p>}
        {ready && !current && <p className="subtitle">Working out where it will go…</p>}
        {current?.error && <p className="error">{current.error}</p>}
        {current?.data && (
          <>
            <p>
              Will be filed as <code>{current.data.relativePath}</code> in your library.
            </p>
            {current.data.fileExists && <p className="diagnostic-text">A book is already filed at that exact place. Importing will replace it.</p>}
            {current.data.alreadyInJellyfin && <p className="diagnostic-text">Jellyfin already seems to have this book (perhaps another edition).</p>}
            {candidate.imported && <p className="diagnostic-text">You've already imported this folder once.</p>}
          </>
        )}
      </div>

      <label>
        Afterwards, the original files
        <select value={sourceAction} onChange={(e) => setSourceAction(e.target.value as 'keep' | 'move')}>
          <option value="keep">stay where they are (they won't be offered again)</option>
          <option value="move">move into an "_imported" folder beside them</option>
        </select>
      </label>
      <small className="import-note">The originals are never deleted. The new file is an .m4b with the title, author, series and cover written into it.</small>

      {needsConfirm && (
        <label className="checkbox-label">
          <input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} />
          Go ahead anyway
        </label>
      )}

      {error && <p className="error">{error}</p>}

      <div className="import-actions">
        <button className="primary" onClick={() => void start()} disabled={starting || !ready || !current?.data || (needsConfirm && !confirmed)}>
          {starting ? 'Adding…' : 'Import this book'}
        </button>
        <button onClick={onBack} disabled={starting}>
          Back to the matches
        </button>
      </div>
      <small className="import-note">It joins the queue and converts in the background, one book at a time, so you can go straight on to the next one.</small>
    </div>
  )
}
