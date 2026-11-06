import { createContext, useContext } from 'react'
import type { ConnectionProblemInfo } from '../api/connection'
import type { CurrentUser } from '../api/types'

export interface AuthContextValue {
  user: CurrentUser | null
  loading: boolean
  /** Set when "who am I?" couldn't be answered because the server was unreachable (as opposed to "not signed in"). */
  problem: ConnectionProblemInfo | null
  retry: () => void
  login: (username: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | undefined>(undefined)

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
