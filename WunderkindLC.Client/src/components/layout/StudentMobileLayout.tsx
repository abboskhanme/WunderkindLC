import { Suspense, useEffect, useState, type CSSProperties } from 'react'
import { Link, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { CalendarDays, GraduationCap, House, Trophy, UserRound, Wallet, type LucideIcon } from 'lucide-react'
import { Loader } from '@/components/ui/Loader'
import { TABS, backFallback, tabOf, type TabKey } from '@/pages/student/nav'
import {
  OWN_BG,
  OWN_SURFACE,
  THEME_EVENT,
  getThemePref,
  resolveTheme,
  telegramApp,
  telegramPalette,
  tgAtLeast,
  type ThemePref,
} from '@/pages/student/theme'

const TAB_ICON: Record<TabKey, LucideIcon> = {
  home: House,
  lessons: CalendarDays,
  results: Trophy,
  finance: Wallet,
  profile: UserRound,
}

const systemDarkQuery = () =>
  typeof window !== 'undefined' && typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-color-scheme: dark)')
    : null

/**
 * Student / parent cabinet shell (phone browser + Telegram Mini App).
 * - 5-tab bottom navigation (nav.ts is the single source of the IA); desktop ≥1024px — side menu.
 * - Theme: Settings choice (light/dark) or "auto" — Telegram's colorScheme inside the Mini App
 *   (with Telegram's own surface/text colours), the system preference elsewhere.
 * - Inside Telegram: header/background colours follow the page, native BackButton on sub-pages,
 *   vertical swipes disabled (the cabinet scrolls inside its own container).
 */
export function StudentMobileLayout() {
  const location = useLocation()
  const navigate = useNavigate()
  const tg = telegramApp()
  const [pref, setPref] = useState<ThemePref>(getThemePref)
  const [systemDark, setSystemDark] = useState<boolean>(() => !!systemDarkQuery()?.matches)
  // Bumped on Telegram's themeChanged — re-renders; colorScheme/themeParams are read from the SDK object.
  const [, setTgTheme] = useState(0)

  useEffect(() => {
    const onPref = () => setPref(getThemePref())
    window.addEventListener(THEME_EVENT, onPref)
    const mq = systemDarkQuery()
    const onSys = (e: MediaQueryListEvent) => setSystemDark(e.matches)
    mq?.addEventListener?.('change', onSys)
    return () => {
      window.removeEventListener(THEME_EVENT, onPref)
      mq?.removeEventListener?.('change', onSys)
    }
  }, [])

  useEffect(() => {
    if (!tg?.onEvent) return
    const onTheme = () => setTgTheme((n) => n + 1)
    tg.onEvent('themeChanged', onTheme)
    try {
      if (tgAtLeast(tg, '7.7')) tg.disableVerticalSwipes?.()
    } catch {
      /* older client */
    }
    return () => tg.offEvent?.('themeChanged', onTheme)
  }, [tg])

  const tgScheme = tg?.colorScheme === 'dark' || tg?.colorScheme === 'light' ? tg.colorScheme : undefined
  const theme = resolveTheme(pref, tgScheme, systemDark)
  const palette = tg && pref === 'auto' && tgScheme === theme ? telegramPalette(tg.themeParams, theme) : null
  const bgColor = palette?.['--bg'] ?? OWN_BG[theme]
  const surfaceColor = palette?.['--surface'] ?? OWN_SURFACE[theme]

  // Telegram chrome (top bar, overscroll background, bottom bar) follows the cabinet colours.
  useEffect(() => {
    if (!tg) return
    try {
      if (tgAtLeast(tg, '6.9')) {
        tg.setHeaderColor?.(bgColor)
        tg.setBackgroundColor?.(bgColor)
      } else if (tgAtLeast(tg, '6.1')) {
        tg.setHeaderColor?.('secondary_bg_color')
      }
      if (tgAtLeast(tg, '7.10')) tg.setBottomBarColor?.(surfaceColor)
    } catch {
      /* unsupported on this client — purely cosmetic */
    }
  }, [tg, bgColor, surfaceColor])

  // Telegram's native BackButton on sub-pages (pages hide their own arrow — see PageHeader).
  const parent = backFallback(location.pathname)
  useEffect(() => {
    const bb = tg?.BackButton
    if (!bb || !tgAtLeast(tg, '6.1')) return
    if (!parent) {
      bb.hide()
      return
    }
    const onBack = () => {
      const idx = (window.history.state as { idx?: number } | null)?.idx
      if (typeof idx === 'number' && idx > 0) navigate(-1)
      else navigate(parent, { replace: true })
    }
    bb.onClick(onBack)
    bb.show()
    return () => bb.offClick(onBack)
  }, [tg, parent, navigate])

  const active = tabOf(location.pathname)
  const onTab = (key: TabKey) => {
    if (key !== active) {
      try {
        tg?.HapticFeedback?.selectionChanged()
      } catch {
        /* no haptics */
      }
    }
  }

  return (
    <div
      className="student-app"
      data-theme={theme}
      data-telegram={tg ? '' : undefined}
      style={{ display: 'flex', flexDirection: 'column', height: '100dvh', overflow: 'hidden', width: '100%', ...(palette as CSSProperties | null) }}
    >
      {/* Desktop side menu (≥1024px only, CSS) — phones/WebView use the bottom bar. */}
      <aside className="st-side">
        <div className="st-brand">
          <GraduationCap size={24} />
          <span>Kabinet</span>
        </div>
        {TABS.map((t) => {
          const Ic = TAB_ICON[t.key]
          const on = t.key === active
          return (
            <Link key={t.key} to={t.to} className={'st-nav-item' + (on ? ' on' : '')} aria-current={on ? 'page' : undefined}>
              <Ic size={20} strokeWidth={on ? 2.4 : 2} />
              <span>{t.label}</span>
            </Link>
          )
        })}
      </aside>

      {/* Content: every screen is a flex:1 .screen with its own inner .scroll. */}
      <div className="st-main" style={{ flex: 1, minHeight: 0, overflow: 'hidden', display: 'flex', flexDirection: 'column' }}>
        {/* Suspense INSIDE the layout — the shell (tab bar) stays while a screen chunk loads. */}
        <Suspense fallback={<Loader className="flex-1" />}>
          <Outlet />
        </Suspense>
      </div>

      <nav className="st-nav" aria-label="Asosiy menyu">
        {TABS.map((t) => {
          const Ic = TAB_ICON[t.key]
          const on = t.key === active
          return (
            <Link
              key={t.key}
              to={t.to}
              onClick={() => onTab(t.key)}
              className={'st-nav-a' + (on ? ' on' : '')}
              aria-current={on ? 'page' : undefined}
            >
              <span className="st-nav-ic">
                <Ic size={24} strokeWidth={on ? 2.3 : 1.9} />
              </span>
              <span className="st-nav-lbl">{t.label}</span>
            </Link>
          )
        })}
      </nav>
    </div>
  )
}
