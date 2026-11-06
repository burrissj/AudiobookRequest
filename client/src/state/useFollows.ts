import { createContext, useContext } from 'react'
import type { FollowDto } from '../api/types'

export interface FollowTarget {
  kind: 'Author' | 'Series'
  name: string
  seriesAsin?: string | null
  seriesAuthor?: string | null
}

export interface FollowsContextValue {
  follows: FollowDto[]
  loaded: boolean
  error: string | null
  findFollow: (kind: 'Author' | 'Series', name: string) => FollowDto | undefined
  follow: (target: FollowTarget, autoRequest: boolean) => Promise<void>
  unfollow: (id: number) => Promise<void>
  setAutoRequest: (id: number, autoRequest: boolean) => Promise<void>
}

export const FollowsContext = createContext<FollowsContextValue | undefined>(undefined)

export function useFollows() {
  const ctx = useContext(FollowsContext)
  if (!ctx) throw new Error('useFollows must be used within a FollowsProvider')
  return ctx
}
