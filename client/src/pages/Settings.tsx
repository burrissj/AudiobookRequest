import { useEffect, useState } from 'react'
import { api } from '../api/client'
import { AppHeader } from '../components/AppHeader'
import { SettingsForm } from '../components/SettingsForm'
import { useAuth } from '../auth/useAuth'
import type { SetupRequest } from '../api/types'

export function Settings() {
  const { user } = useAuth()
  const isAdmin = user?.role === 'Admin'
  const [current, setCurrent] = useState<SetupRequest | null>(null)
  const [loadError, setLoadError] = useState(false)
  const [justSaved, setJustSaved] = useState(false)

  useEffect(() => {
    if (!isAdmin) return
    api
      .get<SetupRequest>('/api/setup')
      .then(setCurrent)
      .catch(() => setLoadError(true))
  }, [isAdmin])

  if (!isAdmin) {
    return (
      <div className="page">
        <AppHeader title="Settings" />
        <p className="subtitle">Only an admin can view or change app settings.</p>
      </div>
    )
  }

  async function handleSubmit(form: SetupRequest) {
    await api.post('/api/setup', form)
    setCurrent(form)
    setJustSaved(true)
    setTimeout(() => setJustSaved(false), 3000)
  }

  return (
    <div className="page">
      <AppHeader title="Settings" />
      {justSaved && <p className="success">Settings saved.</p>}
      {loadError && <p className="error">Failed to load current settings.</p>}
      {current && (
        <SettingsForm adminTools initialValues={current} submitLabel="Save changes" submittingLabel="Saving…" onSubmit={handleSubmit} />
      )}
      {!current && !loadError && <p className="subtitle">Loading…</p>}
    </div>
  )
}
