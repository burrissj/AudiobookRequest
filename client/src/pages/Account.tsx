import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, ApiError } from '../api/client'
import type { AccountNotifications, ChannelResult, VersionInfo } from '../api/types'
import { AppHeader } from '../components/AppHeader'
import { GetAppLinks } from '../components/JellyfinLinks'
import { PlayerPrefsForm } from '../components/PlayerPrefsForm'

interface TestResponse {
  results: ChannelResult[]
  anyConfigured: boolean
}

// A hard-to-guess topic: on a public ntfy server, anyone who knows the name can read it.
function randomTopic(): string {
  const bytes = new Uint8Array(6)
  crypto.getRandomValues(bytes)
  return 'abr-' + Array.from(bytes, (b) => b.toString(36).padStart(2, '0')).join('')
}

/** Where this user wants their own alerts sent, on top of whatever the household channels do. */
export function Account() {
  const [form, setForm] = useState<AccountNotifications | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [saving, setSaving] = useState(false)
  const [saved, setSaved] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [test, setTest] = useState<TestResponse | 'running' | 'failed' | null>(null)
  const [version, setVersion] = useState<VersionInfo | null>(null)

  useEffect(() => {
    api.get<AccountNotifications>('/api/account/notifications').then(setForm).catch(() => setLoadError(true))
    api.get<VersionInfo>('/api/app/version').then(setVersion).catch(() => {})
  }, [])

  function set<K extends keyof AccountNotifications>(key: K, value: AccountNotifications[K]) {
    setSaved(false)
    setForm((f) => (f ? { ...f, [key]: value } : f))
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    if (!form) return
    setSaving(true)
    setError(null)
    try {
      const updated = await api.put<AccountNotifications>('/api/account/notifications', {
        ntfyTopic: form.ntfyTopic,
        telegramChatId: form.telegramChatId,
        email: form.email,
        onReady: form.onReady,
        onFailed: form.onFailed,
        onNewRelease: form.onNewRelease,
      })
      setForm(updated)
      setSaved(true)
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not save')
    } finally {
      setSaving(false)
    }
  }

  async function sendTest() {
    setTest('running')
    try {
      setTest(await api.post<TestResponse>('/api/account/notifications/test'))
    } catch {
      setTest('failed')
    }
  }

  return (
    <div className="page">
      <AppHeader title="Account" />
      <p className="subtitle">
        Get your own alerts when a book you requested is ready, when a request fails, and when an author or series you{' '}
        <Link to="/following">follow</Link> has something new. These are in addition to any shared channel the admin set up.
      </p>

      {loadError && <p className="error">Couldn't load your settings.</p>}
      {!form && !loadError && <p className="subtitle">Loading…</p>}

      {form && (
        <form onSubmit={save}>
          <fieldset>
            <legend>ntfy (phone push)</legend>
            <p className="subtitle" style={{ margin: 0 }}>
              Install the free ntfy app, subscribe to your topic, and alerts arrive as phone notifications. Server: {form.available.ntfyServer}
            </p>
            <label>
              Your topic
              <input value={form.ntfyTopic ?? ''} onChange={(e) => set('ntfyTopic', e.target.value || null)} placeholder="abr-your-secret-name" />
            </label>
            <button type="button" onClick={() => set('ntfyTopic', randomTopic())}>
              Make me a private topic name
            </button>
          </fieldset>

          <fieldset>
            <legend>Email</legend>
            {form.available.email ? (
              <label>
                Your address
                <input type="email" value={form.email ?? ''} onChange={(e) => set('email', e.target.value || null)} placeholder="you@example.com" />
              </label>
            ) : (
              <p className="subtitle" style={{ margin: 0 }}>
                Email isn't available yet. Ask the admin to set up an email server under Settings → Notifications.
              </p>
            )}
          </fieldset>

          <fieldset>
            <legend>Telegram</legend>
            {form.available.telegram ? (
              <label>
                Your chat ID
                <input value={form.telegramChatId ?? ''} onChange={(e) => set('telegramChatId', e.target.value || null)} placeholder="123456789" />
              </label>
            ) : (
              <p className="subtitle" style={{ margin: 0 }}>
                Telegram isn't available yet. Ask the admin to add a Telegram bot under Settings → Notifications.
              </p>
            )}
            {form.available.telegram && (
              <p className="subtitle" style={{ margin: 0 }}>
                Send any message to the bot first, then find your chat ID by messaging @userinfobot.
              </p>
            )}
          </fieldset>

          <fieldset>
            <legend>Tell me when</legend>
            <label className="checkbox-label">
              <input type="checkbox" checked={form.onReady} onChange={(e) => set('onReady', e.target.checked)} />A book I requested is ready
            </label>
            <label className="checkbox-label">
              <input type="checkbox" checked={form.onFailed} onChange={(e) => set('onFailed', e.target.checked)} />
              A request of mine fails or is declined
            </label>
            <label className="checkbox-label">
              <input type="checkbox" checked={form.onNewRelease} onChange={(e) => set('onNewRelease', e.target.checked)} />
              An author or series I follow has a new book
            </label>
          </fieldset>

          {error && <p className="error">{error}</p>}
          {saved && <p className="success">Saved.</p>}

          <button type="submit" className="primary" disabled={saving}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          <button type="button" onClick={sendTest} disabled={test === 'running'}>
            {test === 'running' ? 'Sending…' : 'Send me a test (save first)'}
          </button>
          {test === 'failed' && <p className="error">Couldn't send the test.</p>}
          {test && typeof test === 'object' && !test.anyConfigured && (
            <p className="subtitle">Nothing to send to yet: fill in a channel above, save, then test.</p>
          )}
          {test &&
            typeof test === 'object' &&
            test.results.map((r) => (
              <p key={r.channel} className={r.success ? 'success' : 'error'}>
                {r.channel}: {r.success ? 'sent' : `failed (${r.error})`}
              </p>
            ))}
        </form>
      )}

      <h3>Listening</h3>
      <PlayerPrefsForm />
      <GetAppLinks />
      <p className="subtitle">
        <Link className="header-link" to="/stats">
          Your listening stats
        </Link>
      </p>

      {version && (
        <>
          <h3>What's new</h3>
          <p className="subtitle">You're on version {version.version}.</p>
          {version.changelog.map((entry, i) => (
            <details key={entry.version} className="changelog-entry" open={i === 0}>
              <summary>
                Version {entry.version} <small>{entry.date}</small>
              </summary>
              <ul>
                {entry.items.map((item) => (
                  <li key={item}>{item}</li>
                ))}
              </ul>
            </details>
          ))}
        </>
      )}
    </div>
  )
}
