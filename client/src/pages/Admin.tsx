import { useState } from 'react'
import { useAuth } from '../auth/useAuth'
import { AppHeader } from '../components/AppHeader'
import { AdminHealth } from '../components/admin/AdminHealth'
import { AdminImport } from '../components/admin/AdminImport'
import { AdminLogs } from '../components/admin/AdminLogs'
import { AdminRequests } from '../components/admin/AdminRequests'
import { useRequests } from '../state/useRequests'

type Tab = 'requests' | 'health' | 'import' | 'logs'

export function Admin() {
  const { user } = useAuth()
  const { requests } = useRequests()
  const [tab, setTab] = useState<Tab>('requests')

  if (user?.role !== 'Admin') {
    return (
      <div className="page">
        <AppHeader title="Admin" />
        <p className="subtitle">Only an admin can use these tools.</p>
      </div>
    )
  }

  const pending = requests.filter((r) => r.status === 'PendingApproval').length

  return (
    <div className="page">
      <AppHeader title="Admin" />
      <div className="segmented segmented-wide">
        <button className={tab === 'requests' ? 'active' : ''} onClick={() => setTab('requests')}>
          Requests{pending > 0 ? ` (${pending})` : ''}
        </button>
        <button className={tab === 'health' ? 'active' : ''} onClick={() => setTab('health')}>
          Health
        </button>
        <button className={tab === 'import' ? 'active' : ''} onClick={() => setTab('import')}>
          Import
        </button>
        <button className={tab === 'logs' ? 'active' : ''} onClick={() => setTab('logs')}>
          Logs
        </button>
      </div>
      {tab === 'requests' && <AdminRequests />}
      {tab === 'health' && <AdminHealth />}
      {tab === 'import' && <AdminImport />}
      {tab === 'logs' && <AdminLogs />}
    </div>
  )
}
