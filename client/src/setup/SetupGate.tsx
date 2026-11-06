import { useState, type ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { ConnectionProblem } from '../components/ConnectionProblem'
import { useSetup } from './useSetup'

export function SetupGate({ children }: { children: ReactNode }) {
  const location = useLocation()
  const { setupCompleted, loading, problem, retry } = useSetup()
  const [retrying, setRetrying] = useState(false)

  if (loading) {
    return <div className="page">Loading…</div>
  }

  // The server couldn't be asked. That says nothing about whether setup is done, so don't guess: say so and keep trying.
  if (problem) {
    const onRetry = () => {
      setRetrying(true)
      retry()
      window.setTimeout(() => setRetrying(false), 800)
    }
    return <ConnectionProblem problem={problem} retrying={retrying} onRetry={onRetry} />
  }

  if (!setupCompleted && location.pathname !== '/setup') {
    return <Navigate to="/setup" replace />
  }

  // Once completed, /setup is still reachable by direct navigation (e.g. an admin reconfiguring
  // which Chaptarr instance to use) -- the server enforces that only an authenticated admin can
  // actually resubmit it at that point. We just stop force-redirecting new visitors there.
  return children
}
