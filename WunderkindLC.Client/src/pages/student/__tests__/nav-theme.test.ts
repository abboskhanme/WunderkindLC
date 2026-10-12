import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { SECTIONS, TABS, backFallback, tabOf } from '../nav'
import { contrast, mix, resolveTheme, telegramPalette } from '../theme'

/** Cabinet navigation (5 tabs, every screen reachable) and theme (Telegram) rules. */

const appSource = readFileSync(fileURLToPath(new URL('../../../App.tsx', import.meta.url)), 'utf8')
const studentRoutes = [...appSource.slice(appSource.indexOf('path="/student"')).matchAll(/<Route (?:index|path="([^"]+)")/g)]
  .slice(0, 40)
  .map((m) => (m[1] ? `/student/${m[1]}` : '/student'))

describe('nav', () => {
  it("5 ta tab, o'zbekcha yorliqlar", () => {
    expect(TABS.map((t) => t.label)).toEqual(['Bosh sahifa', 'Darslar', 'Natijalar', "To'lovlar", 'Profil'])
  })

  it("har bir tab va bo'lim manzili App.tsx da BOR", () => {
    const targets = [...TABS.map((t) => t.to), ...Object.values(SECTIONS).flatMap((s) => s.items.map((i) => i.to))]
    for (const t of targets) expect(studentRoutes, t).toContain(t)
  })

  it("har bir kabinet ekrani biror tabga tegishli (hech biri yo'qolmaydi)", () => {
    expect(tabOf('/student')).toBe('home')
    expect(tabOf('/student/schedule')).toBe('lessons')
    expect(tabOf('/student/attendance')).toBe('lessons')
    expect(tabOf('/student/progress')).toBe('lessons')
    expect(tabOf('/student/lesson/abc')).toBe('lessons')
    expect(tabOf('/student/rating')).toBe('results')
    expect(tabOf('/student/tests')).toBe('results')
    expect(tabOf('/student/grades')).toBe('results')
    expect(tabOf('/student/statistics')).toBe('results')
    expect(tabOf('/student/grading')).toBe('results')
    expect(tabOf('/student/finance')).toBe('finance')
    for (const p of ['/student/profile', '/student/settings', '/student/certificates', '/student/contracts', '/student/support']) {
      expect(tabOf(p)).toBe('profile')
    }
  })

  it("ichki sahifalarda orqaga yo'l bor, tab/bo'lim ekranlarida yo'q", () => {
    expect(backFallback('/student/account')).toBe('/student/settings')
    expect(backFallback('/student/settings')).toBe('/student/profile')
    expect(backFallback('/student/lesson/x1')).toBe('/student/progress')
    expect(backFallback('/student/progress/subject/s1')).toBe('/student/progress')
    expect(backFallback('/student/grading')).toBe('/student/statistics')
    for (const p of ['/student', '/student/schedule', '/student/rating', '/student/finance', '/student/profile']) {
      expect(backFallback(p)).toBeNull()
    }
  })
})

describe('theme', () => {
  it("qo'lda tanlov USTUN; avto — Telegram, so'ng tizim", () => {
    expect(resolveTheme('dark', 'light', false)).toBe('dark')
    expect(resolveTheme('light', 'dark', true)).toBe('light')
    expect(resolveTheme('auto', 'dark', false)).toBe('dark')
    expect(resolveTheme('auto', undefined, true)).toBe('dark')
    expect(resolveTheme('auto', undefined, false)).toBe('light')
  })

  it('Telegram ranglari — sahifa kartadan qorong\'iroq, matn kontrasti tekshiriladi', () => {
    const p = telegramPalette(
      { bg_color: '#17212b', secondary_bg_color: '#232e3c', text_color: '#f5f5f5', hint_color: '#708499', section_bg_color: '#17212b' },
      'dark',
    )!
    expect(p['--surface']).toBe('#17212b')
    expect(p['--text']).toBe('#f5f5f5')
    // secondary is LIGHTER than the cards here → replaced by a darker shade
    expect(contrast(p['--bg'], '#000000')).toBeLessThan(contrast('#17212b', '#000000'))
    expect(contrast(p['--muted'], p['--surface'])).toBeGreaterThanOrEqual(4.5)
  })

  it("o'qib bo'lmaydigan / chala mavzu — bizning palitra qoladi", () => {
    expect(telegramPalette(undefined, 'light')).toBeNull()
    expect(telegramPalette({ bg_color: '#ffffff' }, 'light')).toBeNull()
    expect(telegramPalette({ bg_color: '#ffffff', text_color: '#eeeeee' }, 'light')).toBeNull()
    expect(telegramPalette({ bg_color: 'red', text_color: '#000000' }, 'light')).toBeNull()
  })

  it('rang aralashtirish va kontrast', () => {
    expect(mix('#000000', '#ffffff', 0.5)).toBe('#808080')
    expect(Math.round(contrast('#000000', '#ffffff'))).toBe(21)
  })
})
