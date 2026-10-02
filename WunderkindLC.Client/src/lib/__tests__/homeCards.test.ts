import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import { HOME_CARDS, STUDENT_LIST_PRESETS, cardHref, studentListPresetFromUrl } from '../homeCards'
import { permLabel } from '@/config/constants'

const app = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')

/** `/admin/students/yangi?x=1` → `students/yangi` (App.tsx dagi `path="..."` ko'rinishi). */
const routeOf = (to: string) => to.split('?')[0].replace(/^\/admin\//, '')

/**
 * BOSH SAHIFA KARTOCHKALARI — bosilganda TO'G'RI ro'yxatga olib boradimi.
 * Ilgari 7 ta kartochka umumiy `/admin/students` ga olib borardi: "Aktiv o'quvchilar 502"
 * bosilsa arxivdan boshqa hamma, "Qarzdorlar" bosilsa ham hamma chiqardi.
 */
describe('bosh sahifa kartochkalari', () => {
  it('har bir kartochkaning marshruti App.tsx da HAQIQATAN bor', () => {
    for (const c of HOME_CARDS) {
      expect(app, `${c.label} → ${c.to}`).toContain(`path="${routeOf(c.to)}"`)
    }
  })

  it("`linkPerm` maqsad marshrutning RequirePerm'i bilan bir xil", () => {
    // Farq qilsa kartochka bosiladigan bo'lib ko'rinib, "ruxsat yo'q" sahifasiga olib borardi
    // (yoki teskarisi: ochiq sahifa uchun kartochka bosilmasdi).
    for (const c of HOME_CARDS) {
      expect(app, `${c.label} → ${c.to}`).toContain(
        `path="${routeOf(c.to)}" element={<RequirePerm perm="${c.linkPerm}">`,
      )
    }
  })

  it('ruxsat kalitlari ruxsatlar katalogida mavjud', () => {
    // `permLabel` topa olmagan kalitni O'ZINI qaytaradi.
    for (const c of HOME_CARDS) {
      expect(permLabel(c.perm), `${c.label} → ${c.perm}`).not.toBe(c.perm)
      expect(permLabel(c.linkPerm), `${c.label} → ${c.linkPerm}`).not.toBe(c.linkPerm)
    }
  })

  it("aniq ro'yxati BOR kartochkalar o'sha ro'yxatga olib boradi", () => {
    const to = Object.fromEntries(HOME_CARDS.map((c) => [c.key, c.to]))
    expect(to.orders).toBe('/admin/leads?status=lead')
    expect(to.firstLesson).toBe('/admin/leads/birinchi-dars?color=upcoming')
    expect(to.newStudents).toBe('/admin/students/yangi')
    expect(to.activeStudents).toBe('/admin/students/aktiv')
    expect(to.archived).toBe('/admin/students/arxiv')
    expect(to.debtors).toBe('/admin/students?preset=debtors')
    expect(to.frozen).toBe('/admin/students?preset=frozen')
    // "Shu oy" kartochkalari — kartochka hisoblangan OY bilan (`cardHref`).
    expect(to.ordersLeft).toBe('/admin/archive?month={month}')
    expect(to.newLeft).toBe('/admin/students/ketganlar?type=new&month={month}')
    expect(to.activeLeft).toBe('/admin/students/ketganlar?type=active&month={month}')
    expect(to.firstPayments).toBe('/admin/students/birinchi-tolov?month={month}')
  })

  it("`{month}` havolada faqat OXIRGI parametr (aks holda oy noma'lum bo'lganda buzuq manzil qolardi)", () => {
    for (const c of HOME_CARDS.filter((c) => c.to.includes('{month}'))) {
      expect(c.to, c.label).toMatch(/[?&]month=\{month\}$/)
    }
  })

  it("hech bir kartochka filtrsiz umumiy o'quvchilar ro'yxatiga olib bormaydi", () => {
    // Umumiy ro'yxat saqlangan (sessionStorage) filtrlar bilan ochiladi — son kartochkaga mos kelmaydi.
    expect(HOME_CARDS.filter((c) => c.to === '/admin/students').map((c) => c.label)).toEqual([])
  })

  it('kartochkadagi har bir preset o`quvchilar sahifasida taniladi', () => {
    for (const c of HOME_CARDS.filter((c) => c.to.includes('preset='))) {
      expect(studentListPresetFromUrl(c.to.slice(c.to.indexOf('?'))), c.label).not.toBeNull()
    }
  })
})

describe('cardHref', () => {
  it("`{month}` o'rniga kartochka oyi qo'yiladi", () => {
    expect(cardHref({ to: '/admin/students/ketganlar?type=new&month={month}' }, '2026-10')).toBe(
      '/admin/students/ketganlar?type=new&month=2026-10',
    )
    expect(cardHref({ to: '/admin/archive?month={month}' }, '2026-10')).toBe('/admin/archive?month=2026-10')
  })

  it("oy hali noma'lum bo'lsa parametr olib tashlanadi (sahifa joriy oyni oladi)", () => {
    expect(cardHref({ to: '/admin/students/ketganlar?type=new&month={month}' }, undefined)).toBe(
      '/admin/students/ketganlar?type=new',
    )
    expect(cardHref({ to: '/admin/archive?month={month}' }, '')).toBe('/admin/archive')
  })

  it("`{month}` siz havola o'zgarmaydi", () => {
    expect(cardHref({ to: '/admin/students/aktiv' }, '2026-10')).toBe('/admin/students/aktiv')
  })
})

describe('studentListPresetFromUrl', () => {
  it("ma'lum qiymatlarni o'qiydi", () => {
    for (const p of STUDENT_LIST_PRESETS) expect(studentListPresetFromUrl(`?preset=${p}`)).toBe(p)
  })

  it("noma'lum yoki yo'q qiymat — null (eskirgan havola sahifani buzmaydi)", () => {
    expect(studentListPresetFromUrl('')).toBeNull()
    expect(studentListPresetFromUrl('?preset=boshqa')).toBeNull()
    expect(studentListPresetFromUrl('?tab=active')).toBeNull()
  })
})
