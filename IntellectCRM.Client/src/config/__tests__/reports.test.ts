import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'
import {
  reportGroups,
  allReports,
  navReports,
  reportPerms,
  visibleReportGroups,
} from '../reports'
import { permLabel } from '../constants'
import { navByRole, activeNavTo } from '../navigation'

/**
 * HISOBOTLAR KATALOGI — `config/reports.ts` yagona manba bo'lgani uchun undagi xato
 * BIR VAQTDA ikki joyni buzadi: yon menyudagi "Hisobotlar" guruhini ham, hub sahifasini ham.
 * Shu testlar aynan shu xatolarni ushlaydi.
 */
describe('hisobotlar katalogi', () => {
  it("har bir hisobotning marshruti App.tsx da HAQIQATAN bor", () => {
    // Yozuv xatosi (yoki marshrut o'chirilishi) hub'da "404" havola qoldirardi — uni
    // faqat qo'lda bosib ko'rib topish mumkin edi.
    const appPath = fileURLToPath(new URL('../../App.tsx', import.meta.url))
    const app = readFileSync(appPath, 'utf8')
    for (const r of allReports) {
      const path = r.to.split('?')[0].replace(/^\/admin\//, '')
      expect(app, `${r.label} → ${r.to}`).toContain(`path="${path}"`)
    }
  })

  it('ruxsat kalitlari ruxsatlar katalogida mavjud', () => {
    // `permLabel` topa olmagan kalitni O'ZINI qaytaradi — ya'ni teng bo'lsa kalit noma'lum.
    for (const r of allReports) {
      expect(permLabel(r.perm), `${r.label} → ${r.perm}`).not.toBe(r.perm)
    }
  })

  it('bir xil manzil ikki marta yozilmagan', () => {
    const seen = allReports.map((r) => r.to)
    expect(new Set(seen).size).toBe(seen.length)
  })

  it('har bir hisobotda nom va izoh bor', () => {
    // Izoh — "bu hisobot qaysi savolga javob beradi": hub'da nomning o'zi yetarli emas.
    for (const r of allReports) {
      expect(r.label.trim().length, r.to).toBeGreaterThan(0)
      expect(r.description.trim().length, r.to).toBeGreaterThan(0)
    }
  })

  it("`reportPerms` — katalogdagi barcha kalitlar, takrorsiz", () => {
    expect([...reportPerms].sort()).toEqual([...new Set(allReports.map((r) => r.perm))].sort())
  })

  it("menyuga faqat `inNav` belgilangan hisobotlar tushadi", () => {
    expect(navReports.every((r) => r.inNav)).toBe(true)
    expect(navReports.length).toBe(allReports.filter((r) => r.inNav).length)
  })

  it("yon menyudagi 'Hisobotlar' guruhi katalog bilan bir xil", () => {
    // Guruh menyuda QO'LDA yozilmasin — aks holda katalogga qo'shilgan hisobot menyuda
    // paydo bo'lmay, "yo'qolgan" bo'lib ko'rinardi.
    const group = navByRole.admin.find((i) => i.label === 'Hisobotlar')
    expect(group).toBeDefined()
    const childRoutes = (group!.children ?? []).map((c) => c.to)
    expect(childRoutes[0]).toBe('/admin/hisobotlar') // "Barcha hisobotlar" — birinchi
    for (const r of navReports) expect(childRoutes).toContain(r.to)
  })

  it("ko'chirilgan hisobotlar ESKI menyu guruhlarida qolmagan", () => {
    // "Olib kirish" chala bo'lsa band ikki joyda turardi.
    const otherRoutes = navByRole.admin
      .filter((i) => i.label !== 'Hisobotlar')
      .flatMap((i) => [i.to, ...(i.children ?? []).map((c) => c.to)])
    for (const r of navReports) {
      expect(otherRoutes, `${r.label} eski menyuda qolib ketgan`).not.toContain(r.to)
    }
  })
})

describe('visibleReportGroups — ruxsat bo\'yicha filtr', () => {
  it('ruxsati yo\'q hisobot chiqmaydi, bo\'shab qolgan guruh esa umuman chizilmaydi', () => {
    const only = 'schedule.analytics'
    const groups = visibleReportGroups((p) => p === only)
    expect(groups).toHaveLength(1)
    expect(groups[0].items.map((i) => i.perm)).toEqual([only])
  })

  it('hech qanday ruxsat bo\'lmasa bo\'sh ro\'yxat (hub o\'zi buni yozadi)', () => {
    expect(visibleReportGroups(() => false)).toEqual([])
  })

  it('to\'liq ruxsatda katalog to\'liq qaytadi', () => {
    const groups = visibleReportGroups(() => true)
    expect(groups).toHaveLength(reportGroups.length)
    expect(groups.flatMap((g) => g.items)).toHaveLength(allReports.length)
  })
})

/**
 * MENYU MOSLIGI — bitta manzil FAQAT BITTA yuqori darajadagi bandga tegishli bo'lishi kerak.
 *
 * ⚠️ Bu testlar HAQIQIY nosozlikni qulflaydi: "Hisobotlar" bo'limi paydo bo'lgach,
 * hisobot marshruti o'zining ESKI bo'limini ham ochib yuborardi (masalan
 * `/admin/subjects/analitika` — "O'quv bo'limi" ichida `/admin/subjects` bor).
 */
describe('activeNavTo — qaysi menyu bandi faol', () => {
  const admin = navByRole.admin
  const find = (label: string) => admin.find((i) => i.label === label)!.to

  it.each([
    ['/admin/subjects/analitika', 'Hisobotlar'],
    ['/admin/rooms/utilization', 'Hisobotlar'],
    ['/admin/forms/statistika', 'Hisobotlar'],
    ['/admin/marketing/analytics', 'Hisobotlar'],
    ['/admin/settings/history', 'Hisobotlar'],
    ['/admin/crm-stats', 'Hisobotlar'],
    ['/admin/hisobotlar', 'Hisobotlar'],
  ])('%s → «%s»', (path, label) => {
    expect(activeNavTo(admin, path)).toBe(find(label))
  })

  it.each([
    // Eski bo'limlar O'Z sahifalarida avvalgidek faol qoladi.
    ['/admin/subjects', "O'quv bo'limi"],
    ['/admin/rooms', "O'quv bo'limi"],
    ['/admin/marketing/inbox', 'Marketing'],
    ['/admin/settings/school', 'Sozlamalar'],
    ['/admin/students/davomat', "O'quvchilar"],
    // Moliya "Hisobotlar"ga KO'CHIRILMAGAN (ichida amal bor) — o'z bandida qoladi.
    ['/admin/finance', 'Moliya'],
  ])('%s → «%s»', (path, label) => {
    expect(activeNavTo(admin, path)).toBe(find(label))
  })

  it("menyuda yo'q manzilda hech qaysi GURUH ochilmaydi", () => {
    // Eslatma: noma'lum `/admin/...` manzili "Bosh sahifa" (`/admin`) ga prefiks bo'yicha
    // mos keladi — bu ZARARSIZ, chunki u guruh emas (yon menyuda ochiladigan narsa yo'q)
    // va yassi bandning O'Z `NavLink end` qoidasi uni yoritmaydi. Muhimi — guruh ochilmasin.
    const to = activeNavTo(admin, '/admin/bunday-sahifa-yoq')
    const groups = admin.filter((i) => i.children)
    expect(groups.some((g) => g.to === to)).toBe(false)
  })

  it("admin panelidan tashqaridagi manzilda hech narsa faol emas", () => {
    expect(activeNavTo(admin, '/login')).toBeNull()
  })
})
