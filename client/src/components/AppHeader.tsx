import { Link } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { useAppConfig } from '../state/useAppConfig'

export function AppHeader({ title }: { title: string }) {
  const { user, logout } = useAuth()
  const config = useAppConfig()
  return (
    <header className="app-header">
      <div>
        <h1>{title}</h1>
        {user && (
          <p className="subtitle">
            Signed in as <strong>{user.username}</strong>
            {' · '}
            <Link className="header-link" to="/account">
              Account
            </Link>
            {config?.jellyfinUrl && (
              <>
                {' · '}
                <a className="header-link" href={config.jellyfinUrl} target="_blank" rel="noreferrer">
                  Open Jellyfin ↗
                </a>
              </>
            )}
          </p>
        )}
      </div>
      <button onClick={() => logout()}>Sign out</button>
    </header>
  )
}
