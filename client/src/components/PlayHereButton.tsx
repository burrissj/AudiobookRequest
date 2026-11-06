import { useState } from 'react'
import { api } from '../api/client'
import type { JellyfinLink } from '../api/types'
import { useAppConfig } from '../state/useAppConfig'
import { usePlayer } from '../state/usePlayer'

interface PlayHereButtonProps {
  title: string
  author: string
  className?: string
}

/** Finds the book in Jellyfin and plays it in the app's own player. */
export function PlayHereButton({ title, author, className }: PlayHereButtonProps) {
  const config = useAppConfig()
  const player = usePlayer()
  const [working, setWorking] = useState(false)
  const [message, setMessage] = useState<string | null>(null)

  if (!config?.jellyfinUrl) return null

  async function play() {
    setWorking(true)
    setMessage(null)
    try {
      const link = await api.get<JellyfinLink>(`/api/app/jellyfin-link?${new URLSearchParams({ title, author })}`)
      if (link.itemId) await player.play(link.itemId)
      else setMessage("Jellyfin hasn't picked this book up yet. Try again in a minute.")
    } catch {
      setMessage("Couldn't start playback.")
    } finally {
      setWorking(false)
    }
  }

  return (
    <>
      <button className={className ?? 'primary'} onClick={() => void play()} disabled={working}>
        {working ? 'Loading…' : 'Play here'}
      </button>
      {message && <span className="error">{message}</span>}
    </>
  )
}
