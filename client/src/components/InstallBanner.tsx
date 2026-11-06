import { useEffect, useState } from 'react'

const DISMISSED_KEY = 'abr.installDismissed'

interface BeforeInstallPromptEvent extends Event {
  prompt: () => Promise<void>
  userChoice: Promise<{ outcome: 'accepted' | 'dismissed' }>
}

function isStandalone(): boolean {
  return window.matchMedia('(display-mode: standalone)').matches || (navigator as Navigator & { standalone?: boolean }).standalone === true
}

function isIos(): boolean {
  return /iPad|iPhone|iPod/.test(navigator.userAgent) || (navigator.platform === 'MacIntel' && navigator.maxTouchPoints > 1)
}

function wasDismissed(): boolean {
  try {
    return localStorage.getItem(DISMISSED_KEY) !== null
  } catch {
    return false
  }
}

/**
 * Offers to put the app on the home screen: a real Install button where the browser supports it
 * (Chrome/Edge/Android), and the two-tap instructions on iPhone and iPad, which have no install prompt.
 * Never shown once installed, and a dismissal is remembered.
 */
export function InstallBanner() {
  const [promptEvent, setPromptEvent] = useState<BeforeInstallPromptEvent | null>(null)
  const [hidden, setHidden] = useState(() => wasDismissed() || isStandalone())

  useEffect(() => {
    function onPrompt(e: Event) {
      e.preventDefault()
      setPromptEvent(e as BeforeInstallPromptEvent)
    }
    window.addEventListener('beforeinstallprompt', onPrompt)
    return () => window.removeEventListener('beforeinstallprompt', onPrompt)
  }, [])

  function dismiss() {
    try {
      localStorage.setItem(DISMISSED_KEY, String(Date.now()))
    } catch {
      // ignore: it just reappears next visit
    }
    setHidden(true)
  }

  async function install() {
    if (!promptEvent) return
    await promptEvent.prompt()
    const choice = await promptEvent.userChoice
    if (choice.outcome === 'accepted') setHidden(true)
    setPromptEvent(null)
  }

  const ios = isIos()
  if (hidden || (!promptEvent && !ios)) {
    return null
  }

  return (
    <div className="install-banner">
      {promptEvent ? (
        <p>Install Audiobook Request as an app for quick access from your home screen.</p>
      ) : (
        <p>
          Add this to your home screen: tap the <strong>Share</strong> button in Safari, then <strong>Add to Home Screen</strong>.
        </p>
      )}
      <div className="request-action-group">
        {promptEvent && (
          <button className="primary" onClick={install}>
            Install
          </button>
        )}
        <button onClick={dismiss}>Not now</button>
      </div>
    </div>
  )
}
