import { Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider } from './auth/AuthContext'
import { RequireAuth } from './auth/RequireAuth'
import { Home } from './pages/Home'
import { Login } from './pages/Login'
import { Setup } from './pages/Setup'
import { SetupGate } from './setup/SetupGate'

function App() {
  return (
    <AuthProvider>
      <SetupGate>
        <Routes>
          <Route path="/setup" element={<Setup />} />
          <Route path="/login" element={<Login />} />
          <Route
            path="/"
            element={
              <RequireAuth>
                <Home />
              </RequireAuth>
            }
          />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </SetupGate>
    </AuthProvider>
  )
}

export default App
