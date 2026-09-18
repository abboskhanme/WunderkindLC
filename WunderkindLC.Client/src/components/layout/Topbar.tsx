import { useEffect, useRef, useState } from 'react'
import {
  IconArrowBackUp,
  IconBuilding,
  IconCirclePlus,
  IconLogout,
  IconMenu2,
  IconSearch,
  IconSettings,
} from '@tabler/icons-react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import { roleLabels, homeFor } from '@/config/navigation'
import { getSchoolName } from '@/api/services/settings'
import { can } from '@/lib/permissions'
import { cn } from '@/lib/utils'
import { TopbarStudentSearch } from './TopbarStudentSearch'
import { NotificationBell } from './NotificationBell'

interface TopbarProps {
  /** Mobil drawer'ni ochish/yopish (desktopda yig'ish tugmasi yon menyuning o'zida). */
  onMenuClick: () => void
  /** Desktopda yon menyu yig'ilganmi — logo bloki shu kenglikka moslashadi. */
  collapsed?: boolean
}

/**
 * Yuqori panel — edutizim.uz ko'rinishida: BUTUN ENG bo'ylab (yon menyu ustida ham), 56px.
 * Chapda logo bloki (yon menyu kengligida), keyin "orqaga", markaz nomi va qidiruv;
 * o'ngda bildirishnomalar va profil.
 */
export function Topbar({ onMenuClick, collapsed = false }: TopbarProps) {
  const { user, logout } = useAuth()
  const navigate = useNavigate()
  const [menuOpen, setMenuOpen] = useState(false)
  const menuRef = useRef<HTMLDivElement>(null)
  const [quickOpen, setQuickOpen] = useState(false)
  const quickRef = useRef<HTMLDivElement>(null)
  const [schoolName, setSchoolName] = useState('')
  const [logoUrl, setLogoUrl] = useState('')

  // Markaz nomi va logosi; sozlamada saqlangach 'school:updated' hodisasi bilan yangilanadi.
  useEffect(() => {
    const load = () => {
      getSchoolName()
        .then((s) => {
          setSchoolName(s.name)
          setLogoUrl(s.logoUrl)
        })
        .catch(() => {})
    }
    load()
    window.addEventListener('school:updated', load)
    return () => window.removeEventListener('school:updated', load)
  }, [])

  // Tashqariga bosilganda yoki Escape bosilganda ochiq menyularni yopamiz
  useEffect(() => {
    if (!menuOpen && !quickOpen) return
    const onClick = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) setMenuOpen(false)
      if (quickRef.current && !quickRef.current.contains(e.target as Node)) setQuickOpen(false)
    }
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        setMenuOpen(false)
        setQuickOpen(false)
      }
    }
    document.addEventListener('mousedown', onClick)
    document.addEventListener('keydown', onKey)
    return () => {
      document.removeEventListener('mousedown', onClick)
      document.removeEventListener('keydown', onKey)
    }
  }, [menuOpen, quickOpen])

  if (!user) return null

  const handleLogout = () => {
    setMenuOpen(false)
    logout()
    navigate('/login', { replace: true })
  }

  const openAccount = () => {
    setMenuOpen(false)
    navigate('/admin/account')
  }

  const initials = user.fullName
    .split(' ')
    .map((w) => w[0])
    .slice(0, 2)
    .join('')
    .toUpperCase()

  const name = schoolName || 'Wunderkind'

  return (
    <header className="relative z-50 flex h-14 shrink-0 items-center border-b border-[#dbe0e6] bg-white pr-1.5 shadow-[0_1px_2px_rgba(0,0,0,0.04)]">
      {/* Logo bloki — yon menyu kengligida (edutizimda "Edu tizim" logotipi turadigan joy) */}
      <div
        className={cn(
          'flex shrink-0 items-center gap-2 pl-3 lg:pl-5',
          collapsed ? 'lg:w-[72px] lg:justify-center lg:pl-0' : 'lg:w-[173px]',
        )}
      >
        <button
          onClick={onMenuClick}
          className="rounded-lg p-1.5 text-[#667085] transition-colors hover:bg-slate-100 lg:hidden"
          title="Menyu"
          aria-label="Menyu"
        >
          <IconMenu2 className="h-5 w-5" />
        </button>
        <Link to={homeFor(user)} className="flex min-w-0 items-center gap-2" title={name}>
          {logoUrl ? (
            <img src={logoUrl} alt="" className="h-7 w-7 shrink-0 object-contain" />
          ) : (
            <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-md bg-brand-600 text-sm font-extrabold text-white">
              W
            </span>
          )}
          {!collapsed && (
            <span className="hidden truncate text-[18px] font-extrabold text-brand-600 sm:inline">{name}</span>
          )}
        </Link>
      </div>

      <div className="flex min-w-0 flex-1 items-center gap-2.5 pl-2 lg:pl-[15px]">
        {/* Orqaga — edutizimdagi "undo" tugmasi */}
        <button
          onClick={() => navigate(-1)}
          className="hidden h-[30px] w-[30px] shrink-0 items-center justify-center rounded-lg border border-brand-600/20 bg-brand-600/10 text-brand-600 transition-colors hover:bg-brand-600/15 sm:flex"
          title="Orqaga"
          aria-label="Orqaga"
        >
          <IconArrowBackUp className="h-5 w-5" />
        </button>

        {/* Markaz (edutizimda filial tanlash turadigan joy). Bizda filial almashtirish yo'q —
            shuning uchun bu TANLOV EMAS, oddiy yorliq (soxta ochiladigan ro'yxat qilinmadi). */}
        <div className="hidden h-9 w-[200px] shrink-0 items-center gap-2 rounded-lg border border-[#dbe0e6] px-3 text-sm text-[#333] md:flex">
          <IconBuilding className="h-5 w-5 shrink-0 text-[#667085]" />
          <span className="truncate">{name}</span>
        </div>

        <div className="hidden min-w-0 flex-1 sm:flex">
          <TopbarStudentSearch />
        </div>
      </div>

      <div className="flex shrink-0 items-center gap-1 sm:gap-2">
        {/* Mobil ekranda buyruq paneli (Ctrl+K) ikonasi */}
        <button
          onClick={() => window.dispatchEvent(new Event('cmdk:open'))}
          className="rounded-lg p-2 text-brand-600 transition-colors hover:bg-slate-100 sm:hidden"
          title="Qidirish"
        >
          <IconSearch className="h-5 w-5" />
        </button>

        {/* Tezkor bo'limlar — edutizimdagi "+" menyusi */}
        <div className="relative" ref={quickRef}>
          <button
            onClick={() => setQuickOpen((o) => !o)}
            className="flex h-8 w-8 items-center justify-center rounded-lg text-brand-600 transition-colors hover:bg-brand-50"
            title="Tezkor bo'limlar"
            aria-haspopup="menu"
            aria-expanded={quickOpen}
          >
            <IconCirclePlus className="h-5 w-5" />
          </button>
          {quickOpen && (
            <div
              role="menu"
              className="absolute right-0 top-full z-40 mt-2 w-52 overflow-hidden rounded-xl border border-[#dbe0e6] bg-white py-1.5 shadow-lg"
            >
              {[
                { label: 'Buyurtma yaratish', to: '/admin/leads', perm: 'leads.list' },
                { label: "Moliya bo'limi", to: '/admin/finance', perm: 'finance.main' },
              ]
                .filter((q) => can(user.permissions, q.perm, 'view'))
                .map((q) => (
                <button
                  key={q.to}
                  role="menuitem"
                  onClick={() => {
                    setQuickOpen(false)
                    navigate(q.to)
                  }}
                  className="mx-1.5 flex w-[calc(100%-12px)] items-center rounded-lg px-2.5 py-2 text-left text-sm font-semibold text-[#333] transition-colors hover:bg-[#e6eaea] hover:text-brand-600"
                >
                  {q.label}
                </button>
              ))}
            </div>
          )}
        </div>

        <NotificationBell />

        {/* Profil — bosilganda akkaunt sozlamalari/chiqish menyusi ochiladi */}
        <div className="relative" ref={menuRef}>
          <button
            onClick={() => setMenuOpen((o) => !o)}
            className="ml-1 flex items-center rounded-full"
            title={user.fullName}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
          >
            <div className="flex h-[38px] w-[38px] items-center justify-center rounded-full border-2 border-[#dbe0e6] bg-[#ecf0ff] text-sm font-bold text-brand-600">
              {initials}
            </div>
          </button>

          {menuOpen && (
            <div
              role="menu"
              className="absolute right-0 top-full z-40 mt-2 w-56 overflow-hidden rounded-xl border border-slate-200 bg-white shadow-lg"
            >
              <div className="border-b border-slate-100 px-4 py-3">
                <p className="truncate text-sm font-semibold text-[#333]">{user.fullName}</p>
                {user.email && <p className="truncate text-xs text-slate-400">{user.email}</p>}
                <p className="mt-0.5 text-xs text-slate-400">{roleLabels[user.role]}</p>
              </div>
              <button
                role="menuitem"
                onClick={openAccount}
                className="flex w-full items-center gap-2 px-4 py-2.5 text-sm text-[#333] transition-colors hover:bg-slate-50"
              >
                <IconSettings className="h-4 w-4 text-slate-400" />
                Akkaunt sozlamalari
              </button>
              <button
                role="menuitem"
                onClick={handleLogout}
                className="flex w-full items-center gap-2 border-t border-slate-100 px-4 py-2.5 text-sm text-red-600 transition-colors hover:bg-red-50"
              >
                <IconLogout className="h-4 w-4" />
                Chiqish
              </button>
            </div>
          )}
        </div>
      </div>
    </header>
  )
}
