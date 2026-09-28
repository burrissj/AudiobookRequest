import { useAuth } from '../auth/AuthContext'

export function Home() {
  const { user, logout } = useAuth()

  return (
    <div className="page home-page">
      <header className="home-header">
        <h1>Audiobook Request</h1>
        <button onClick={() => logout()}>Sign out</button>
      </header>
      <p>
        Signed in as <strong>{user?.username}</strong> ({user?.role})
      </p>
      <p className="subtitle">Book search and requests land here in Phase 2.</p>
    </div>
  )
}
