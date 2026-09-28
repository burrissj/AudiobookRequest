import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import type { SetupRequest } from '../api/types'

const initialForm: SetupRequest = {
  readarrUrl: '',
  readarrApiKey: '',
  readarrRootFolderPath: '',
  readarrQualityProfileId: 1,
  readarrMetadataProfileId: 1,
  jellyfinUrl: '',
  jellyfinApiKey: '',
  stagingPath: '',
  libraryPath: '',
  scanIntervalMinutes: 15,
  originalRetentionDays: 90,
  namingPattern: '{Author}/{Title}/{Title}',
}

type TestState = 'idle' | 'testing' | 'ok' | 'failed'

export function Setup() {
  const navigate = useNavigate()
  const [form, setForm] = useState<SetupRequest>(initialForm)
  const [readarrTest, setReadarrTest] = useState<TestState>('idle')
  const [jellyfinTest, setJellyfinTest] = useState<TestState>('idle')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  function set<K extends keyof SetupRequest>(key: K, value: SetupRequest[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function testConnection(kind: 'readarr' | 'jellyfin') {
    const setState = kind === 'readarr' ? setReadarrTest : setJellyfinTest
    const url = kind === 'readarr' ? form.readarrUrl : form.jellyfinUrl
    const apiKey = kind === 'readarr' ? form.readarrApiKey : form.jellyfinApiKey
    setState('testing')
    try {
      const result = await api.post<{ success: boolean }>(`/api/setup/test-${kind}`, { url, apiKey })
      setState(result.success ? 'ok' : 'failed')
    } catch {
      setState('failed')
    }
  }

  async function onSubmit(e: FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      await api.post('/api/setup', form)
      navigate('/login', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Setup failed')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="page setup-page">
      <h1>Welcome — let's get set up</h1>
      <p className="subtitle">Connect Readarr and Jellyfin, then choose where files live.</p>

      <form onSubmit={onSubmit}>
        <fieldset>
          <legend>Readarr</legend>
          <label>
            URL
            <input value={form.readarrUrl} onChange={(e) => set('readarrUrl', e.target.value)} placeholder="http://readarr-host:8787" required />
          </label>
          <label>
            API Key
            <input value={form.readarrApiKey} onChange={(e) => set('readarrApiKey', e.target.value)} required />
          </label>
          <label>
            Root folder path (as Readarr sees it)
            <input value={form.readarrRootFolderPath} onChange={(e) => set('readarrRootFolderPath', e.target.value)} placeholder="/audiobooks" required />
          </label>
          <button type="button" onClick={() => testConnection('readarr')} disabled={!form.readarrUrl || !form.readarrApiKey}>
            Test connection
          </button>
          <TestBadge state={readarrTest} />
        </fieldset>

        <fieldset>
          <legend>Jellyfin</legend>
          <label>
            URL
            <input value={form.jellyfinUrl} onChange={(e) => set('jellyfinUrl', e.target.value)} placeholder="http://jellyfin-host:8096" required />
          </label>
          <label>
            API Key
            <input value={form.jellyfinApiKey} onChange={(e) => set('jellyfinApiKey', e.target.value)} required />
          </label>
          <button type="button" onClick={() => testConnection('jellyfin')} disabled={!form.jellyfinUrl || !form.jellyfinApiKey}>
            Test connection
          </button>
          <TestBadge state={jellyfinTest} />
        </fieldset>

        <fieldset>
          <legend>File handling</legend>
          <label>
            Staging path (this service's scratch area)
            <input value={form.stagingPath} onChange={(e) => set('stagingPath', e.target.value)} placeholder="C:\AudiobookRequest\staging" required />
          </label>
          <label>
            Library path (final Jellyfin-served library)
            <input value={form.libraryPath} onChange={(e) => set('libraryPath', e.target.value)} placeholder="C:\Audiobooks" required />
          </label>
          <label>
            Naming pattern
            <input value={form.namingPattern} onChange={(e) => set('namingPattern', e.target.value)} />
          </label>
          <label>
            Scan interval (minutes)
            <input type="number" min={1} value={form.scanIntervalMinutes} onChange={(e) => set('scanIntervalMinutes', Number(e.target.value))} />
          </label>
          <label>
            Keep original downloads for (days)
            <input type="number" min={1} value={form.originalRetentionDays} onChange={(e) => set('originalRetentionDays', Number(e.target.value))} />
          </label>
        </fieldset>

        {error && <p className="error">{error}</p>}

        <button type="submit" className="primary" disabled={submitting}>
          {submitting ? 'Saving…' : 'Finish setup'}
        </button>
      </form>
    </div>
  )
}

function TestBadge({ state }: { state: TestState }) {
  if (state === 'idle') return null
  if (state === 'testing') return <span className="badge">Testing…</span>
  if (state === 'ok') return <span className="badge badge-ok">Connected</span>
  return <span className="badge badge-fail">Couldn't connect</span>
}
