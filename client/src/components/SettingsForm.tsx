import { useState, type FormEvent } from 'react'
import { api } from '../api/client'
import type { NotificationTestResponse, SetupRequest } from '../api/types'

type TestState = 'idle' | 'testing' | 'ok' | 'failed'

interface SettingsFormProps {
  /** Shows the sections that only make sense once an admin is signed in (notifications and the test button). */
  adminTools?: boolean
  initialValues: SetupRequest
  submitLabel: string
  submittingLabel: string
  onSubmit: (form: SetupRequest) => Promise<void>
}

/** The Chaptarr/Jellyfin/file-handling form, shared by the first-run Setup wizard and the Settings tab. */
export function SettingsForm({ adminTools = false, initialValues, submitLabel, submittingLabel, onSubmit }: SettingsFormProps) {
  const [form, setForm] = useState<SetupRequest>(initialValues)
  const [chaptarrTest, setChaptarrTest] = useState<TestState>('idle')
  const [jellyfinTest, setJellyfinTest] = useState<TestState>('idle')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [notifyTest, setNotifyTest] = useState<NotificationTestResponse | 'running' | 'failed' | null>(null)

  function set<K extends keyof SetupRequest>(key: K, value: SetupRequest[K]) {
    setForm((f) => ({ ...f, [key]: value }))
  }

  async function testConnection(kind: 'chaptarr' | 'jellyfin') {
    const setState = kind === 'chaptarr' ? setChaptarrTest : setJellyfinTest
    const url = kind === 'chaptarr' ? form.chaptarrUrl : form.jellyfinUrl
    const apiKey = kind === 'chaptarr' ? form.chaptarrApiKey : form.jellyfinApiKey
    setState('testing')
    try {
      const result = await api.post<{ success: boolean }>(`/api/setup/test-${kind}`, { url, apiKey })
      setState(result.success ? 'ok' : 'failed')
    } catch {
      setState('failed')
    }
  }

  // Tests what's saved on the server (the channels read their settings from the database), so save first.
  async function sendTestNotification() {
    setNotifyTest('running')
    try {
      setNotifyTest(await api.post<NotificationTestResponse>('/api/admin/notifications/test'))
    } catch {
      setNotifyTest('failed')
    }
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault()
    setSubmitting(true)
    setError(null)
    try {
      await onSubmit(form)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to save settings')
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <form onSubmit={handleSubmit}>
      <fieldset>
        <legend>Chaptarr</legend>
        <label>
          URL
          <input value={form.chaptarrUrl} onChange={(e) => set('chaptarrUrl', e.target.value)} placeholder="http://chaptarr-host:8789" required />
        </label>
        <label>
          API Key
          <input value={form.chaptarrApiKey} onChange={(e) => set('chaptarrApiKey', e.target.value)} required />
        </label>
        <label>
          Root folder path (as Chaptarr sees it)
          <input value={form.chaptarrRootFolderPath} onChange={(e) => set('chaptarrRootFolderPath', e.target.value)} placeholder="/audiobooks" required />
        </label>
        <button type="button" onClick={() => testConnection('chaptarr')} disabled={!form.chaptarrUrl || !form.chaptarrApiKey}>
          Test connection
        </button>
        <TestBadge state={chaptarrTest} />
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
        <label>
          Jellyfin address for users (optional)
          <input
            value={form.jellyfinPublicUrl ?? ''}
            onChange={(e) => set('jellyfinPublicUrl', e.target.value || null)}
            placeholder="https://jellyfin.example.net"
          />
        </label>
        <p className="subtitle" style={{ margin: 0 }}>
          The address people use on their own devices. The "Open Jellyfin" and "Listen" links use it; leave it blank to use the URL above,
          which may only work on your home network.
        </p>
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
        <label>
          Pause processing when free disk space drops below (GB, 0 = never)
          <input type="number" min={0} value={form.minFreeDiskGb} onChange={(e) => set('minFreeDiskGb', Number(e.target.value))} />
        </label>
      </fieldset>

      <fieldset>
        <legend>Processing</legend>
        <p className="subtitle" style={{ margin: 0 }}>
          Control what this app does to a finished download before it reaches your library. Turn these
          off if Chaptarr (or something else) already handles that step the way you want.
        </p>
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={form.enableMoving}
            onChange={(e) => set('enableMoving', e.target.checked)}
          />
          Move into library path
        </label>
        <p className="subtitle" style={{ margin: '0 0 0 1.5em' }}>
          When on, Chaptarr's original files are deleted after the retention period. When off they are
          never touched. If this and the tag/cover option below are both off, nothing is copied or
          converted — this app just waits for Chaptarr's own final file to appear, marks the request
          Ready, and tells Jellyfin to rescan.
        </p>
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={form.enableRenaming}
            disabled={!form.enableMoving && !form.enableConversion}
            onChange={(e) => set('enableRenaming', e.target.checked)}
          />
          Rename files using the naming pattern
        </label>
        <p className="subtitle" style={{ margin: '0 0 0 1.5em' }}>
          When off, the original filename Chaptarr produced is kept (still filed under the naming
          pattern's folder structure).
        </p>
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={form.enableConversion}
            onChange={(e) => set('enableConversion', e.target.checked)}
          />
          Embed metadata tags &amp; cover art
        </label>
        <p className="subtitle" style={{ margin: '0 0 0 1.5em' }}>
          Works independently of moving: with moving off, a merged, tagged copy is written to the
          library path and Chaptarr's files are left alone. When this is off but moving is on,
          multi-part books are still combined into one .m4b with chapter markers, just without title,
          author, and cover art.
        </p>
        <label className="checkbox-label">
          <input type="checkbox" checked={form.autoFixImports} onChange={(e) => set('autoFixImports', e.target.checked)} />
          Fix blocked imports automatically
        </label>
        <p className="subtitle" style={{ margin: '0 0 0 1.5em' }}>
          When Chaptarr refuses a finished download only because it matched a sibling edition of the requested book (same
          author, same title apart from a suffix like "(Part 2 of 2)"), import it as the requested book without waiting for an
          admin. Anything less clear-cut is left for you, with the Fix import button.
        </p>
      </fieldset>

      <fieldset>
        <legend>Requests</legend>
        <label className="checkbox-label">
          <input type="checkbox" checked={form.requireApproval} onChange={(e) => set('requireApproval', e.target.checked)} />
          Require admin approval for new requests
        </label>
        <p className="subtitle" style={{ margin: '0 0 0 1.5em' }}>
          Requests from non-admin users wait on the Admin tab until you approve or decline them. Admins are never held.
        </p>
        <label>
          Requests allowed per user per week (0 = unlimited)
          <input
            type="number"
            min={0}
            value={form.maxRequestsPerUserPerWeek}
            onChange={(e) => set('maxRequestsPerUserPerWeek', Number(e.target.value))}
          />
        </label>
        <label>
          Wishlisted requests search again every (hours)
          <input type="number" min={1} value={form.wishlistRetryHours} onChange={(e) => set('wishlistRetryHours', Number(e.target.value))} />
        </label>
      </fieldset>

      {adminTools && (
        <fieldset>
          <legend>Notifications</legend>
          <p className="subtitle" style={{ margin: 0 }}>
            Get told when a book is ready or fails, so nobody has to keep checking. Configure any channels you like;
            messages name who requested the book.
          </p>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.notifyOnReady} onChange={(e) => set('notifyOnReady', e.target.checked)} />
            When a book is ready to listen
          </label>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.notifyOnFailed} onChange={(e) => set('notifyOnFailed', e.target.checked)} />
            When a request fails or is declined
          </label>
          <label className="checkbox-label">
            <input
              type="checkbox"
              checked={form.notifyOnPendingApproval}
              onChange={(e) => set('notifyOnPendingApproval', e.target.checked)}
            />
            When a request needs approval
          </label>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.notifyOnNewRelease} onChange={(e) => set('notifyOnNewRelease', e.target.checked)} />
            When an author or series someone follows has a new book
          </label>
          <label>
            Discord webhook URL
            <input
              value={form.notifyDiscordWebhookUrl ?? ''}
              onChange={(e) => set('notifyDiscordWebhookUrl', e.target.value || null)}
              placeholder="https://discord.com/api/webhooks/..."
            />
          </label>
          <label>
            Telegram bot token
            <input value={form.notifyTelegramBotToken ?? ''} onChange={(e) => set('notifyTelegramBotToken', e.target.value || null)} />
          </label>
          <label>
            Telegram chat ID
            <input value={form.notifyTelegramChatId ?? ''} onChange={(e) => set('notifyTelegramChatId', e.target.value || null)} />
          </label>
          <label>
            ntfy topic URL
            <input
              value={form.notifyNtfyUrl ?? ''}
              onChange={(e) => set('notifyNtfyUrl', e.target.value || null)}
              placeholder="https://ntfy.sh/my-audiobooks"
            />
          </label>
          <label>
            Generic webhook URL (receives a JSON POST)
            <input value={form.notifyWebhookUrl ?? ''} onChange={(e) => set('notifyWebhookUrl', e.target.value || null)} />
          </label>
          <label>
            Email: SMTP server
            <input value={form.smtpHost ?? ''} onChange={(e) => set('smtpHost', e.target.value || null)} placeholder="smtp.example.com" />
          </label>
          <label>
            SMTP port
            <input type="number" min={1} value={form.smtpPort} onChange={(e) => set('smtpPort', Number(e.target.value))} />
          </label>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.smtpUseSsl} onChange={(e) => set('smtpUseSsl', e.target.checked)} />
            Use SSL/TLS
          </label>
          <label>
            SMTP username
            <input value={form.smtpUsername ?? ''} onChange={(e) => set('smtpUsername', e.target.value || null)} />
          </label>
          <label>
            SMTP password
            <input type="password" value={form.smtpPassword ?? ''} onChange={(e) => set('smtpPassword', e.target.value || null)} />
          </label>
          <label>
            Send email from
            <input value={form.smtpFrom ?? ''} onChange={(e) => set('smtpFrom', e.target.value || null)} placeholder="audiobooks@example.com" />
          </label>
          <label>
            Send email to (comma separated)
            <input value={form.smtpTo ?? ''} onChange={(e) => set('smtpTo', e.target.value || null)} />
          </label>
          <button type="button" onClick={sendTestNotification} disabled={notifyTest === 'running'}>
            {notifyTest === 'running' ? 'Sending…' : 'Send test notification (uses saved settings)'}
          </button>
          {notifyTest === 'failed' && <p className="error">Couldn't send the test.</p>}
          {notifyTest && typeof notifyTest === 'object' && !notifyTest.anyConfigured && (
            <p className="subtitle">No channel is saved yet. Fill one in, click Save changes, then test.</p>
          )}
          {notifyTest &&
            typeof notifyTest === 'object' &&
            notifyTest.results.map((r) => (
              <p key={r.channel} className={r.success ? 'success' : 'error'}>
                {r.channel}: {r.success ? 'sent' : `failed (${r.error})`}
              </p>
            ))}
        </fieldset>
      )}

      {adminTools && (
        <fieldset>
          <legend>Backups</legend>
          <label>
            Daily database backups to keep (0 = no backups)
            <input
              type="number"
              min={0}
              value={form.backupRetentionCount}
              onChange={(e) => set('backupRetentionCount', Number(e.target.value))}
            />
          </label>
          <p className="subtitle" style={{ margin: 0 }}>
            Every request, follow and setting lives in one database file. Backups are listed, and can be downloaded, on the
            Admin tab's Health page.
          </p>
        </fieldset>
      )}

      <fieldset>
        <legend>Remote path mapping (optional)</legend>
        <p className="subtitle" style={{ margin: 0 }}>
          Only needed if Chaptarr runs on a different machine than this service. Leave both blank if
          Chaptarr's reported file paths already work here as-is.
        </p>
        <label>
          Chaptarr-side path prefix
          <input
            value={form.chaptarrFilePathPrefix ?? ''}
            onChange={(e) => set('chaptarrFilePathPrefix', e.target.value || null)}
            placeholder="C:\audiobooks"
          />
        </label>
        <label>
          Equivalent path from this machine
          <input
            value={form.localFilePathPrefix ?? ''}
            onChange={(e) => set('localFilePathPrefix', e.target.value || null)}
            placeholder="\\server\share\audiobooks"
          />
        </label>
      </fieldset>

      {error && <p className="error">{error}</p>}

      <button type="submit" className="primary" disabled={submitting}>
        {submitting ? submittingLabel : submitLabel}
      </button>
    </form>
  )
}

function TestBadge({ state }: { state: TestState }) {
  if (state === 'idle') return null
  if (state === 'testing') return <span className="badge">Testing…</span>
  if (state === 'ok') return <span className="badge badge-ok">Connected</span>
  return <span className="badge badge-fail">Couldn't connect</span>
}
