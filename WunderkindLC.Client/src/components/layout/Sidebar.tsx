import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { NavLink, useLocation, type Location } from 'react-router-dom'
import { IconChevronLeft, IconChevronRight } from '@tabler/icons-react'
import type { Role } from '@/types'
import { useAuth } from '@/context/auth-context'
import { useUnread } from '@/context/unread-context'
import { navByRole, homeByRole, activeNavTo, type NavChild } from '@/config/navigation'
import { can } from '@/lib/permissions'
import { cn } from '@/lib/utils'

interface SidebarProps {
  /** Mobil drawer ochiqmi (&lt;1024px). */
  open: boolean
  /** Desktopda yon menyu yig'ilganmi (faqat ikonkalar). */
  collapsed?: boolean
  onToggleCollapse?: () => void
  onNavigate: () => void
}

/**
 * Bola band FAOLmi. `?tab=` li band (masalan Moliya → «Bonus» = `/admin/finance?tab=bonuses`)
 * manzil bilan birga TAB bo'yicha ham solishtiriladi — aks holda bitta sahifaning to'rtta tabi
 * menyuda birdan faol bo'lib ko'rinardi. Tabsiz manzilga kelinsa (`/admin/finance`) sahifaning
 * birinchi tabi ochiladi, shuning uchun u holda hech biri ajratilmaydi — bu zararsiz.
 */
function childActive(child: NavChild, loc: Location): boolean {
  const [path, query] = child.to.split('?')
  const pathOk = child.end
    ? loc.pathname === path
    : loc.pathname === path || loc.pathname.startsWith(path + '/')
  if (!pathOk) return false
  if (!query) return true
  const want = new URLSearchParams(query).get('tab')
  return want === null || new URLSearchParams(loc.search).get('tab') === want
}

/** Desktop (lg) ekranmi — yon ro'yxat (flyout) faqat desktopda; mobilda bolalar ichkarida ochiladi. */
function useIsDesktop(): boolean {
  const [desktop, setDesktop] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(min-width: 1024px)').matches,
  )
  useEffect(() => {
    const mq = window.matchMedia('(min-width: 1024px)')
    const onChange = (e: MediaQueryListEvent) => setDesktop(e.matches)
    mq.addEventListener('change', onChange)
    return () => mq.removeEventListener('change', onChange)
  }, [])
  return desktop
}

/**
 * Yon menyu — edutizim.uz ko'rinishida: ikonka + nom, guruh ustiga olib borilganda (yoki
 * bosilganda) o'ng tomonda ro'yxat ochiladi. Guruh o'zi sahifa OCHMAYDI.
 */
export function Sidebar({ open, collapsed = false, onToggleCollapse, onNavigate }: SidebarProps) {
  const location = useLocation()
  const { user } = useAuth()
  const { unreadChannels } = useUnread()
  const isDesktop = useIsDesktop()
  const [openKey, setOpenKey] = useState<string | null>(null)
  const [anchor, setAnchor] = useState<DOMRect | null>(null)
  const closeTimer = useRef<number | undefined>(undefined)

  // Sahifa almashganda ochiq ro'yxat yopiladi.
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- marshrut o'zgarganda flyout yopilishi maqsadli
    setOpenKey(null)
  }, [location.pathname, location.search])

  if (!user) return null
  const role = user.role
  // Band ko'rinadi: roli mos VA xodim shu bo'limni KO'RISH ruxsatiga ega (`can` — `view`).
  // `permAny` — ruxsatlardan BIRORTASI yetarli. Sahifaning o'zi baribir `RequirePerm` bilan yopiq.
  const canSee = (x: { roles?: Role[]; perm?: string; permAny?: string[] }) =>
    (!x.roles || x.roles.includes(role)) &&
    (!x.perm || can(user.permissions, x.perm, 'view')) &&
    (!x.permAny || x.permAny.some((p) => can(user.permissions, p, 'view')))

  // Bolalarni (3-darajagacha) rekursiv filtrlaymiz; bolasi qolmagan guruh ko'rinmaydi.
  function filterNav<T extends { roles?: Role[]; perm?: string; permAny?: string[]; children?: NavChild[] }>(
    list: T[],
  ): T[] {
    return list
      .filter(canSee)
      .map((i) => (i.children ? { ...i, children: filterNav(i.children) } : i))
      .filter((i) => !i.children || i.children.length > 0)
  }
  const items = filterNav(navByRole[role])
  const activeTo = activeNavTo(items, location.pathname)
  const unread = unreadChannels.size

  const cancelClose = () => window.clearTimeout(closeTimer.current)
  const scheduleClose = () => {
    cancelClose()
    closeTimer.current = window.setTimeout(() => setOpenKey(null), 160)
  }
  const openGroup = (key: string, el: HTMLElement) => {
    cancelClose()
    setAnchor(el.getBoundingClientRect())
    setOpenKey(key)
  }

  const flyoutItem = items.find((i) => i.to === openKey && i.children)

  return (
    <aside
      className={cn(
        // Desktopda (lg) statik; `open` faqat MOBIL drawer'ni boshqaradi.
        'fixed inset-y-0 left-0 z-40 flex shrink-0 flex-col bg-white pt-14 transition-[width,transform] duration-200 lg:relative lg:z-20 lg:translate-x-0 lg:pt-0',
        open ? 'translate-x-0' : '-translate-x-full',
        collapsed ? 'w-[260px] lg:w-[72px]' : 'w-[260px] lg:w-[173px]',
      )}
    >
      {/* Yig'ish tugmasi — edutizimdagidek yon menyu chetida, sarlavha ostida */}
      {onToggleCollapse && (
        <button
          type="button"
          onClick={onToggleCollapse}
          title={collapsed ? 'Menyuni ochish' : "Menyuni yig'ish"}
          aria-label={collapsed ? 'Menyuni ochish' : "Menyuni yig'ish"}
          className="absolute -right-[14px] top-1 z-30 hidden h-7 w-7 items-center justify-center rounded-lg border border-[#dbe0e6] bg-white text-[#333] shadow-[0_2px_8px_rgba(0,0,0,0.06)] transition-colors hover:text-brand-600 lg:flex"
        >
          {collapsed ? <IconChevronRight className="h-4 w-4" /> : <IconChevronLeft className="h-4 w-4" />}
        </button>
      )}

      <nav className="flex-1 space-y-[2px] overflow-y-auto px-[5px] pb-2.5 pt-2.5">
        {items.map((item) => {
          const Icon = item.icon
          if (!item.children) {
            return (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.to === homeByRole[role]}
                onClick={onNavigate}
                title={collapsed ? item.label : undefined}
                className={({ isActive }) => itemClass(isActive, false, collapsed)}
              >
                {({ isActive }) => (
                  <>
                    <ActiveBar show={isActive} />
                    <Icon className="h-5 w-5 shrink-0" />
                    {!collapsed && <span className="whitespace-nowrap">{item.label}</span>}
                  </>
                )}
              </NavLink>
            )
          }

          const active = item.to === activeTo
          const isOpen = openKey === item.to
          const badge = item.to === '#future' ? unread : 0
          return (
            <div key={item.to}>
              <button
                type="button"
                title={collapsed ? item.label : undefined}
                onMouseEnter={(e) => isDesktop && openGroup(item.to, e.currentTarget)}
                onMouseLeave={() => isDesktop && scheduleClose()}
                onClick={(e) => {
                  if (isOpen) setOpenKey(null)
                  else openGroup(item.to, e.currentTarget)
                }}
                className={itemClass(active, isOpen, collapsed)}
              >
                <ActiveBar show={active || isOpen} />
                <span className="relative shrink-0">
                  <Icon className="h-5 w-5" />
                  {badge > 0 && (
                    <span className="absolute -right-2 -top-2 flex h-[15px] min-w-[15px] items-center justify-center rounded-full bg-[#e53e3e] px-1 text-[9px] font-bold leading-none text-white">
                      {badge > 9 ? '9+' : badge}
                    </span>
                  )}
                </span>
                {!collapsed && <span className="whitespace-nowrap">{item.label}</span>}
              </button>

              {/* Mobilda bolalar shu yerning o'zida (drawer ichida) ochiladi */}
              {!isDesktop && isOpen && (
                <div className="mb-1 ml-8 mt-1 space-y-0.5">
                  <FlyoutLinks items={item.children!} onNavigate={onNavigate} />
                </div>
              )}
            </div>
          )
        })}
      </nav>

      {isDesktop && flyoutItem && anchor &&
        createPortal(
          <Flyout
            anchor={anchor}
            title={collapsed ? flyoutItem.label : undefined}
            wide={flyoutItem.columns}
            onMouseEnter={cancelClose}
            onMouseLeave={scheduleClose}
          >
            {flyoutItem.columns ? (
              <FlyoutColumns items={flyoutItem.children!} onNavigate={onNavigate} />
            ) : (
              <FlyoutLinks items={flyoutItem.children!} onNavigate={onNavigate} />
            )}
          </Flyout>,
          document.body,
        )}
    </aside>
  )
}

function itemClass(active: boolean, open: boolean, collapsed: boolean): string {
  // edutizimda hover, ochiq va faol holat BIR XIL ko'rinadi: kulrang fon + ko'k matn/ikonka +
  // chap chetda ko'k chiziq.
  return cn(
    'group relative flex h-[45px] w-full items-center gap-[5px] rounded-[10px] pl-3 pr-1.5 text-left text-[14px] font-semibold leading-[21px] transition-colors',
    collapsed && 'lg:mx-auto lg:h-10 lg:w-[62px] lg:justify-center lg:px-0',
    active || open ? 'bg-[#e6eaea] text-brand-600' : 'text-[#333] hover:bg-[#e6eaea] hover:text-brand-600',
  )
}

/** Faol (yoki ustiga olib borilgan) bandning chap chetidagi 3px ko'k chiziq (edutizim belgisi). */
function ActiveBar({ show }: { show: boolean }) {
  return (
    <span
      className={cn(
        'absolute inset-y-1.5 left-0 w-[3px] rounded-full bg-brand-600 transition-opacity',
        show ? 'opacity-100' : 'opacity-0 group-hover:opacity-100',
      )}
    />
  )
}

/** Guruh ro'yxatining ichi. 3-darajali guruh ("Future") bo'lsa kichik guruhlar SARLAVHA bo'lib chiqadi. */
function FlyoutLinks({ items, onNavigate }: { items: NavChild[]; onNavigate: () => void }) {
  const location = useLocation()
  return (
    <>
      {items.map((c) =>
        c.children ? (
          <div key={c.to} className="pb-1">
            <p className="px-2.5 pb-0.5 pt-2 text-[11px] font-bold uppercase tracking-wide text-[#949494]">
              {c.label}
            </p>
            <FlyoutLinks items={c.children} onNavigate={onNavigate} />
          </div>
        ) : (
          <NavLink
            key={c.to}
            to={c.to}
            onClick={onNavigate}
            className={cn(
              'mt-1 flex min-h-9 items-center rounded-lg px-2.5 py-1 text-[14px] font-semibold leading-tight transition-colors',
              childActive(c, location)
                ? 'bg-[#e6eaea] text-brand-600'
                : 'text-[#333] hover:bg-[#e6eaea] hover:text-brand-600',
            )}
          >
            {c.label}
          </NavLink>
        ),
      )}
    </>
  )
}

/**
 * Kichik guruhlar YONMA-YON ustun bo'lib (edutizimdagi "Moliya" ro'yxati). Faqat
 * `NavItem.columns` bo'lganda ishlatiladi; qolgan guruhlar avvalgidek ustma-ust chiziladi.
 *
 * ⚠️ Ruxsat filtri allaqachon `filterNav` da bajarilgan — bo'sh qolgan ustun bu yerga
 * umuman kelmaydi, shuning uchun qo'shimcha tekshiruv YO'Q.
 */
function FlyoutColumns({ items, onNavigate }: { items: NavChild[]; onNavigate: () => void }) {
  return (
    <div className="flex items-stretch">
      {items.map((col, i) => (
        <div
          key={col.to}
          // ⚠️ `w-max` + `whitespace-nowrap`: band nomi ikki qatorga SINMASIN
          // ("Rejalashtirilgan xarajatlar") — ustun mazmuniga qarab kengayadi.
          className={cn(
            'w-max min-w-[188px] px-2 [&_a]:whitespace-nowrap',
            i > 0 && 'border-l border-[#eceff2]',
          )}
        >
          <p className="px-2.5 pb-1.5 pt-1 text-[11px] font-bold uppercase tracking-wide text-[#949494]">
            {col.label}
          </p>
          <FlyoutLinks items={col.children ?? []} onNavigate={onNavigate} />
        </div>
      ))}
    </div>
  )
}

/**
 * Yon ro'yxat (flyout) — `position: fixed`, guruh bandining o'ng tomonida. Pastga sig'masa
 * pastki cheti band bilan tekislanadi (edutizimda "Sozlamalar" ro'yxati shunday yuqoriga ochiladi).
 */
function Flyout({
  anchor,
  title,
  children,
  wide,
  onMouseEnter,
  onMouseLeave,
}: {
  anchor: DOMRect
  title?: string
  children: React.ReactNode
  /** Ustunli ro'yxat — kengligi mazmunga qarab o'sadi (`NavItem.columns`). */
  wide?: boolean
  onMouseEnter: () => void
  onMouseLeave: () => void
}) {
  const ref = useRef<HTMLDivElement>(null)
  const [top, setTop] = useState(anchor.top)

  useLayoutEffect(() => {
    const h = ref.current?.offsetHeight ?? 0
    const vh = window.innerHeight
    let t = anchor.top
    if (t + h > vh - 8) t = Math.max(8, anchor.bottom - h)
    setTop(t)
  }, [anchor])

  return (
    <div
      ref={ref}
      onMouseEnter={onMouseEnter}
      onMouseLeave={onMouseLeave}
      style={{ top, left: anchor.right + 10 }}
      className={cn(
        'fixed z-50 rounded-xl border border-[#dbe0e6] bg-white shadow-[0_6px_20px_-4px_rgba(24,39,75,0.08),0_12px_48px_-4px_rgba(24,39,75,0.10)]',
        wide ? 'w-auto' : 'w-[220px]',
      )}
    >
      {/* Band tomonga qaragan kichik strelka (edutizimdagi popover belgisi) */}
      <span
        style={{ top: Math.min(Math.max(anchor.top + anchor.height / 2 - top, 14), 9999) - 6 }}
        className="absolute -left-[7px] h-3 w-3 rotate-45 border-b border-l border-[#dbe0e6] bg-white"
      />
      <div className="relative max-h-[calc(100vh-16px)] overflow-y-auto px-1.5 py-1.5">
        {title && <p className="px-2.5 pb-0.5 pt-1 text-[13px] font-bold text-[#333]">{title}</p>}
        {children}
      </div>
    </div>
  )
}
