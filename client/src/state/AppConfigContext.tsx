import { useEffect, useState, type ReactNode } from 'react'
import { api } from '../api/client'
import type { AppConfig } from '../api/types'
import { AppConfigContext } from './useAppConfig'

/** Values every screen may need -- where Jellyfin is, and the mobile app links -- fetched once after sign-in. */
export function AppConfigProvider({ children }: { children: ReactNode }) {
  const [config, setConfig] = useState<AppConfig | null>(null)

  useEffect(() => {
    api.get<AppConfig>('/api/app/config').then(setConfig).catch(console.error)
  }, [])

  return <AppConfigContext.Provider value={config}>{children}</AppConfigContext.Provider>
}
