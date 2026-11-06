import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { ListenStats } from '../api/types'
import { AppHeader } from '../components/AppHeader'
import { localDateString } from '../player/listenTracker'
import { formatDuration } from '../player/playerMath'
import { usePlayer } from '../state/usePlayer'
import { MarqueeText } from '../components/MarqueeText'

function Tile({ label, value, note }: { label: string; value: string; note?: string }) {
  return (
    <div className="stat-tile">
      <span className="stat-value">{value}</span>
      <span className="stat-label">{label}</span>
      {note && <small className="stat-note">{note}</small>}
    </div>
  )
}

function shortDay(day: string): string {
  const [, m, d] = day.split('-')
  return `${Number(m)}/${Number(d)}`
}

/** How much you've listened: totals, streaks, the last 30 days, and what you've spent the time on. */
export function Stats() {
  const player = usePlayer()
  const [stats, setStats] = useState<ListenStats | null>(null)
  const [error, setError] = useState(false)

  useEffect(() => {
    api
      .get<ListenStats>(`/api/listen/stats?today=${localDateString()}`)
      .then(setStats)
      .catch(() => setError(true))
  }, [])

  const peak = stats ? Math.max(1, ...stats.last30Days.map((d) => d.seconds)) : 1

  return (
    <div className="page">
      <AppHeader title="Your stats" />
      <p className="subtitle">
        Counted from the in-app player. <Link className="header-link" to="/listen">Back to Listen</Link>
      </p>

      {error && <p className="error">Couldn't load your stats.</p>}
      {!stats && !error && <p className="subtitle">Loading…</p>}

      {stats && stats.allTimeSeconds === 0 && (
        <p className="subtitle">Nothing yet. Play a book in the Listen tab and your listening time will start to appear here.</p>
      )}

      {stats && stats.allTimeSeconds > 0 && (
        <>
          <div className="stat-grid">
            <Tile label="Today" value={formatDuration(stats.todaySeconds)} />
            <Tile label="Last 7 days" value={formatDuration(stats.last7DaysSeconds)} />
            <Tile label="Last 30 days" value={formatDuration(stats.last30DaysSeconds)} />
            <Tile label="This year" value={formatDuration(stats.thisYearSeconds)} />
            <Tile label="All time" value={formatDuration(stats.allTimeSeconds)} />
            <Tile
              label="Current streak"
              value={`${stats.currentStreakDays} day${stats.currentStreakDays === 1 ? '' : 's'}`}
              note={`Longest: ${stats.longestStreakDays}`}
            />
          </div>

          <h3 className="stat-heading">Last 30 days</h3>
          <div className="stat-chart" role="img" aria-label="Listening time per day for the last 30 days">
            {stats.last30Days.map((d) => (
              <div key={d.day} className="stat-bar-col" title={`${shortDay(d.day)}: ${formatDuration(d.seconds)}`}>
                <div className="stat-bar" style={{ height: `${(d.seconds / peak) * 100}%` }} />
              </div>
            ))}
          </div>
          <div className="stat-chart-axis">
            <span>{shortDay(stats.last30Days[0].day)}</span>
            <span>today</span>
          </div>

          <div className="stat-grid">
            <Tile
              label="Books finished"
              value={stats.booksFinishedThisYear === null ? '—' : String(stats.booksFinishedThisYear)}
              note={stats.booksFinishedTotal === null ? undefined : `${stats.booksFinishedTotal} in total`}
            />
            <Tile label="Typical listening day" value={formatDuration(stats.averageSecondsPerActiveDay)} note={`${stats.activeDays} days listened`} />
            {stats.timeSavedSeconds >= 60 && (
              <Tile label="Saved by listening faster" value={formatDuration(stats.timeSavedSeconds)} />
            )}
          </div>

          {stats.topBooks.length > 0 && (
            <>
              <h3 className="stat-heading">Most listened</h3>
              <ol className="stat-list">
                {stats.topBooks.map((b) => (
                  <li key={b.itemId}>
                    <button onClick={() => void player.play(b.itemId)}>
                      <span>
                        <MarqueeText as="strong" text={b.title} />
                        {b.author && <small>{b.author}</small>}
                      </span>
                      <span className="stat-list-time">{formatDuration(b.seconds)}</span>
                    </button>
                  </li>
                ))}
              </ol>
            </>
          )}

          {stats.topAuthors.length > 0 && (
            <>
              <h3 className="stat-heading">Favourite authors</h3>
              <ol className="stat-list">
                {stats.topAuthors.map((a) => (
                  <li key={a.author}>
                    <div className="stat-list-row">
                      <strong>{a.author}</strong>
                      <span className="stat-list-time">{formatDuration(a.seconds)}</span>
                    </div>
                  </li>
                ))}
              </ol>
            </>
          )}
        </>
      )}
    </div>
  )
}
