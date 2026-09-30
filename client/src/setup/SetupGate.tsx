import { useEffect, useState, type ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { api } from '../api/client'
import type { SetupStatus } from '../api/types'

export function SetupGate({ children }: { children: ReactNode }) {
  const location = useLocation()
  const [status, setStatus] = useState<SetupStatus | null>(null)

  useEffect(() => {
    api.get<SetupStatus>('/api/setup/status').then(setStatus)
  }, [])

  if (status === null) {
    return <div className="page">Loading…</div>
  }

  if (!status.setupCompleted && location.pathname !== '/setup') {
    return <Navigate to="/setup" replace />
  }

  // Once completed, /setup is still reachable by direct navigation (e.g. an admin reconfiguring
  // which Chaptarr instance to use) -- the server enforces that only an authenticated admin can
  // actually resubmit it at that point. We just stop force-redirecting new visitors there.
  return children
}
