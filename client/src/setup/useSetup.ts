import { createContext, useContext } from 'react'
import type { ConnectionProblemInfo } from '../api/connection'

export interface SetupContextValue {
  setupCompleted: boolean
  loading: boolean
  /** Set when the server couldn't be asked at all. Never treated as "setup isn't done". */
  problem: ConnectionProblemInfo | null
  retry: () => void
  /** Call right after a successful setup POST, before navigating away, so SetupGate doesn't
   * bounce the next route back to /setup using its stale pre-save status. */
  markCompleted: () => void
}

export const SetupContext = createContext<SetupContextValue | undefined>(undefined)

export function useSetup() {
  const ctx = useContext(SetupContext)
  if (!ctx) throw new Error('useSetup must be used within a SetupProvider')
  return ctx
}
