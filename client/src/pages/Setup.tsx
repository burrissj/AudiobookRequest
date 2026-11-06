import { useNavigate } from 'react-router-dom'
import { api } from '../api/client'
import { SettingsForm } from '../components/SettingsForm'
import { useSetup } from '../setup/useSetup'
import type { SetupRequest } from '../api/types'

const initialForm: SetupRequest = {
  chaptarrUrl: '',
  chaptarrApiKey: '',
  chaptarrRootFolderPath: '',
  jellyfinUrl: '',
  jellyfinApiKey: '',
  stagingPath: '',
  libraryPath: '',
  scanIntervalMinutes: 15,
  originalRetentionDays: 90,
  namingPattern: '{Author}/{Title}/{Title}',
  chaptarrFilePathPrefix: null,
  localFilePathPrefix: null,
  enableRenaming: true,
  enableConversion: true,
  enableMoving: true,
  requireApproval: false,
  maxRequestsPerUserPerWeek: 0,
  minFreeDiskGb: 5,
  wishlistRetryHours: 12,
  notifyOnReady: true,
  notifyOnFailed: true,
  notifyOnPendingApproval: true,
  notifyDiscordWebhookUrl: null,
  notifyTelegramBotToken: null,
  notifyTelegramChatId: null,
  notifyNtfyUrl: null,
  notifyWebhookUrl: null,
  smtpHost: null,
  smtpPort: 587,
  smtpUseSsl: true,
  smtpUsername: null,
  smtpPassword: null,
  smtpFrom: null,
  smtpTo: null,
  notifyOnNewRelease: true,
  autoFixImports: false,
  backupRetentionCount: 14,
  jellyfinPublicUrl: null,
}

export function Setup() {
  const navigate = useNavigate()
  const { markCompleted } = useSetup()

  async function handleSubmit(form: SetupRequest) {
    await api.post('/api/setup', form)
    // Must happen before navigate(): SetupGate guards /login using this same status, and its default
    // is "not completed" until this flips -- otherwise the very next render bounces back to /setup.
    markCompleted()
    navigate('/login', { replace: true })
  }

  return (
    <div className="page setup-page">
      <h1>Welcome — let's get set up</h1>
      <p className="subtitle">Connect Chaptarr and Jellyfin, then choose where files live.</p>
      <SettingsForm initialValues={initialForm} submitLabel="Finish setup" submittingLabel="Saving…" onSubmit={handleSubmit} />
    </div>
  )
}
