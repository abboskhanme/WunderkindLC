import { Suspense, useEffect, useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { Loader } from '@/components/ui/Loader'
import { Icon } from '@/pages/student/lib'

interface Tab {
  to: string
  label: string
  icon: string
  end?: boolean
}

const TABS: Tab[] = [
  { to: '/student', label: 'Dashboard', icon: 'home', end: true },
  { to: '/student/progress', label: 'Progress', icon: 'chart' },
  { to: '/student/chat', label: 'Chat', icon: 'chat' },
  { to: '/student/profile', label: 'Profil', icon: 'user' },
]

/** Joriy tema ('light' | 'dark') — localStorage'dan. */
export function getStudentTheme(): 'light' | 'dark' {
  return localStorage.getItem('student_theme') === 'dark' ? 'dark' : 'light'
}
export function setStudentTheme(t: 'light' | 'dark') {
  localStorage.setItem('student_theme', t)
  window.dispatchEvent(new Event('student:theme'))
}

/**
 * O'quvchi portali — MOBIL ilova qobig'i (telefon/web). Dizayn: student.html (blue UI-kit).
 * 480px markazlashtirilgan ustun, pastki 4-tab navigatsiya, light/dark tema.
 */
export function StudentMobileLayout() {
  const [theme, setTheme] = useState<'light' | 'dark'>(getStudentTheme)

  useEffect(() => {
    const onChange = () => setTheme(getStudentTheme())
    window.addEventListener('student:theme', onChange)
    return () => window.removeEventListener('student:theme', onChange)
  }, [])

  return (
    <div
      className="student-app"
      data-theme={theme}
      style={{ display: 'flex', flexDirection: 'column', height: '100dvh', overflow: 'hidden', width: '100%' }}
    >
      {/* Desktop yon-menyu (chap) — faqat keng ekranda ko'rinadi (CSS @media >=1024px).
          Telefon/WebView'da display:none (dizayn o'zgarmaydi). */}
      <aside className="st-side">
        <div className="st-brand">
          <Icon name="school" size={24} fill />
          <span>O'quvchi</span>
        </div>
        {TABS.map((tab) => (
          <NavLink
            key={tab.to}
            to={tab.to}
            end={tab.end}
            className={({ isActive }) => 'st-nav-item' + (isActive ? ' on' : '')}
          >
            {({ isActive }) => (
              <>
                <Icon name={tab.icon} size={20} fill={isActive} />
                <span>{tab.label}</span>
              </>
            )}
          </NavLink>
        ))}
      </aside>

      {/* Content area — flex:1, min-height:0. Har sahifa .screen class bilan flex:1 oladi.
          Tashqi overflow:hidden — sahifalar o'z ichki .scroll orqali scroll qiladi. */}
      <div className="st-main" style={{ flex: 1, minHeight: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        {/* Suspense LAYOUT ICHIDA — ekran chunk'i yuklanayotganda qobiq (tabbar/yon-menyu) qoladi. */}
        <Suspense fallback={<Loader className="flex-1" />}>
          <Outlet />
        </Suspense>
      </div>

      {/* Bottom navigation — telefon/WebView'da pastda; desktop'da yashiriladi (yon-menyu o'rniga). */}
      <nav className="tabbar">
        {TABS.map((tab) => (
          <NavLink
            key={tab.to}
            to={tab.to}
            end={tab.end}
            className={({ isActive }) => 'tab' + (isActive ? ' on' : '')}
            style={{ textDecoration: 'none' }}
          >
            {({ isActive }) => (
              <>
                <Icon name={tab.icon} size={24} fill={isActive} />
                <span>{tab.label}</span>
              </>
            )}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
