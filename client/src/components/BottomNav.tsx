import { NavLink } from 'react-router-dom'
import { useAuth } from '../auth/useAuth'
import { useRequests } from '../state/useRequests'
import { HeadphonesIcon } from './PlayerIcons'

function SearchIcon() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <circle cx="11" cy="11" r="7" />
      <line x1="21" y1="21" x2="16.65" y2="16.65" />
    </svg>
  )
}

function ListIcon() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <line x1="9" y1="6" x2="20" y2="6" />
      <line x1="9" y1="12" x2="20" y2="12" />
      <line x1="9" y1="18" x2="20" y2="18" />
      <circle cx="4.5" cy="6" r="1.4" fill="currentColor" stroke="none" />
      <circle cx="4.5" cy="12" r="1.4" fill="currentColor" stroke="none" />
      <circle cx="4.5" cy="18" r="1.4" fill="currentColor" stroke="none" />
    </svg>
  )
}

function SparkleIcon() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" fill="currentColor">
      <path d="M12 2l1.8 5.6L19.5 9.4l-5.7 1.8L12 17l-1.8-5.8L4.5 9.4l5.7-1.8L12 2z" />
      <path d="M19 15l.8 2.4L22.2 18.2l-2.4.8L19 21.4l-.8-2.4-2.4-.8 2.4-.8L19 15z" />
    </svg>
  )
}

function SlidersIcon() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
      <line x1="5" y1="4" x2="5" y2="20" />
      <circle cx="5" cy="9" r="2" fill="currentColor" stroke="none" />
      <line x1="12" y1="4" x2="12" y2="20" />
      <circle cx="12" cy="15" r="2" fill="currentColor" stroke="none" />
      <line x1="19" y1="4" x2="19" y2="20" />
      <circle cx="19" cy="7" r="2" fill="currentColor" stroke="none" />
    </svg>
  )
}

function ShieldIcon() {
  return (
    <svg viewBox="0 0 24 24" width="22" height="22" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 3l8 3v6c0 4.5-3.2 8.3-8 9-4.8-.7-8-4.5-8-9V6l8-3z" />
      <path d="M9 12l2 2 4-4" />
    </svg>
  )
}

const BASE_TABS = [
  { to: '/', label: 'Browse', Icon: SearchIcon, end: true },
  { to: '/requests', label: 'Requests', Icon: ListIcon, end: false },
  { to: '/recommended', label: 'For You', Icon: SparkleIcon, end: false },
  { to: '/listen', label: 'Listen', Icon: HeadphonesIcon, end: false },
]

const ADMIN_TAB = { to: '/admin', label: 'Admin', Icon: ShieldIcon, end: false }
const SETTINGS_TAB = { to: '/settings', label: 'Settings', Icon: SlidersIcon, end: false }

export function BottomNav() {
  const { user } = useAuth()
  const { requests } = useRequests()
  const isAdmin = user?.role === 'Admin'
  const tabs = isAdmin ? [...BASE_TABS, ADMIN_TAB, SETTINGS_TAB] : BASE_TABS
  const pendingApprovals = isAdmin ? requests.filter((r) => r.status === 'PendingApproval').length : 0

  return (
    <nav className="bottom-nav">
      {tabs.map(({ to, label, Icon, end }) => (
        <NavLink key={to} to={to} end={end} className={({ isActive }) => `bottom-nav-item${isActive ? ' active' : ''}`}>
          <Icon />
          <span>{label}</span>
          {to === '/admin' && pendingApprovals > 0 && <span className="nav-count">{pendingApprovals}</span>}
        </NavLink>
      ))}
    </nav>
  )
}
