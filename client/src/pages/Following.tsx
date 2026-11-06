import { Link, useNavigate } from 'react-router-dom'
import { AppHeader } from '../components/AppHeader'
import { useFollows } from '../state/useFollows'

function checked(iso: string | null): string {
  return iso ? `Last checked ${new Date(iso).toLocaleString()}` : 'Waiting for the first check'
}

/** Everything the user follows, with the auto-request choice and unfollow for each. */
export function Following() {
  const navigate = useNavigate()
  const { follows, loaded, error, unfollow, setAutoRequest } = useFollows()

  return (
    <div className="page">
      <button onClick={() => navigate(-1)}>← Back</button>
      <AppHeader title="Following" />
      <p className="subtitle">
        New books from these authors and series show up on For You, and on your notification channels. Follow more from any book or author page.
      </p>
      {error && <p className="error">{error}</p>}
      {loaded && follows.length === 0 && (
        <p className="subtitle">
          You're not following anyone yet. Open a book and tap its author to follow them.
        </p>
      )}
      <ul className="book-list">
        {follows.map((f) => (
          <li key={f.id} className="follow-row">
            <div className="book-info">
              <strong>
                {f.kind === 'Author' ? (
                  <Link to={`/category/a?q=${encodeURIComponent(f.name)}`}>{f.name}</Link>
                ) : (
                  f.name
                )}
              </strong>
              <span className="subtitle follow-meta">
                {f.kind === 'Author' ? 'Author' : 'Series'} · {checked(f.lastCheckedAt)}
              </span>
              <label className="checkbox-label follow-auto">
                <input type="checkbox" checked={f.autoRequest} onChange={(e) => setAutoRequest(f.id, e.target.checked)} />
                {f.kind === 'Author' ? 'Request new books automatically' : 'Request the next book after I finish one'}
              </label>
            </div>
            <button onClick={() => unfollow(f.id)}>Unfollow</button>
          </li>
        ))}
      </ul>
    </div>
  )
}
