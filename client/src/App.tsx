import { Navigate, Outlet, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { useKeyboardOpen } from './hooks/useKeyboardOpen'
import { BottomNav } from './components/BottomNav'
import { PlayerSheet } from './components/PlayerSheet'
import { WhatsNew } from './components/WhatsNew'
import { Account } from './pages/Account'
import { Admin } from './pages/Admin'
import { BookDetail } from './pages/BookDetail'
import { Browse } from './pages/Browse'
import { Following } from './pages/Following'
import { Category } from './pages/Category'
import { Listen } from './pages/Listen'
import { Login } from './pages/Login'
import { Recommended } from './pages/Recommended'
import { Requests } from './pages/Requests'
import { Settings } from './pages/Settings'
import { Stats } from './pages/Stats'
import { Setup } from './pages/Setup'
import { SetupGate } from './setup/SetupGate'
import { SetupProvider } from './setup/SetupContext'
import { AppConfigProvider } from './state/AppConfigContext'
import { FollowsProvider } from './state/FollowsContext'
import { PlayerProvider } from './state/PlayerContext'
import { RequestsProvider } from './state/RequestsContext'

function AuthenticatedShell() {
  return (
    <RequestsProvider>
      <FollowsProvider>
        <AppConfigProvider>
          <PlayerProvider>
            <Outlet />
            <PlayerSheet />
            <WhatsNew />
            <BottomNav />
          </PlayerProvider>
        </AppConfigProvider>
      </FollowsProvider>
    </RequestsProvider>
  )
}

function App() {
  useKeyboardOpen()
  return (
    <AuthProvider>
      <SetupProvider>
        <SetupGate>
          <Routes>
            <Route path="/setup" element={<Setup />} />
            <Route path="/login" element={<Login />} />
            <Route
              element={
                <RequireAuth>
                  <AuthenticatedShell />
                </RequireAuth>
              }
            >
              <Route path="/" element={<Browse />} />
              <Route path="/requests" element={<Requests />} />
              <Route path="/recommended" element={<Recommended />} />
              <Route path="/listen" element={<Listen key="listening" mode="listen" />} />
              <Route path="/listen/library" element={<Listen key="library" mode="library" />} />
              <Route path="/stats" element={<Stats />} />
              <Route path="/category/:id" element={<Category />} />
              <Route path="/following" element={<Following />} />
              <Route path="/account" element={<Account />} />
              <Route path="/admin" element={<Admin />} />
              <Route path="/settings" element={<Settings />} />
              <Route path="/book" element={<BookDetail />} />
            </Route>
            <Route path="*" element={<Navigate to="/" replace />} />
          </Routes>
        </SetupGate>
      </SetupProvider>
    </AuthProvider>
  )
}

export default App
