/**
 * Marketing (Instagram AI agenti) bo'limining umumiy dizayn qatlami — ikonlar,
 * Instagram glyphi, sahifa o'rovchisi, sahifa ichidagi sub-navigatsiya, to'liq
 * ekranli oynalar va holat blokchalari.
 *
 * ⚠️ Mock ma'lumotlar OLIB TASHLANDI — barcha sahifalar `api/services/instagram.ts`
 * orqali haqiqiy API bilan ishlaydi. Bo'lim FAQAT Instagram'dan iborat.
 *
 * ⚠️ Bu fayl FAQAT komponent va TIP eksport qiladi (eslint
 * `react-refresh/only-export-components`) — sof funksiya qo'shilmaydi.
 */
import { useEffect, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { Link, NavLink } from 'react-router-dom'

/* ---------------- ICONS (line) ---------------- */
/**
 * Uslub BIR XIL: 24×24 viewBox, `fill="none"`, `stroke="currentColor"`, chiziqli
 * (Lucide/Feather). Bir nechta bo'lak bo'lsa bitta `d` ichida `M…` bilan ketma-ket.
 */
const Ic: Record<string, string> = {
  dashboard: 'M3 13h8V3H3v10zm0 8h8v-6H3v6zm10 0h8V11h-8v10zm0-18v6h8V3h-8z',
  rules: 'M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2M9 5a2 2 0 0 0 2 2h2a2 2 0 0 0 2-2M9 5a2 2 0 0 1 2-2h2a2 2 0 0 1 2 2m-6 9 2 2 4-4',
  inbox: 'M22 12h-6l-2 3h-4l-2-3H2M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11z',
  book: 'M4 19.5A2.5 2.5 0 0 1 6.5 17H20M4 19.5A2.5 2.5 0 0 0 6.5 22H20v-5M4 19.5V4.5A2.5 2.5 0 0 1 6.5 2H20v15',
  ai: 'M12 2a3 3 0 0 0-3 3 3 3 0 0 0-3 3 3 3 0 0 0 0 6 3 3 0 0 0 3 3 3 3 0 0 0 6 0 3 3 0 0 0 3-3 3 3 0 0 0 0-6 3 3 0 0 0-3-3 3 3 0 0 0-3-3zM12 8v8M8 12h8',
  analytics: 'M3 3v18h18M7 16l4-4 3 3 5-6',
  settings: 'M12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z',
  search: 'M11 19a8 8 0 1 0 0-16 8 8 0 0 0 0 16zM21 21l-4.35-4.35',
  bell: 'M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9M13.73 21a2 2 0 0 1-3.46 0',
  plus: 'M12 5v14M5 12h14',
  chevDown: 'M6 9l6 6 6-6',
  chevRight: 'M9 18l6-6-6-6',
  chevUp: 'M18 15l-6-6-6 6',
  arrowRight: 'M5 12h14M12 5l7 7-7 7',
  send: 'M22 2 11 13M22 2l-7 20-4-9-9-4 20-7z',
  check: 'M20 6 9 17l-5-5',
  edit: 'M11 4H4a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-7M18.5 2.5a2.12 2.12 0 0 1 3 3L12 15l-4 1 1-4 9.5-9.5z',
  trash: 'M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2',
  copy: 'M20 9h-9a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h9a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2zM5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1',
  clock: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 6v6l4 2',
  users: 'M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8zM23 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75',
  msg: 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z',
  trendUp: 'M23 6l-9.5 9.5-5-5L1 18M17 6h6v6',
  filter: 'M22 3H2l8 9.46V19l4 2v-8.54L22 3z',
  more: 'M12 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM12 6a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM12 20a1 1 0 1 0 0-2 1 1 0 0 0 0 2z',
  sparkle: 'M12 3l1.9 5.8L20 10l-5.8 1.9L12 18l-1.9-5.8L4 10l5.8-1.1L12 3z',
  zap: 'M13 2 3 14h9l-1 8 10-12h-9l1-8z',
  link: 'M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71',
  unlink: 'M18.84 12.25l1.72-1.71a5 5 0 0 0-7.07-7.07l-1.72 1.71M5.17 11.75l-1.71 1.71a5 5 0 0 0 7.07 7.07l1.71-1.71M2 2l20 20',
  warn: 'M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01',
  refresh: 'M23 4v6h-6M1 20v-6h6M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15',
  play: 'M5 3l14 9-14 9V3z',
  close: 'M18 6 6 18M6 6l12 12',
  user: 'M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2M12 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8z',
  fire: 'M12 2s4 4 4 8a4 4 0 0 1-8 0c0-1 .5-2 .5-2S6 11 6 14a6 6 0 0 0 12 0c0-5-6-12-6-12z',
  grip: 'M9 5h.01M9 12h.01M9 19h.01M15 5h.01M15 12h.01M15 19h.01',

  /* ── kontent / navigatsiya uchun qo'shilganlar ── */
  image: 'M19 3H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V5a2 2 0 0 0-2-2zM8.5 10a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3zM21 15l-5-5L5 21',
  film: 'M19.8 2H4.2A2.2 2.2 0 0 0 2 4.2v15.6A2.2 2.2 0 0 0 4.2 22h15.6a2.2 2.2 0 0 0 2.2-2.2V4.2A2.2 2.2 0 0 0 19.8 2zM7 2v20M17 2v20M2 12h20M2 7h5M2 17h5M17 17h5M17 7h5',
  layers: 'M12 2 2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5',
  calendar: 'M19 4H5a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2V6a2 2 0 0 0-2-2zM16 2v4M8 2v4M3 10h18',
  list: 'M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01',
  gauge: 'M20.5 15.5a9 9 0 1 0-17 0M12 13l4-4M12 13h.01',
  upload: 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M17 8l-5-5-5 5M12 3v12',
  download: 'M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4M7 10l5 5 5-5M12 15V3',
  arrowLeft: 'M19 12H5M12 19l-7-7 7-7',
  chevLeft: 'M15 18l-6-6 6-6',
  eye: 'M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8zM12 15a3 3 0 1 0 0-6 3 3 0 0 0 0 6z',
  text: 'M4 7V4h16v3M9 20h6M12 4v16',
  hash: 'M4 9h16M4 15h16M10 3 8 21M16 3l-2 18',
  sliders: 'M4 21v-7M4 10V3M12 21v-9M12 8V3M20 21v-5M20 12V3M1 14h6M9 8h6M17 16h6',
  globe: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM2 12h20M12 2a15.3 15.3 0 0 1 4 10 15.3 15.3 0 0 1-4 10 15.3 15.3 0 0 1-4-10 15.3 15.3 0 0 1 4-10z',
  heart: 'M20.84 4.61a5.5 5.5 0 0 0-7.78 0L12 5.67l-1.06-1.06a5.5 5.5 0 1 0-7.78 7.78l1.06 1.06L12 21.23l7.78-7.78 1.06-1.06a5.5 5.5 0 0 0 0-7.78z',
  comment: 'M21 11.5a8.38 8.38 0 0 1-.9 3.8 8.5 8.5 0 0 1-7.6 4.7 8.38 8.38 0 0 1-3.8-.9L3 21l1.9-5.7a8.38 8.38 0 0 1-.9-3.8 8.5 8.5 0 0 1 4.7-7.6 8.38 8.38 0 0 1 3.8-.9h.5a8.48 8.48 0 0 1 8 8v.5z',
  bookmark: 'M19 21l-7-5-7 5V5a2 2 0 0 1 2-2h10a2 2 0 0 1 2 2z',
  share: 'M4 12v8a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-8M16 6l-4-4-4 4M12 2v13',
  dots: 'M12 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM19 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2zM5 13a1 1 0 1 0 0-2 1 1 0 0 0 0 2z',
  save: 'M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2zM17 21v-8H7v8M7 3v5h8',
  history: 'M3 3v5h5M3.05 13A9 9 0 1 0 6 5.3L3 8M12 7v5l4 2',
  wand: 'M15 4V2M15 16v-2M8 9h2M20 9h2M17.8 11.8 19 13M15 9h.01M17.8 6.2 19 5M3 21l9-9M12.2 6.2 11 5',
  crop: 'M6.13 1 6 16a2 2 0 0 0 2 2h15M1 6.13 16 6a2 2 0 0 1 2 2v15',
  alert: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 8v4M12 16h.01',
  info: 'M12 22a10 10 0 1 0 0-20 10 10 0 0 0 0 20zM12 16v-4M12 8h.01',
  folder: 'M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z',
  grid: 'M10 3H3v7h7V3zM21 3h-7v7h7V3zM21 14h-7v7h7v-7zM10 14H3v7h7v-7z',
}

export function Icon({ name, style, className }: { name: string; style?: CSSProperties; className?: string }) {
  return (
    <svg
      viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={2}
      strokeLinecap="round" strokeLinejoin="round" style={style} className={className}
    >
      <path d={Ic[name]} />
    </svg>
  )
}

/* ---------------- CHANNEL brand glyph (filled) ---------------- */
/** Bo'limda FAQAT bitta kanal bor — Instagram. */
export type ChannelId = 'instagram'

const ChannelGlyph: Record<ChannelId, string> = {
  instagram: 'M12 2.16c3.2 0 3.58.01 4.85.07 1.17.05 1.8.25 2.23.41.56.22.96.48 1.38.9.42.42.68.82.9 1.38.16.42.36 1.06.41 2.23.06 1.27.07 1.65.07 4.85s-.01 3.58-.07 4.85c-.05 1.17-.25 1.8-.41 2.23-.22.56-.48.96-.9 1.38-.42.42-.82.68-1.38.9-.42.16-1.06.36-2.23.41-1.27.06-1.65.07-4.85.07s-3.58-.01-4.85-.07c-1.17-.05-1.8-.25-2.23-.41a3.7 3.7 0 0 1-1.38-.9 3.7 3.7 0 0 1-.9-1.38c-.16-.42-.36-1.06-.41-2.23C2.17 15.58 2.16 15.2 2.16 12s.01-3.58.07-4.85c.05-1.17.25-1.8.41-2.23.22-.56.48-.96.9-1.38.42-.42.82-.68 1.38-.9.42-.16 1.06-.36 2.23-.41C8.42 2.17 8.8 2.16 12 2.16M12 0C8.74 0 8.33.01 7.05.07 5.78.13 4.9.33 4.14.63c-.79.31-1.46.72-2.12 1.38C1.35 2.67.94 3.34.63 4.14.33 4.9.13 5.78.07 7.05.01 8.33 0 8.74 0 12s.01 3.67.07 4.95c.06 1.27.26 2.15.56 2.91.31.8.72 1.47 1.38 2.13.66.66 1.33 1.07 2.12 1.38.76.3 1.64.5 2.91.56C8.33 23.99 8.74 24 12 24s3.67-.01 4.95-.07c1.27-.06 2.15-.26 2.91-.56.8-.31 1.47-.72 2.13-1.38.66-.66 1.07-1.33 1.38-2.13.3-.76.5-1.64.56-2.91.06-1.28.07-1.69.07-4.95s-.01-3.67-.07-4.95c-.06-1.27-.26-2.15-.56-2.91a5.9 5.9 0 0 0-1.38-2.13A5.9 5.9 0 0 0 19.86.63c-.76-.3-1.64-.5-2.91-.56C15.67.01 15.26 0 12 0zm0 5.84A6.16 6.16 0 1 0 12 18.16 6.16 6.16 0 0 0 12 5.84zM12 16a4 4 0 1 1 0-8 4 4 0 0 1 0 8zm6.41-10.85a1.44 1.44 0 1 0 0 2.88 1.44 1.44 0 0 0 0-2.88z',
}

export function ChannelIcon({ ch = 'instagram' }: { ch?: ChannelId }) {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor">
      <path d={ChannelGlyph[ch]} />
    </svg>
  )
}

/* ---------------- PAGE WRAPPER ---------------- */
/**
 * Marketing sahifa o'rovchisi — `.marketing-app` scope + YOPISHQOQ sarlavha.
 *
 * ⚠️ `full` propi OLIB TASHLANDI: sahifa DOIM to'liq kenglikda ochiladi.
 * Sabab — marketing ekranlari (navbat, galereya, jadval, composer) tor
 * ustunga sig'masdi va foydalanuvchi bir vaqtda kam ma'lumot ko'rardi.
 *
 * Sarlavha bloki sticky: uzun sahifada ham "qayerdaman va nima qila olaman"
 * ko'rinib tursin. Ostidagi kartochkalar orasidan o'tib ketmasligi uchun
 * fon TO'LIQ (blur + `--bg`) va z-index ko'tarilgan.
 */
export function MarketingPage({
  title, sub, children, actions, back, subnav,
}: {
  title: string
  sub: string
  children: ReactNode
  /** Sarlavha o'ng tomonidagi tugmalar. */
  actions?: ReactNode
  /** Orqaga havola — sub-sahifalarda ("← Navbat"). */
  back?: { to: string; label: string }
  /** Sahifa ICHIDAGI sub-sahifa tugmalari (odatda `<MkSubnav …/>`). */
  subnav?: ReactNode
}) {
  return (
    <div className="marketing-app">
      <div className="mk-shell">
        <div className="mk-head">
          <div className="mk-head-inner">
            {back && (
              <Link to={back.to} className="mk-back">
                <Icon name="arrowLeft" /> {back.label}
              </Link>
            )}
            <div className="mk-head-row">
              <div className="mk-head-titles">
                <div className="page-title">{title}</div>
                <div className="page-sub">{sub}</div>
              </div>
              {actions && <div className="mk-head-actions">{actions}</div>}
            </div>
            {subnav}
          </div>
        </div>
        {children}
      </div>
    </div>
  )
}

/* ---------------- SAHIFA ICHIDAGI SUB-NAVIGATSIYA ---------------- */

export interface MkSubnavItem {
  to: string
  label: string
  /** `Ic` xaritasidagi nom. */
  icon?: string
  /** `NavLink` `end` — aniq moslik (ildiz marshrut uchun). */
  end?: boolean
  /** O'ngdagi kichik raqam chipi; 0 yoki `undefined` bo'lsa CHIZILMAYDI. */
  count?: number
}

/**
 * Sub-sahifa tugmalari — NAV'DA EMAS, SAHIFA ICHIDA.
 *
 * Sabab: chap menyu bo'limlar uchun, sub-sahifa esa bo'lim ICHIDAGI qadam.
 * Menyuga chiqarilsa ro'yxat o'nlab qatorga cho'zilib, ierarxiya yo'qolardi.
 * Mobilda qator gorizontal skrollanadi (scrollbar yashirin).
 */
export function MkSubnav({ items }: { items: MkSubnavItem[] }) {
  return (
    <nav className="mk-subnav">
      {items.map((it) => (
        <NavLink
          key={it.to}
          to={it.to}
          end={it.end}
          className={({ isActive }) => `mk-subnav-item${isActive ? ' active' : ''}`}
        >
          {it.icon && <Icon name={it.icon} />}
          <span>{it.label}</span>
          {/* 0 ni ATAYIN chizmaymiz — bo'sh navbat yonidagi "0" shovqin, xabar emas. */}
          {!!it.count && <span className="mk-subnav-count">{it.count}</span>}
        </NavLink>
      ))}
    </nav>
  )
}

/* ---------------- FON SKROLLI QULFI ---------------- */

/**
 * Fon skrollini bloklovchi YAGONA mexanizm — GLOBAL SANAGICH bilan.
 *
 * ⚠️ Nega sanagich: ilgari har oyna `document.body.style.overflow` ning o'z `prev`
 * qiymatini ushlab turardi. Ikkita oyna ichma-ich ochilib (masalan `MkSheet` ichidan
 * tasdiq `MkDialog`i) TASHQISI oldin unmount bo'lsa, ichkisi yopilganda `prev = 'hidden'`
 * ni tiklab qo'yardi va sahifa ABADIY skrollanmay qolardi.
 * Endi asl qiymat FAQAT birinchi qulfda saqlanadi va FAQAT oxirgisi bo'shatilganda
 * tiklanadi. Qaytgan funksiya ikki marta chaqirilsa ikkinchisi e'tiborsiz qoladi
 * (React 18 StrictMode'dagi qo'sh effekt sanagichni buzmasin).
 */
let scrollLockCount = 0
let scrollLockPrev = ''

function lockBodyScroll(): () => void {
  if (scrollLockCount === 0) {
    scrollLockPrev = document.body.style.overflow
    document.body.style.overflow = 'hidden'
  }
  scrollLockCount += 1
  let released = false
  return () => {
    if (released) return
    released = true
    scrollLockCount = Math.max(0, scrollLockCount - 1)
    if (scrollLockCount === 0) document.body.style.overflow = scrollLockPrev
  }
}

/* ---------------- TO'LIQ EKRANLI OYNA ---------------- */

/**
 * TO'LIQ EKRANLI oyna — kichik modal o'rniga.
 *
 * Marketing formalarida (post yaratish, media, qoida) maydon ko'p: 560px
 * kenglikdagi modalda ular ikki-uch ekran bo'lib ketardi. Bu yerda tana
 * skrollanadi, bosh va oyoq qotib turadi — foydalanuvchi "qayerdaman" ni
 * yo'qotmaydi va amal tugmalari doim ko'rinadi.
 */
export function MkSheet({
  title, sub, icon, onClose, footer, children, dirty,
}: {
  title: string
  sub?: string
  icon?: string
  onClose: () => void
  footer?: ReactNode
  children: ReactNode
  /** true bo'lsa Esc va ✕ darhol yopmaydi — avval tasdiq so'raladi. */
  dirty?: boolean
}) {
  /**
   * ⚠️ Yopishning IKKI yo'li BIR XIL xavfsizlik darajasida bo'lishi shart.
   * Overlay bosilganda yopilish ATAYIN yo'q (to'ldirilgan forma yo'qolmasin), Esc esa
   * AYNAN o'sha ma'lumotni bir zumda o'chirib yuborardi — mantiqan ziddiyat edi.
   * Endi `dirty` bo'lsa ikkalasi ham tasdiq so'raydi.
   */
  const [askClose, setAskClose] = useState(false)
  const requestClose = () => { if (dirty) setAskClose(true); else onClose() }

  // Esc bilan yopish + fon skrollini bloklash: aks holda sahifa oyna ortida
  // siljib, yopilgandan keyin foydalanuvchi boshqa joyda turib qolardi.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key !== 'Escape') return
      // Tasdiq oynasi ochiq bo'lsa Esc AYNAN o'shanga tegishli (uni MkDialog yopadi) —
      // aks holda ikkala tinglovchi ham ishlab, tasdiqni yopib bo'lmasdi.
      if (askClose) return
      if (dirty) setAskClose(true)
      else onClose()
    }
    document.addEventListener('keydown', onKey)
    const unlock = lockBodyScroll()
    return () => {
      document.removeEventListener('keydown', onKey)
      unlock()
    }
  }, [onClose, dirty, askClose])

  // ⚠️ `.marketing-app` o'rovchisi ATAYIN takrorlangan: oyna `MarketingPage`
  // daraxtidan TASHQARIDA (fragment ichida) chizilsa ham scope'langan stillar
  // va CSS o'zgaruvchilari ishlashda davom etsin. U AYRI div — selektor
  // `.marketing-app .mk-sheet-overlay` AVLODni kutadi, bitta elementdagi
  // ikkala klass unga mos kelmasdi.
  // Tasdiq oynasi (`MkDialog`) o'z `.marketing-app` o'rovchisi bilan keladi va
  // varaqdan KEYIN chiziladi — shuning uchun butun natija fragmentga o'ralgan.
  return (
    <>
      <div className="marketing-app">
        <div className="mk-sheet-overlay" role="dialog" aria-modal="true" aria-label={title}>
          <div className="mk-sheet">
            <div className="mk-sheet-head">
              {icon && <div className="mk-sheet-ic"><Icon name={icon} /></div>}
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="mk-sheet-title">{title}</div>
                {sub && <div className="mk-sheet-sub">{sub}</div>}
              </div>
              <button className="icon-btn" onClick={requestClose} title="Yopish (Esc)" aria-label="Yopish">
                <Icon name="close" style={{ width: 18, height: 18 }} />
              </button>
            </div>
            <div className="mk-sheet-body">
              <div className="mk-sheet-inner">{children}</div>
            </div>
            {footer && (
              <div className="mk-sheet-foot">
                <div className="mk-sheet-inner mk-sheet-foot-inner">{footer}</div>
              </div>
            )}
          </div>
        </div>
      </div>
      {askClose && (
        <MkDialog
          title="O'zgarishlar saqlanmaydi. Yopilsinmi?"
          tone="danger"
          onClose={() => setAskClose(false)}
          footer={(
            <>
              <button className="btn btn-ghost" onClick={() => setAskClose(false)}>Bekor qilish</button>
              <button className="btn btn-primary" onClick={onClose}>Ha, yopilsin</button>
            </>
          )}
        >
          Kiritilgan ma'lumot saqlanmagan. Oynani yopsangiz u yo'qoladi.
        </MkDialog>
      )}
    </>
  )
}

/**
 * QISQA tasdiq oynasi (o'chirish / bekor qilish).
 *
 * ⚠️ ATAYIN kichik va markazda: "rostdan o'chiraymi?" savoli uchun to'liq
 * ekran ochish diqqatni asosiy sahifadan uzib yuborardi.
 */
export function MkDialog({
  title, tone = 'default', onClose, footer, children,
}: {
  title: string
  tone?: 'default' | 'danger'
  onClose: () => void
  footer?: ReactNode
  children: ReactNode
}) {
  // ⚠️ Fon skrolli bu yerda ham bloklanadi: o'chirish tasdig'i ochiq turganda
  // g'ildirak orqadagi ro'yxatni siljitardi va tasdiqdan keyin foydalanuvchi
  // butunlay boshqa joyda turib qolardi. Mexanizm `MkSheet` bilan BITTA (sanagichli).
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose() }
    document.addEventListener('keydown', onKey)
    const unlock = lockBodyScroll()
    return () => {
      document.removeEventListener('keydown', onKey)
      unlock()
    }
  }, [onClose])

  return (
    // `.marketing-app` o'rovchisi — MkSheet'dagi bilan bir xil sabab.
    <div className="marketing-app">
      <div className="mk-dialog-overlay" role="dialog" aria-modal="true" aria-label={title}>
        <div className={`mk-dialog${tone === 'danger' ? ' tone-danger' : ''}`}>
          <div className="mk-dialog-head">
            <div className="mk-dialog-title">{title}</div>
            <button className="icon-btn" onClick={onClose} title="Yopish (Esc)" aria-label="Yopish">
              <Icon name="close" style={{ width: 16, height: 16 }} />
            </button>
          </div>
          <div className="mk-dialog-body">{children}</div>
          {footer && <div className="mk-dialog-foot">{footer}</div>}
        </div>
      </div>
    </div>
  )
}

/* ---------------- BOSQICHLAR ---------------- */

/**
 * Sahifa ichidagi BOSQICH tugmalari (composer'ning "sub-sahifalari").
 *
 * Bosqichlar bir sahifada qoladi — sehrgar (wizard) qilib marshrutga
 * chiqarilmadi, chunki foydalanuvchi ixtiyoriy tartibda oldinga-orqaga
 * yuradi va oraliq holat yo'qolmasligi kerak.
 */
export function MkSteps({
  steps, active, onSelect, done,
}: {
  steps: { id: string; label: string; hint?: string; icon?: string }[]
  active: string
  onSelect: (id: string) => void
  /** Bajarilgan bosqichlar — ✓ belgisi bilan. */
  done?: Record<string, boolean>
}) {
  return (
    <div className="mk-steps">
      {steps.map((s, i) => {
        const isDone = !!done?.[s.id]
        const cls = `mk-step${s.id === active ? ' active' : ''}${isDone ? ' done' : ''}`
        return (
          <button key={s.id} type="button" className={cls} onClick={() => onSelect(s.id)}>
            <span className="mk-step-num">
              {isDone ? <Icon name="check" style={{ width: 14, height: 14 }} /> : i + 1}
            </span>
            <span className="mk-step-text">
              <span className="mk-step-label">{s.label}</span>
              {s.hint && <span className="mk-step-hint">{s.hint}</span>}
            </span>
          </button>
        )
      })}
    </div>
  )
}

/* ---------------- KO'RSATKICH VA XABAR ---------------- */

/** Katta ko'rsatkich kartochkasi. Konteyner: `<div className="mk-kpi">…</div>`. */
export function MkStat({
  label, value, tone = 'muted', icon, hint,
}: {
  label: string
  value: ReactNode
  tone?: 'primary' | 'success' | 'warning' | 'danger' | 'muted'
  icon?: string
  hint?: string
}) {
  return (
    <div className={`mk-stat-card tone-${tone}`}>
      {icon && <div className="mk-stat-ic"><Icon name={icon} /></div>}
      <div style={{ minWidth: 0 }}>
        <div className="mk-stat-v">{value}</div>
        <div className="mk-stat-l">{label}</div>
        {hint && <div className="mk-stat-h">{hint}</div>}
      </div>
    </div>
  )
}

/** Yopiladigan xabar chizig'i (saqlandi / xato / maslahat). */
export function MkNotice({
  text, tone = 'info', onClose,
}: {
  text: string
  tone?: 'success' | 'danger' | 'info'
  onClose?: () => void
}) {
  const icon = tone === 'success' ? 'check' : tone === 'danger' ? 'warn' : 'info'
  return (
    <div className={`mk-notice tone-${tone}`}>
      <Icon name={icon} style={{ width: 17, height: 17, flexShrink: 0 }} />
      <span style={{ flex: 1, minWidth: 0 }}>{text}</span>
      {onClose && (
        <button className="mk-notice-x" onClick={onClose} title="Yopish" aria-label="Yopish">
          <Icon name="close" style={{ width: 15, height: 15 }} />
        </button>
      )}
    </div>
  )
}

/** Bo'lim kartochkasi: sarlavha + izoh + o'ngda amallar + tana. */
export function MkCard({
  title, sub, actions, children, pad = true,
}: {
  title?: string
  sub?: string
  actions?: ReactNode
  children: ReactNode
  /** default `true` — jadval/galereya uchun `false` qilinadi. */
  pad?: boolean
}) {
  const hasHead = !!title || !!sub || !!actions
  return (
    <div className="card mk-card">
      {hasHead && (
        <div className="mk-card-head">
          <div style={{ flex: 1, minWidth: 0 }}>
            {title && <div className="mk-card-title">{title}</div>}
            {sub && <div className="mk-card-sub">{sub}</div>}
          </div>
          {actions && <div className="mk-card-acts">{actions}</div>}
        </div>
      )}
      <div className={pad ? 'mk-card-body' : 'mk-card-body no-pad'}>{children}</div>
    </div>
  )
}

/* ---------------- HOLAT BLOKCHALARI ---------------- */

/** Yuklanmoqda — har sahifada bir xil ko'rinsin. */
export function MkLoading({ text = 'Yuklanmoqda…' }: { text?: string }) {
  return <div className="mk-state">{text}</div>
}

/** Xato — sabab TO'LIQ ko'rsatiladi (jim yutilmaydi), qayta urinish tugmasi bilan. */
export function MkError({ text, onRetry }: { text: string; onRetry?: () => void }) {
  return (
    <div className="mk-state mk-state-error">
      <Icon name="warn" style={{ width: 18, height: 18, flexShrink: 0 }} />
      <span style={{ flex: 1 }}>{text}</span>
      {onRetry && (
        <button className="btn btn-outline btn-sm" onClick={onRetry}>
          <Icon name="refresh" /> Qayta urinish
        </button>
      )}
    </div>
  )
}

/** Bo'sh ro'yxat — "nima yo'q va nima qilish kerak". */
export function MkEmpty({ text, hint }: { text: string; hint?: string }) {
  return (
    <div className="mk-state">
      <div>
        <div style={{ fontWeight: 700 }}>{text}</div>
        {hint && <div className="field-hint" style={{ marginTop: 4 }}>{hint}</div>}
      </div>
    </div>
  )
}

/** "Sozlangan / sozlanmagan" ko'rinishidagi holat kartochkasi. */
export function MkStatusCard({
  label, ok, value, hint, warn,
}: {
  label: string
  ok: boolean
  value?: string
  hint?: string
  /** `true` — "yaxshi emas, lekin xato ham emas" (sariq). */
  warn?: boolean
}) {
  const color = ok ? 'var(--success)' : warn ? 'var(--warning)' : 'var(--danger)'
  const bg = ok ? 'var(--success-soft)' : warn ? 'var(--warning-soft)' : 'var(--danger-soft)'
  return (
    <div className="mk-status" style={{ borderColor: color }}>
      <div className="mk-status-dot" style={{ background: bg, color }}>
        <Icon name={ok ? 'check' : 'warn'} style={{ width: 15, height: 15 }} />
      </div>
      <div style={{ minWidth: 0 }}>
        <div className="mk-status-label">{label}</div>
        <div className="mk-status-value" style={{ color }}>{value ?? (ok ? 'Sozlangan' : 'Sozlanmagan')}</div>
        {hint && <div className="field-hint">{hint}</div>}
      </div>
    </div>
  )
}

/** Matnni buferga nusxalash tugmasi (webhook/callback manzillari uchun). */
export function MkCopyRow({ label, value, hint }: { label: string; value: string; hint?: string }) {
  const copy = () => { void navigator.clipboard?.writeText(value) }
  return (
    <div className="field">
      <label className="field-label">{label}</label>
      <div className="mk-copy">
        <code>{value || '—'}</code>
        <button className="btn btn-ghost btn-sm" onClick={copy} disabled={!value} title="Nusxa olish">
          <Icon name="copy" /> Nusxa
        </button>
      </div>
      {hint && <div className="field-hint">{hint}</div>}
    </div>
  )
}
