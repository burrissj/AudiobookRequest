import { useState } from 'react'
import { api } from '../api/client'
import type { JellyfinLink } from '../api/types'
import { useAppConfig } from '../state/useAppConfig'

interface ListenButtonProps {
  title: string
  author: string
  label?: string
  className?: string
}

/**
 * Opens a book in Jellyfin. The page is opened immediately and pointed at the exact book once it is found, because
 * a tab opened only after a network call is treated as a pop-up and blocked by phone browsers.
 */
export function ListenButton({ title, author, label = 'Listen in Jellyfin', className }: ListenButtonProps) {
  const config = useAppConfig()
  const [working, setWorking] = useState(false)
  const [failed, setFailed] = useState(false)

  if (!config?.jellyfinUrl) return null

  async function open() {
    setWorking(true)
    setFailed(false)
    const tab = window.open('', '_blank')
    try {
      const params = new URLSearchParams({ title, author })
      const link = await api.get<JellyfinLink>(`/api/app/jellyfin-link?${params}`)
      if (tab) {
        tab.opener = null
        tab.location.href = link.url
      } else {
        window.location.href = link.url
      }
    } catch {
      // Fall back to Jellyfin's home page rather than leaving a blank tab.
      if (tab) tab.location.href = config!.jellyfinUrl!
      else setFailed(true)
    } finally {
      setWorking(false)
    }
  }

  return (
    <>
      <button className={className ?? 'retry-toggle'} onClick={open} disabled={working}>
        {working ? 'Opening…' : label}
      </button>
      {failed && <span className="error">Couldn't open Jellyfin.</span>}
    </>
  )
}

/** "Get the Jellyfin app: iPhone · Android" -- the official mobile app's store pages. */
export function GetAppLinks() {
  const config = useAppConfig()
  if (!config) return null

  return (
    <p className="get-app-links">
      Get the Jellyfin app:{' '}
      <a href={config.iosAppUrl} target="_blank" rel="noreferrer">
        iPhone
      </a>
      {' · '}
      <a href={config.androidAppUrl} target="_blank" rel="noreferrer">
        Android
      </a>
    </p>
  )
}
