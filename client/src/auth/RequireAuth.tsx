import { useState, type ReactNode } from 'react'
import { Navigate } from 'react-router-dom'
import { ConnectionProblem } from '../components/ConnectionProblem'
import { useAuth } from './useAuth'

export function RequireAuth({ children }: { children: ReactNode }) {
  const { user, loading, problem, retry } = useAuth()
  const [retrying, setRetrying] = useState(false)

  if (loading) {
    return <div className="page">Loading…</div>
  }

  if (problem && !user) {
    const onRetry = () => {
      setRetrying(true)
      retry()
      window.setTimeout(() => setRetrying(false), 800)
    }
    return <ConnectionProblem problem={problem} retrying={retrying} onRetry={onRetry} />
  }

  if (!user) {
    return <Navigate to="/login" replace />
  }

  return children
}
