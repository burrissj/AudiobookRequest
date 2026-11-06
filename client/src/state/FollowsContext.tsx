import { useEffect, useState, type ReactNode } from 'react'
import { api, ApiError } from '../api/client'
import type { FollowDto } from '../api/types'
import { FollowsContext, type FollowTarget } from './useFollows'

/** The signed-in user's followed authors and series, shared by every screen that offers a Follow button. */
export function FollowsProvider({ children }: { children: ReactNode }) {
  const [follows, setFollows] = useState<FollowDto[]>([])
  const [loaded, setLoaded] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    api
      .get<FollowDto[]>('/api/follows')
      .then(setFollows)
      .catch(console.error)
      .finally(() => setLoaded(true))
  }, [])

  const findFollow = (kind: 'Author' | 'Series', name: string) =>
    follows.find((f) => f.kind === kind && f.name.toLowerCase() === name.trim().toLowerCase())

  async function follow(target: FollowTarget, autoRequest: boolean) {
    setError(null)
    try {
      const created = await api.post<FollowDto>('/api/follows', { ...target, autoRequest })
      setFollows((prev) => (prev.some((f) => f.id === created.id) ? prev.map((f) => (f.id === created.id ? created : f)) : [...prev, created]))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not follow')
    }
  }

  async function unfollow(id: number) {
    setError(null)
    try {
      await api.delete(`/api/follows/${id}`)
      setFollows((prev) => prev.filter((f) => f.id !== id))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not unfollow')
    }
  }

  async function setAutoRequest(id: number, autoRequest: boolean) {
    setError(null)
    try {
      const updated = await api.put<FollowDto>(`/api/follows/${id}`, { autoRequest })
      setFollows((prev) => prev.map((f) => (f.id === id ? updated : f)))
    } catch (err) {
      setError(err instanceof ApiError ? err.message : 'Could not update')
    }
  }

  return (
    <FollowsContext.Provider value={{ follows, loaded, error, findFollow, follow, unfollow, setAutoRequest }}>
      {children}
    </FollowsContext.Provider>
  )
}
