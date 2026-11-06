import { useCallback, useEffect, useState, type ReactNode } from 'react'
import { api, ApiError } from '../api/client'
import { describeConnectionProblem, type ConnectionProblemInfo } from '../api/connection'
import type { CurrentUser } from '../api/types'
import { AuthContext } from './useAuth'

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState(true)
  const [problem, setProblem] = useState<ConnectionProblemInfo | null>(null)

  const load = useCallback(() => {
    api
      .get<CurrentUser>('/api/auth/me')
      .then((u) => {
        setUser(u)
        setProblem(null)
      })
      .catch((err) => {
        if (err instanceof ApiError && err.status === 401) {
          setUser(null)
          setProblem(null)
        } else {
          // Unreachable is not the same as signed out: don't send someone to the login page over a bad connection.
          setProblem(describeConnectionProblem(err))
        }
      })
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const login = useCallback(async (username: string, password: string) => {
    const loggedInUser = await api.post<CurrentUser>('/api/auth/login', { username, password })
    setUser(loggedInUser)
  }, [])

  const logout = useCallback(async () => {
    await api.post('/api/auth/logout')
    setUser(null)
  }, [])

  return <AuthContext.Provider value={{ user, loading, problem, retry: load, login, logout }}>{children}</AuthContext.Provider>
}
