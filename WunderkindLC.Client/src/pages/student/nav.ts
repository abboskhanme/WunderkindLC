/* ============================================================
   Student cabinet — information architecture (single source).

   Bottom navigation (5 tabs):
     Bosh sahifa · Darslar · Natijalar · To'lovlar · Profil
   «Darslar» and «Natijalar» are groups of sibling screens switched by a segmented control
   under the page header (SECTIONS). Every other screen is a sub-page with a back button
   (and Telegram's native BackButton inside the Mini App) — BACK_FALLBACK says where "back"
   goes when the page was opened directly (deep link, no in-app history).
   Routes themselves are unchanged (App.tsx); this file only groups them.
   ============================================================ */

export type TabKey = 'home' | 'lessons' | 'results' | 'finance' | 'profile'

export interface NavTab {
  key: TabKey
  to: string
  label: string
}

export const TABS: NavTab[] = [
  { key: 'home', to: '/student', label: 'Bosh sahifa' },
  { key: 'lessons', to: '/student/schedule', label: 'Darslar' },
  { key: 'results', to: '/student/rating', label: 'Natijalar' },
  { key: 'finance', to: '/student/finance', label: "To'lovlar" },
  { key: 'profile', to: '/student/profile', label: 'Profil' },
]

export type SectionKey = 'lessons' | 'results'

export interface SectionItem {
  to: string
  label: string
}

export const SECTIONS: Record<SectionKey, { title: string; items: SectionItem[] }> = {
  lessons: {
    title: 'Darslar',
    items: [
      { to: '/student/schedule', label: 'Jadval' },
      { to: '/student/attendance', label: 'Davomat' },
      { to: '/student/progress', label: 'Dastur' },
    ],
  },
  results: {
    title: 'Natijalar',
    items: [
      { to: '/student/rating', label: 'Reyting' },
      { to: '/student/tests', label: 'Testlar' },
      { to: '/student/grades', label: 'Baholar' },
      { to: '/student/statistics', label: 'Statistika' },
    ],
  },
}

/** Sub-pages → where "back" leads when there is no in-app history. */
const BACK_FALLBACK: [RegExp, string][] = [
  [/^\/student\/lesson\/[^/]+\/?$/, '/student/progress'],
  [/^\/student\/progress\/subject\/[^/]+\/?$/, '/student/progress'],
  [/^\/student\/grading\/?$/, '/student/statistics'],
  [/^\/student\/account\/?$/, '/student/settings'],
  [/^\/student\/(settings|certificates|contracts|feedback|support|location|ai-check)\/?$/, '/student/profile'],
]

const trim = (p: string) => (p.length > 1 ? p.replace(/\/+$/, '') : p)

/** Bottom-nav tab a path belongs to. */
export function tabOf(pathname: string): TabKey {
  const p = trim(pathname)
  if (p === '/student') return 'home'
  if (/^\/student\/(schedule|attendance|progress|lesson)(\/|$)/.test(p)) return 'lessons'
  if (/^\/student\/(rating|tests|grades|statistics|grading)(\/|$)/.test(p)) return 'results'
  if (/^\/student\/finance(\/|$)/.test(p)) return 'finance'
  return 'profile'
}

/** Parent route for a sub-page, or null for top-level screens (tabs and their sections). */
export function backFallback(pathname: string): string | null {
  const p = trim(pathname)
  for (const [re, to] of BACK_FALLBACK) if (re.test(p)) return to
  return null
}

/** Active section item for a path (exact screen match). */
export function isSectionActive(item: SectionItem, pathname: string): boolean {
  return trim(pathname) === item.to
}
