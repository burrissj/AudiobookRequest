import { createContext, useContext } from 'react'
import type { AppConfig } from '../api/types'

export const AppConfigContext = createContext<AppConfig | null>(null)

/** Null until loaded (or if the call failed); callers just hide what depends on it. */
export function useAppConfig() {
  return useContext(AppConfigContext)
}
