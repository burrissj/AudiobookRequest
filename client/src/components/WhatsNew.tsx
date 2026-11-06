import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api/client'
import type { VersionInfo } from '../api/types'
import { entriesToShow } from '../lib/version'

const SEEN_KEY = 'abr.seenVersion'

function readSeen(): string | null | undefined {
  try {
    return localStorage.getItem(SEEN_KEY)
  } catch {
    // Storage blocked: say nothing rather than nagging on every visit.
    return undefined
  }
}

/** After the app is updated, tells each person once what's new -- in plain words, with a way to see the rest. */
export function WhatsNew() {
  const [info, setInfo] = useState<VersionInfo | null>(null)
  const [entries, setEntries] = useState<VersionInfo['changelog']>([])

  useEffect(() => {
    const seen = readSeen()
    if (seen === undefined) return
    api
      .get<VersionInfo>('/api/app/version')
      .then((v) => {
        const show = entriesToShow(v.changelog, seen, v.version)
        if (show.length > 0) {
          setInfo(v)
          setEntries(show)
        } else {
          // Up to date (or a downgrade): remember the running version so a later update compares against it.
          try {
            localStorage.setItem(SEEN_KEY, v.version)
          } catch {
            // ignore
          }
        }
      })
      .catch(() => {})
  }, [])

  if (!info || entries.length === 0) return null

  function dismiss() {
    try {
      if (info) localStorage.setItem(SEEN_KEY, info.version)
    } catch {
      // ignore
    }
    setInfo(null)
  }

  return (
    <div className="whats-new-backdrop" role="dialog" aria-modal="true" aria-label="What's new">
      <div className="whats-new-card">
        <h2>What's new</h2>
        {entries.map((e) => (
          <section key={e.version}>
            <h3>
              Version {e.version} <small>{e.date}</small>
            </h3>
            <ul>
              {e.items.map((item) => (
                <li key={item}>{item}</li>
              ))}
            </ul>
          </section>
        ))}
        <div className="whats-new-actions">
          <Link className="header-link" to="/account" onClick={dismiss}>
            All past changes
          </Link>
          <button className="primary" onClick={dismiss}>
            Got it
          </button>
        </div>
      </div>
    </div>
  )
}
