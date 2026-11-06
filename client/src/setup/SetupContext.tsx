import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import { describeConnectionProblem, type ConnectionProblemInfo } from '../api/connection'
import type { SetupStatus } from '../api/types'
import { SetupContext } from './useSetup'

export function SetupProvider({ children }: { children: ReactNode }) {
  const [setupCompleted, setSetupCompleted] = useState(false)
  const [loading, setLoading] = useState(true)
  const [problem, setProblem] = useState<ConnectionProblemInfo | null>(null)

  const load = useCallback(() => {
    api
      .get<SetupStatus>('/api/setup/status')
      .then((s) => {
        setSetupCompleted(s.setupCompleted)
        setProblem(null)
      })
      .catch((err) => setProblem(describeConnectionProblem(err)))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  return (
    <SetupContext.Provider value={{ setupCompleted, loading, problem, retry: load, markCompleted: () => setSetupCompleted(true) }}>
      {children}
    </SetupContext.Provider>
  )
}
