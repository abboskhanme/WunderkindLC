/* ============================================================
   Student cabinet — theme preference + Telegram Mini App helpers.

   Theme preference (localStorage `student_theme`):
     'light' | 'dark' — chosen manually in Settings (works in a normal browser as before);
     absent           — 'auto': follow Telegram's colorScheme inside the Mini App,
                        the system preference elsewhere.
   Inside Telegram with 'auto' the layout also borrows Telegram's surface/text colors
   (`telegramPalette`) so the cabinet blends with the chat UI; colors that fail a contrast
   check are ignored and our own palette is used instead.
   ============================================================ */

export type ThemePref = 'auto' | 'light' | 'dark'
export type ThemeMode = 'light' | 'dark'

const KEY = 'student_theme'
export const THEME_EVENT = 'student:theme'

/** Our own page background per mode — used for Telegram header/background colors. */
export const OWN_BG: Record<ThemeMode, string> = { light: '#f3f5f9', dark: '#0c111d' }
export const OWN_SURFACE: Record<ThemeMode, string> = { light: '#ffffff', dark: '#151c2c' }

export function getThemePref(): ThemePref {
  try {
    const v = localStorage.getItem(KEY)
    return v === 'light' || v === 'dark' ? v : 'auto'
  } catch {
    return 'auto'
  }
}

export function setThemePref(p: ThemePref): void {
  try {
    if (p === 'auto') localStorage.removeItem(KEY)
    else localStorage.setItem(KEY, p)
  } catch {
    /* storage blocked (private mode) — the choice lives for this page only */
  }
  window.dispatchEvent(new Event(THEME_EVENT))
}

export function resolveTheme(pref: ThemePref, tgScheme: ThemeMode | undefined, systemDark: boolean): ThemeMode {
  if (pref !== 'auto') return pref
  if (tgScheme) return tgScheme
  return systemDark ? 'dark' : 'light'
}

/**
 * The Telegram WebApp object — ONLY when the page really runs inside Telegram.
 * The SDK script is loaded in every browser (index.html); outside Telegram `initData` is empty.
 */
export function telegramApp(): TelegramWebApp | null {
  const tg = typeof window !== 'undefined' ? window.Telegram?.WebApp : undefined
  return tg && typeof tg.initData === 'string' && tg.initData.length > 0 ? tg : null
}

export function tgAtLeast(tg: TelegramWebApp | null, version: string): boolean {
  if (!tg) return false
  try {
    return typeof tg.isVersionAtLeast === 'function' ? tg.isVersionAtLeast(version) : false
  } catch {
    return false
  }
}

/** Telegram's native BackButton is usable (Bot API 6.1+). Pages then hide their own back arrow. */
export function hasTelegramBackButton(): boolean {
  const tg = telegramApp()
  return !!tg?.BackButton && tgAtLeast(tg, '6.1')
}

/* ---------- colour maths (pure) ---------- */

const HEX = /^#([0-9a-f]{6})$/i

export function isHex(v: unknown): v is string {
  return typeof v === 'string' && HEX.test(v.trim())
}

function rgb(hex: string): [number, number, number] {
  const n = parseInt(hex.trim().slice(1), 16)
  return [(n >> 16) & 255, (n >> 8) & 255, n & 255]
}

function toHex([r, g, b]: [number, number, number]): string {
  return '#' + [r, g, b].map((c) => Math.round(Math.max(0, Math.min(255, c))).toString(16).padStart(2, '0')).join('')
}

/** `a` mixed into `b` by weight `t` (0 → b, 1 → a). */
export function mix(a: string, b: string, t: number): string {
  const A = rgb(a)
  const B = rgb(b)
  return toHex([A[0] * t + B[0] * (1 - t), A[1] * t + B[1] * (1 - t), A[2] * t + B[2] * (1 - t)])
}

function luminance(hex: string): number {
  const [r, g, b] = rgb(hex).map((c) => {
    const s = c / 255
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4
  })
  return 0.2126 * r + 0.7152 * g + 0.0722 * b
}

/** WCAG contrast ratio (1..21). */
export function contrast(a: string, b: string): number {
  const la = luminance(a)
  const lb = luminance(b)
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05)
}

/**
 * CSS variable overrides borrowed from Telegram's theme. Returns null when the theme is unusable
 * (missing colors, or body text below 4.5:1 on the card surface) — the caller then keeps our palette.
 * Accent/status colors are NEVER taken from Telegram: they carry meaning (debt, attendance).
 */
export function telegramPalette(tp: TelegramThemeParams | undefined, mode: ThemeMode): Record<string, string> | null {
  if (!tp) return null
  const surface = [tp.section_bg_color, tp.bg_color].find(isHex)
  const text = isHex(tp.text_color) ? tp.text_color : null
  if (!surface || !text) return null
  if (contrast(text, surface) < 4.5) return null
  // Page background: Telegram's "secondary" background when it differs from the cards, otherwise a
  // slightly tinted card color so the cards still read as cards.
  // The page must sit BELOW the cards (darker in both modes) — some Telegram themes have a lighter
  // "secondary" colour, which would make the cards look sunken.
  let bg = isHex(tp.secondary_bg_color) ? tp.secondary_bg_color : null
  if (!bg || contrast(bg, surface) < 1.04 || luminance(bg) > luminance(surface))
    bg = mode === 'dark' ? mix('#000000', surface, 0.3) : mix(text, surface, 0.05)
  const hint = [tp.subtitle_text_color, tp.hint_color].find(isHex)
  const muted = hint && contrast(hint, surface) >= 4.5 ? hint : mix(text, surface, 0.68)
  const faint = isHex(tp.hint_color) && contrast(tp.hint_color, surface) >= 2.6 ? tp.hint_color : mix(text, surface, 0.5)
  return {
    '--bg': bg,
    '--surface': surface,
    '--surface2': mix(text, surface, 0.035),
    '--surface3': mix(text, surface, 0.075),
    '--text': text,
    '--muted': muted,
    '--faint': faint,
    '--border': mix(text, surface, 0.1),
    '--borderStrong': mix(text, surface, 0.18),
  }
}
