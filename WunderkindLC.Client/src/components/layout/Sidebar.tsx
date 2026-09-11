import { useEffect, useState } from 'react'
import { NavLink, useLocation } from 'react-router-dom'
import { ChevronDown, Search } from 'lucide-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { useUnread } from '@/context/unread-context'
import { getSchoolName } from '@/api/services/settings'
import { navByRole, roleLabels, homeByRole, activeNavTo, type NavItem, type NavChild } from '@/config/navigation'
import { can } from '@/lib/permissions'
import { cn } from '@/lib/utils'

interface SidebarProps {
  /** Mobil drawer ochiqmi (&lt;1024px). */
  open: boolean
  /** Desktopda yon menyu yig'ilganmi — Topbar'dagi hamburger boshqaradi. */
  collapsed?: boolean
  onNavigate: () => void
}

function initialsOf(name: string): string {
  return name
    .split(' ')
    .map((w) => w[0])
    .filter(Boolean)
    .slice(0, 2)
    .join('')
    .toUpperCase()
}

export function Sidebar({ open, collapsed = false, onNavigate }: SidebarProps) {
  const location = useLocation()
  const { user } = useAuth()
  const { unreadChannels } = useUnread()
  const totalUnread = unreadChannels.size
  const [schoolName, setSchoolName] = useState('')
  const [logoUrl, setLogoUrl] = useState('')

  // Markaz nomi va logosini yuklaymiz; sozlamada saqlangach 'school:updated' hodisasi bilan yangilanadi.
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

  if (!user) return null
  const role = user.role
  // Element ko'rinadi: roli mos (yoki roles yo'q) VA xodim shu bo'limni KO'RISH ruxsatiga ega
  // (yoki perm yo'q / permissions yo'q — admin). "Ko'rish" — bare "section" yoki biror "section:action".
  // `permAny` — bir necha ruxsat bilan ishlaydigan band (masalan "Formalar": lid formalari
  // `leads.forms`, daraja testi `schedule.levelTests`): ulardan BIRORTASI yetarli. Sahifa ichida
  // baribir `RequirePerm` bor.
  // ⚠️ Kalit SAHIFA darajasida ham bo'ladi (`students.turnstile`): bo'lim ruxsati sahifani
  // avtomatik ochadi, bitta sahifa ruxsati esa GURUHNI menyuda ko'rsatadi (`can` — `view`).
  const canSee = (x: { roles?: Role[]; perm?: string; permAny?: string[] }) =>
    (!x.roles || x.roles.includes(role)) &&
    (!x.perm || can(user.permissions, x.perm, 'view')) &&
    (!x.permAny || x.permAny.some((p) => can(user.permissions, p, 'view')))

  // Guruh bolalarini (3-darajagacha) rekursiv filtrlaymiz; barcha bolalari yashirilgan guruh ko'rinmaydi.
  function filterNav<T extends { roles?: Role[]; perm?: string; permAny?: string[]; children?: NavChild[] }>(
    list: T[],
  ): T[] {
    return list
      .filter(canSee)
      .map((i) => (i.children ? { ...i, children: filterNav(i.children) } : i))
      .filter((i) => !i.children || i.children.length > 0)
  }
  const items = filterNav(navByRole[role])
  // Manzil FAQAT BITTA bandga tegishli — eng aniq moslik g'olib (`activeNavTo`).
  // Har guruh o'zini mustaqil tekshirganda bitta manzil bir nechta guruhni ochib
  // yuborardi (masalan hisobot marshruti o'zining ESKI bo'limini ham ochardi).
  const activeTo = activeNavTo(items, location.pathname)

  return (
    <aside
      className={cn(
        // Desktopda (lg) statik; `open` faqat MOBIL drawer'ni boshqaradi.
        'fixed inset-y-0 left-0 z-40 flex w-64 shrink-0 flex-col border-r border-slate-200 bg-white transition-transform duration-200 lg:static lg:translate-x-0',
        open ? 'translate-x-0' : '-translate-x-full',
        // Desktopda yig'ilgan holat. Bu XAVFSIZ, chunki Topbar'dagi hamburger endi HAR QANDAY
        // ekran kengligida ko'rinadi — menyusiz qolib ketilmaydi (ilgari hamburger `lg:hidden`
        // bo'lgani uchun sidebar'ni desktopda yashirish mumkin emas edi).
        collapsed && 'lg:hidden',
      )}
    >
      {/* Brend — logo Wunderkind sariq plitkasi ichida (LMS bilan bir xil brend belgisi) */}
      <div className="flex h-16 items-center gap-2.5 border-b border-slate-200 px-5">
        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-[#FFD006]">
          {logoUrl ? (
            <img src={logoUrl} alt="Logo" className="h-6 w-6 object-contain" />
          ) : (
            <span className="text-sm font-bold text-slate-900">W</span>
          )}
        </div>
        <div className="min-w-0 leading-tight">
          <p className="truncate font-semibold text-slate-800">{schoolName || 'WunderkindLC'}</p>
          <p className="text-xs text-slate-400">{roleLabels[role]}</p>
        </div>
      </div>

      {/* Menyu */}
      <nav className="flex-1 space-y-1 overflow-y-auto p-3">
        {/* Qidiruv (Ctrl+K) — buyruq paneli: bo'limlar + o'quvchi (FISH/telefon) */}
        <button
          type="button"
          onClick={() => window.dispatchEvent(new Event('cmdk:open'))}
          className="mb-2 flex w-full items-center gap-2.5 rounded-xl border border-slate-200 px-3 py-2 text-sm text-slate-400 transition-colors hover:bg-slate-50"
          title="Qidirish (Ctrl+K)"
        >
          <Search className="h-4 w-4" />
          <span className="flex-1 text-left">Qidirish...</span>
          <kbd className="rounded border border-slate-200 bg-slate-50 px-1.5 text-[10px] text-slate-400">
            ⌘K
          </kbd>
        </button>
        <div className="px-3 pb-1 pt-2 text-[11px] font-semibold uppercase tracking-wider text-slate-400">
          Asosiy
        </div>
        {items.map((item) =>
          item.children ? (
            <NavGroup key={item.to} item={item} active={item.to === activeTo} onNavigate={onNavigate} />
          ) : (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.to === homeByRole[role]}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors',
                  isActive
                    ? 'bg-brand-50 text-brand-700'
                    : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900',
                )
              }
            >
              <item.icon className="h-5 w-5" />
              {item.label}
              {/* O'qilmagan chat belgisi — guruh chati "Chats" bo'limiga ko'chgan
                  (o'qituvchi menyusida u hamon "Xabarlar" ichida). */}
              {(item.to.endsWith('/chats') || item.to === '/teacher/messages') &&
                totalUnread > 0 && (
                <span className="ml-auto flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-red-500 px-1 font-mono text-[10px] font-bold text-white">
                  {totalUnread > 9 ? '9+' : totalUnread}
                </span>
              )}
            </NavLink>
          ),
        )}
      </nav>

      {/* Foydalanuvchi */}
      <div className="border-t border-slate-200 p-3">
        <div className="flex items-center gap-3 rounded-xl bg-slate-50 p-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-brand-600 text-xs font-semibold text-white">
            {initialsOf(user.fullName)}
          </div>
          <div className="min-w-0 leading-tight">
            <p className="truncate text-sm font-medium text-slate-700">{user.fullName}</p>
            <p className="truncate text-xs text-slate-400">{roleLabels[role]}</p>
          </div>
        </div>
      </div>
    </aside>
  )
}

function NavGroup({
  item,
  active,
  onNavigate,
}: {
  item: NavItem
  /** Joriy manzil AYNAN shu guruhga tegishlimi — qaror `Sidebar` da (`activeNavTo`),
   *  chunki u faqat qo'shni guruhlar bilan solishtirib aniqlanadi. */
  active: boolean
  onNavigate: () => void
}) {
  // "Chats" guruhi yig'ilgan holatda ham o'qilmagan xabar sonini ko'rsatadi.
  const { unreadChannels } = useUnread()
  const groupUnread = item.to.endsWith('/chats') ? unreadChannels.size : 0
  const [openGroup, setOpenGroup] = useState(active)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- marshrut shu guruh ostida bo'lsa, uni avtomatik ochamiz (maqsadli)
    if (active) setOpenGroup(true)
  }, [active])

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpenGroup((o) => !o)}
        className={cn(
          'flex w-full items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition-colors',
          active
            ? 'bg-brand-50 text-brand-700'
            : 'text-slate-600 hover:bg-slate-50 hover:text-slate-900',
        )}
      >
        <item.icon className="h-5 w-5" />
        {item.label}
        {groupUnread > 0 && (
          <span className="ml-auto flex h-5 min-w-[1.25rem] items-center justify-center rounded-full bg-red-500 px-1 font-mono text-[10px] font-bold text-white">
            {groupUnread > 9 ? '9+' : groupUnread}
          </span>
        )}
        <ChevronDown
          className={cn(
            'h-3.5 w-3.5 text-slate-400 transition-transform',
            groupUnread > 0 ? 'ml-1.5' : 'ml-auto',
            openGroup && 'rotate-180',
          )}
        />
      </button>

      {openGroup && (
        <div className="mt-1 space-y-1 border-l border-slate-200 pl-3 ml-[22px]">
          {item.children?.map((child) =>
            child.children ? (
              <NavSubGroup key={child.to} child={child} onNavigate={onNavigate} />
            ) : (
              <NavLink
                key={child.to}
                to={child.to}
                end={child.end}
                onClick={onNavigate}
                className={({ isActive }) =>
                  cn(
                    'block rounded-lg px-3 py-2 text-sm transition-colors',
                    isActive
                      ? 'bg-brand-50 font-medium text-brand-700'
                      : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
                  )
                }
              >
                {child.label}
              </NavLink>
            ),
          )}
        </div>
      )}
    </div>
  )
}

// 3-daraja: "O'quv bo'limi" → "Guruhlar" → "Reyting" kabi ichki yig'iladigan bo'lim.
function NavSubGroup({ child, onNavigate }: { child: NavChild; onNavigate: () => void }) {
  const location = useLocation()
  const isUnder =
    child.children?.some(
      (c) => location.pathname === c.to || location.pathname.startsWith(c.to + '/'),
    ) ?? false
  const [openGroup, setOpenGroup] = useState(isUnder)

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- marshrut shu bo'lim ostida bo'lsa avtomatik ochamiz
    if (isUnder) setOpenGroup(true)
  }, [isUnder])

  return (
    <div>
      <button
        type="button"
        onClick={() => setOpenGroup((o) => !o)}
        className={cn(
          'flex w-full items-center gap-2 rounded-lg px-3 py-2 text-sm transition-colors',
          isUnder ? 'font-medium text-brand-700' : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
        )}
      >
        {child.label}
        <ChevronDown className={cn('ml-auto h-3 w-3 transition-transform', openGroup && 'rotate-180')} />
      </button>

      {openGroup && (
        <div className="mt-1 space-y-1 pl-3">
          {child.children?.map((c) => (
            <NavLink
              key={c.to}
              to={c.to}
              end={c.end}
              onClick={onNavigate}
              className={({ isActive }) =>
                cn(
                  'block rounded-lg px-3 py-2 text-sm transition-colors',
                  isActive
                    ? 'bg-brand-50 font-medium text-brand-700'
                    : 'text-slate-500 hover:bg-slate-50 hover:text-slate-800',
                )
              }
            >
              {c.label}
            </NavLink>
          ))}
        </div>
      )}
    </div>
  )
}
