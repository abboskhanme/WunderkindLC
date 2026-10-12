import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ChevronRight, KeyRound, LogOut, Moon, Sun } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { useTeacherPortal } from '@/context/teacher-portal-context'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import {
  TEACHER_NAV,
  TEACHER_SECTION_LABELS,
  getTeacherTheme,
  initials,
  navVisible,
  setTeacherTheme,
} from '@/lib/teacherPortal'
import { TPage } from '@/components/teacher/TeacherUi'

/**
 * MENYU (telefon) — pastki tablarga sig'magan barcha bo'limlar bitta ro'yxatda
 * (`TEACHER_NAV` dan: yon menyu bilan AYNAN bir xil tarkib) + tungi rejim va chiqish.
 * Kompyuterda yon menyu bor, lekin sahifa u yerda ham to'g'ri ishlaydi.
 */
export function TeacherMenuPage() {
  const { user, logout } = useAuth()
  const { can } = usePerm()
  const portal = useTeacherPortal()
  const navigate = useNavigate()
  const [dark, setDark] = useState(() => getTeacherTheme() === 'dark')

  useEffect(() => {
    const onChange = () => setDark(getTeacherTheme() === 'dark')
    window.addEventListener('teacher-theme', onChange)
    return () => window.removeEventListener('teacher-theme', onChange)
  }, [])

  const items = TEACHER_NAV.filter((i) => i.to !== '/teacher' && navVisible(i, (p) => can(p, 'view'), portal.isSupport))
  const sections = (['main', 'work', 'account'] as const)
    .map((s) => ({ key: s, items: items.filter((i) => i.section === s) }))
    .filter((s) => s.items.length > 0)
  const fullName = user?.fullName || "O'qituvchi"

  return (
    <TPage title="Menyu" sub="Barcha bo'limlar">
      <Link
        to="/teacher/profile"
        className="mb-3 flex items-center gap-3 rounded-xl border border-line bg-white p-3.5 shadow-[var(--shadow-card)] transition-colors hover:border-brand-200"
      >
        <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-brand-600 text-[16px] font-bold text-white">
          {initials(fullName)}
        </span>
        <span className="min-w-0 flex-1">
          <span className="block truncate text-[15px] font-bold text-ink">{fullName}</span>
          <span className="block truncate text-[12.5px] text-mute">{portal.schoolName ? `${portal.schoolName} · ` : ''}O'qituvchi</span>
        </span>
        <ChevronRight className="h-5 w-5 shrink-0 text-faint" />
      </Link>

      <div className="space-y-3">
        {sections.map((s) => (
          <div key={s.key}>
            <p className="mb-1.5 px-1 text-[11.5px] font-bold uppercase tracking-wide text-faint">{TEACHER_SECTION_LABELS[s.key]}</p>
            <ul className="divide-y divide-line-soft overflow-hidden rounded-xl border border-line bg-white shadow-[var(--shadow-card)]">
              {s.items.map((i) => {
                const Icon = i.icon
                return (
                  <li key={i.to}>
                    <Link to={i.to} className="flex items-center gap-3 px-3.5 py-3 transition-colors hover:bg-panel2 active:bg-panel3">
                      <span className="relative flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-brand-50 text-brand-600">
                        <Icon className="h-5 w-5" />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="block text-[14.5px] font-bold text-ink">{i.label}</span>
                        <span className="block truncate text-[12px] text-mute">{i.hint}</span>
                      </span>
                      <ChevronRight className="h-5 w-5 shrink-0 text-faint" />
                    </Link>
                  </li>
                )
              })}
            </ul>
          </div>
        ))}

        <div>
          <p className="mb-1.5 px-1 text-[11.5px] font-bold uppercase tracking-wide text-faint">Sozlamalar</p>
          <ul className="divide-y divide-line-soft overflow-hidden rounded-xl border border-line bg-white shadow-[var(--shadow-card)]">
            <li>
              <button
                type="button"
                onClick={() => setTeacherTheme(dark ? 'light' : 'dark')}
                className="flex w-full items-center gap-3 px-3.5 py-3 text-left transition-colors hover:bg-panel2"
              >
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-violet-50 text-violet-600">
                  {dark ? <Sun className="h-5 w-5" /> : <Moon className="h-5 w-5" />}
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block text-[14.5px] font-bold text-ink">Tungi rejim</span>
                  <span className="block text-[12px] text-mute">{dark ? 'Yoqilgan' : "O'chirilgan"}</span>
                </span>
                <span className={cn('relative h-6 w-11 shrink-0 rounded-full transition-colors', dark ? 'bg-brand-600' : 'bg-slate-200')}>
                  <span
                    className={cn(
                      'absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-transform',
                      dark ? 'translate-x-[22px]' : 'translate-x-0.5',
                    )}
                  />
                </span>
              </button>
            </li>
            <li>
              <Link to="/teacher/account" className="flex items-center gap-3 px-3.5 py-3 transition-colors hover:bg-panel2">
                <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-slate-100 text-slate-600">
                  <KeyRound className="h-5 w-5" />
                </span>
                <span className="min-w-0 flex-1">
                  <span className="block text-[14.5px] font-bold text-ink">Parolni almashtirish</span>
                  <span className="block text-[12px] text-mute">Hisob xavfsizligi</span>
                </span>
                <ChevronRight className="h-5 w-5 shrink-0 text-faint" />
              </Link>
            </li>
          </ul>
        </div>

        <button
          type="button"
          onClick={() => {
            logout()
            navigate('/login', { replace: true })
          }}
          className="flex h-12 w-full items-center justify-center gap-2 rounded-xl border border-red-200 bg-white text-[15px] font-bold text-red-600 transition-colors hover:bg-red-50"
        >
          <LogOut className="h-5 w-5" /> Chiqish
        </button>
      </div>
    </TPage>
  )
}
