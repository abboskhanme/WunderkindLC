import { Suspense, useEffect, useMemo, useRef, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { ArrowLeft, KeyRound, LayoutGrid, LogOut, Moon, Sun, UserRound } from 'lucide-react'
import { useAuth } from '@/context/auth-context'
import { TeacherPortalContext, type TeacherPortalInfo } from '@/context/teacher-portal-context'
import { getTeacherProfile, getTeacherSchool } from '@/api/services/teacher'
import { Loader } from '@/components/ui/Loader'
import { TelegramLinkBanner } from '@/components/telegram/TelegramLinkBanner'
import { TeacherNotifications } from '@/components/teacher/TeacherNotifications'
import { usePerm } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import {
  TEACHER_NAV,
  TEACHER_SECTION_LABELS,
  TEACHER_TABS,
  dayMonthWeekday,
  getTeacherTheme,
  initials,
  navMatches,
  navVisible,
  setTeacherTheme,
  type TeacherNavItem,
} from '@/lib/teacherPortal'
import { todayIso } from '@/lib/month'

/**
 * O'qituvchi portali qobig'i — ADMIN TIZIMI bilan bir xil ko'rinish (edutizim uslubi: oq yuqori
 * panel, chapda oq yon menyu, kulrang `#f0f2f2` kontent foni, Nunito, brand ko'k).
 *
 * TO'LIQ MOSLASHUVCHAN — o'qituvchilar asosan TELEFONDAN ishlatadi:
 * - telefon/planshet (<1024px): yuqori panel + pastki 5 ta tab (Bosh · Guruhlar · Davomat · Jadval ·
 *   Menyu), qolgan bo'limlar "Menyu" sahifasida;
 * - kompyuter (≥1024px): yuqori panel + yon menyu (hamma bo'lim), pastki tablar yo'q.
 *
 * Bo'limlar ro'yxati YAGONA — `lib/teacherPortal.ts` → `TEACHER_NAV` (menyu, tablar va "Menyu"
 * sahifasi shundan quriladi).
 */
export function TeacherMobileLayout() {
  const { user } = useAuth()
  const { can } = usePerm()
  const location = useLocation()
  const mainRef = useRef<HTMLElement>(null)
  const [theme, setTheme] = useState<'light' | 'dark'>(getTeacherTheme)
  const [info, setInfo] = useState<TeacherPortalInfo>({ schoolName: '', logoUrl: '', telegramChannel: '', isSupport: false })

  useEffect(() => {
    const onChange = () => setTheme(getTeacherTheme())
    window.addEventListener('teacher-theme', onChange)
    return () => window.removeEventListener('teacher-theme', onChange)
  }, [])

  // Markaz nomi/logosi va Support bayrog'i — bir marta (fon o'qish: xato bo'lsa standart ko'rinish).
  useEffect(() => {
    getTeacherSchool()
      .then((s) => setInfo((i) => ({ ...i, schoolName: s.name, logoUrl: s.logoUrl ?? '', telegramChannel: s.telegramChannel })))
      .catch(() => {})
    getTeacherProfile()
      .then((p) => setInfo((i) => ({ ...i, isSupport: !!p?.isSupport })))
      .catch(() => {})
  }, [])

  // Sahifa almashganda kontent tepaga qaytadi (skroll konteyneri — main, oyna emas).
  useEffect(() => {
    mainRef.current?.scrollTo({ top: 0 })
  }, [location.pathname])

  const items = useMemo(
    () => TEACHER_NAV.filter((i) => navVisible(i, (p) => can(p, 'view'), info.isSupport)),
    // eslint-disable-next-line react-hooks/exhaustive-deps -- `can` har renderda yangi, ruxsatlar esa user bilan o'zgaradi
    [user?.permissions, info.isSupport],
  )

  return (
    <TeacherPortalContext.Provider value={info}>
      <div className={cn('teacher-app flex h-[100dvh] flex-col overflow-hidden bg-paper text-ink', theme === 'dark' && 'dark')}>
        <Topbar info={info} dark={theme === 'dark'} />

        <div className="flex min-h-0 flex-1">
          <SideNav items={items} />
          <main ref={mainRef} className="min-w-0 flex-1 overflow-y-auto overflow-x-hidden overscroll-contain">
            {/* Suspense QOBIQ ICHIDA — sahifa yuklanayotganda menyu/tablar joyida qoladi. */}
            <div className="mx-auto h-full w-full max-w-[1280px]">
              <TelegramLinkBanner variant="teacher" />
              <Suspense fallback={<Loader className="min-h-[240px]" />}>
                <Outlet />
              </Suspense>
            </div>
          </main>
        </div>

        <BottomTabs items={items} />
      </div>
    </TeacherPortalContext.Provider>
  )
}

/* ─────────────────────────── Yuqori panel ─────────────────────────── */

function Topbar({ info, dark }: { info: TeacherPortalInfo; dark: boolean }) {
  const navigate = useNavigate()
  const name = info.schoolName || 'Wunderkind'
  return (
    <header className="relative z-40 flex h-14 shrink-0 items-center border-b border-line bg-white pr-1.5 shadow-[0_1px_2px_rgba(0,0,0,0.04)] sm:pr-3">
      <div className="flex min-w-0 shrink items-center gap-2 pl-3 lg:w-[210px] lg:shrink-0 lg:pl-5">
        <Link to="/teacher" className="flex min-w-0 items-center gap-2" title={name}>
          {info.logoUrl ? (
            <img src={info.logoUrl} alt="" className="h-8 w-8 shrink-0 object-contain" />
          ) : (
            <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-md bg-brand-600 text-sm font-extrabold text-white">
              {name.slice(0, 1).toUpperCase()}
            </span>
          )}
          <span className="min-w-0">
            <span className="block truncate text-[16px] font-extrabold leading-tight text-brand-600 lg:text-[17px]">{name}</span>
            <span className="block text-[11px] font-semibold leading-tight text-mute lg:hidden">O'qituvchi</span>
          </span>
        </Link>
      </div>

      <div className="hidden min-w-0 flex-1 items-center gap-2.5 pl-[15px] lg:flex">
        <button
          type="button"
          // Tarix bo'lmasa (havola yangi tabda ochilgan) — ilovadan chiqib ketmasin, bosh sahifaga.
          onClick={() =>
            ((window.history.state as { idx?: number } | null)?.idx ?? 0) > 0 ? navigate(-1) : navigate('/teacher')
          }
          className="flex h-[30px] w-[30px] shrink-0 items-center justify-center rounded-lg border border-brand-600/20 bg-brand-600/10 text-brand-600 transition-colors hover:bg-brand-600/15"
          title="Orqaga"
          aria-label="Orqaga"
        >
          <ArrowLeft className="h-4 w-4" />
        </button>
        <span className="truncate text-[13.5px] font-semibold text-mute">{dayMonthWeekday(todayIso())}</span>
      </div>

      <div className="ml-auto flex shrink-0 items-center gap-1">
        <TeacherNotifications />
        <UserMenu dark={dark} />
      </div>
    </header>
  )
}

function UserMenu({ dark }: { dark: boolean }) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    const onDown = (e: MouseEvent) => ref.current && !ref.current.contains(e.target as Node) && setOpen(false)
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && setOpen(false)
    document.addEventListener('mousedown', onDown)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onDown)
      document.removeEventListener('keydown', onKey)
    }
  }, [open])

  const go = (to: string) => {
    setOpen(false)
    navigate(to)
  }

  const fullName = user?.fullName || "O'qituvchi"
  return (
    <div className="relative" ref={ref}>
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="ml-0.5 flex items-center gap-2 rounded-full lg:rounded-lg lg:py-1 lg:pl-1 lg:pr-2.5 lg:hover:bg-panel3"
        aria-haspopup="menu"
        aria-expanded={open}
        title={fullName}
      >
        <span className="flex h-[36px] w-[36px] items-center justify-center rounded-full border-2 border-line bg-brand-50 text-[13px] font-bold text-brand-600">
          {initials(fullName)}
        </span>
        <span className="hidden max-w-[160px] text-left lg:block">
          <span className="block truncate text-[13px] font-bold leading-tight text-ink">{fullName}</span>
          <span className="block text-[11px] leading-tight text-mute">O'qituvchi</span>
        </span>
      </button>

      {open && (
        <div role="menu" className="absolute right-0 top-full z-50 mt-2 w-60 overflow-hidden rounded-xl border border-line bg-white shadow-[0_12px_32px_rgba(16,24,40,0.12)]">
          <div className="border-b border-line-soft px-4 py-3">
            <p className="truncate text-sm font-bold text-ink">{fullName}</p>
            {user?.email && <p className="truncate text-xs text-faint">{user.email}</p>}
            <p className="mt-0.5 text-xs text-faint">O'qituvchi</p>
          </div>
          <MenuButton icon={UserRound} label="Profil" onClick={() => go('/teacher/profile')} />
          <MenuButton icon={KeyRound} label="Parolni almashtirish" onClick={() => go('/teacher/account')} />
          <MenuButton
            icon={dark ? Sun : Moon}
            label={dark ? 'Yorug\' rejim' : 'Tungi rejim'}
            onClick={() => setTeacherTheme(dark ? 'light' : 'dark')}
          />
          <button
            role="menuitem"
            type="button"
            onClick={() => {
              setOpen(false)
              logout()
              navigate('/login', { replace: true })
            }}
            className="flex w-full items-center gap-2.5 border-t border-line-soft px-4 py-2.5 text-sm font-semibold text-red-600 transition-colors hover:bg-red-50"
          >
            <LogOut className="h-4 w-4" />
            Chiqish
          </button>
        </div>
      )}
    </div>
  )
}

function MenuButton({ icon: Icon, label, onClick }: { icon: typeof UserRound; label: string; onClick: () => void }) {
  return (
    <button
      role="menuitem"
      type="button"
      onClick={onClick}
      className="flex w-full items-center gap-2.5 px-4 py-2.5 text-sm font-semibold text-ink transition-colors hover:bg-panel3 hover:text-brand-600"
    >
      <Icon className="h-4 w-4 text-faint" />
      {label}
    </button>
  )
}

/* ─────────────────────────── Yon menyu (≥1024px) ─────────────────────────── */

function SideNav({ items }: { items: TeacherNavItem[] }) {
  const sections = (['main', 'work', 'account'] as const)
    .map((s) => ({ key: s, items: items.filter((i) => i.section === s) }))
    .filter((s) => s.items.length > 0)

  return (
    <aside className="hidden w-[210px] shrink-0 flex-col overflow-y-auto border-r border-line bg-white px-[6px] pb-3 pt-2 lg:flex">
      {sections.map((s, idx) => (
        <div key={s.key} className={cn(idx > 0 && 'mt-2 border-t border-line-soft pt-2')}>
          <p className="px-3 pb-1 pt-1 text-[11px] font-bold uppercase tracking-wide text-faint">
            {TEACHER_SECTION_LABELS[s.key]}
          </p>
          <div className="space-y-[2px]">
            {s.items.map((item) => {
              const Icon = item.icon
              return (
                <NavLink
                  key={item.to}
                  to={item.to}
                  end={item.end}
                  className={({ isActive }) =>
                    cn(
                      'group relative flex h-[42px] items-center gap-2.5 rounded-[10px] pl-3 pr-2 text-[14px] font-semibold transition-colors',
                      isActive ? 'bg-paper2 text-brand-600' : 'text-ink hover:bg-paper2 hover:text-brand-600',
                    )
                  }
                >
                  {({ isActive }) => (
                    <>
                      <span
                        className={cn(
                          'absolute inset-y-1.5 left-0 w-[3px] rounded-full bg-brand-600 transition-opacity',
                          isActive ? 'opacity-100' : 'opacity-0 group-hover:opacity-100',
                        )}
                      />
                      <Icon className="h-5 w-5 shrink-0" />
                      <span className="truncate">{item.label}</span>
                    </>
                  )}
                </NavLink>
              )
            })}
          </div>
        </div>
      ))}
    </aside>
  )
}

/* ─────────────────────────── Pastki tablar (<1024px) ─────────────────────────── */

function BottomTabs({ items }: { items: TeacherNavItem[] }) {
  const { pathname } = useLocation()
  const tabs = TEACHER_TABS.map((to) => items.find((i) => i.to === to)).filter((i): i is TeacherNavItem => !!i)
  // Tabda yo'q bo'lim ochiq bo'lsa (Maosh, Testlar, ...) — "Menyu" tabi faol ko'rinadi.
  const activeTab = tabs.find((t) => navMatches(t, pathname))?.to ?? '/teacher/menu'

  const all = [
    ...tabs.map((t) => ({ to: t.to, label: t.short ?? t.label, icon: t.icon })),
    { to: '/teacher/menu', label: 'Menyu', icon: LayoutGrid },
  ]

  return (
    <nav className="shrink-0 border-t border-line bg-white pb-[env(safe-area-inset-bottom)] lg:hidden">
      <div className="mx-auto flex h-[60px] max-w-[640px]">
        {all.map((t) => {
          const Icon = t.icon
          const active = activeTab === t.to
          return (
            <Link key={t.to} to={t.to} className="relative flex flex-1 flex-col items-center justify-center gap-0.5">
              <span
                className={cn(
                  'flex h-7 w-14 items-center justify-center rounded-full transition-colors',
                  active ? 'bg-brand-50' : 'bg-transparent',
                )}
              >
                <Icon className={cn('h-[21px] w-[21px]', active ? 'text-brand-600' : 'text-faint')} strokeWidth={active ? 2.4 : 2} />
              </span>
              <span className={cn('text-[11px] tracking-tight', active ? 'font-bold text-brand-600' : 'font-semibold text-faint')}>
                {t.label}
              </span>
            </Link>
          )
        })}
      </div>
    </nav>
  )
}
