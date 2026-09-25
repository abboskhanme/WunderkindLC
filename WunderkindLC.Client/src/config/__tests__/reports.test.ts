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
import { navByRole, activeNavTo, edutizimReportRoutes, edutizimDuplicateRoutes } from '../navigation'

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
    // ⚠️ `superadminOnly` hisobotlar KIRMAYDI: aks holda faqat `contacts` ruxsati bor operator
    // menyuda "Hisobotlar"ni ko'rib, ichidan hech narsa topmasdi.
    expect([...reportPerms].sort()).toEqual(
      [...new Set(allReports.filter((r) => !r.superadminOnly).map((r) => r.perm))].sort(),
    )
  })

  it("menyuga faqat `inNav` belgilangan hisobotlar tushadi", () => {
    expect(navReports.every((r) => r.inNav)).toBe(true)
    expect(navReports.length).toBe(allReports.filter((r) => r.inNav).length)
  })

  it("katalogdagi har bir menyu hisoboti yon menyuda AYNAN BIR MARTA bor", () => {
    // Menyu edutizim tartibida (docs/EDUTIZIM-PARITY.md): hisobot yo edutizimning "Hisobotlar"
    // guruhida, yo "Future → Hisobotlar" da turadi — lekin HECH QACHON yo'qolmaydi va ikki
    // joyda turmaydi. Guruh menyuda QO'LDA yozilmasin: katalogga qo'shilgan hisobot o'zi chiqadi.
    const leaves = leafRoutes(navByRole.admin)
    for (const r of navReports) {
      const n = leaves.filter((to) => to === r.to).length
      expect(n, `${r.label} menyuda bor`).toBeGreaterThan(0)
      if (!edutizimDuplicateRoutes.has(r.to)) expect(n, `${r.label} menyuda bir marta`).toBe(1)
    }
  })

  it("Future → Hisobotlar hub'dan boshlanadi va edutizim hisobotlarini takrorlamaydi", () => {
    const future = navByRole.admin.find((i) => i.label === 'Future')!
    const reports = future.children!.find((c) => c.label === 'Hisobotlar')!
    const routes = reports.children!.map((c) => c.to)
    expect(routes[0]).toBe('/admin/hisobotlar') // "Barcha hisobotlar" — birinchi
    for (const to of edutizimReportRoutes) expect(routes).not.toContain(to)
  })
})

/** Menyuning barcha BARG manzillari (guruh kalitlarisiz). */
function leafRoutes(items: { to: string; children?: { to: string; children?: unknown[] }[] }[]): string[] {
  return items.flatMap((i) =>
    i.children ? leafRoutes(i.children as { to: string; children?: { to: string }[] }[]) : [i.to],
  )
}

/**
 * EDUTIZIM MENYUSI — admin paneli edutizim.uz bilan bir xil bo'lishi kerak (xodimlar o'sha
 * tizimga o'rgangan). Bu testlar tartib va nomlarni QULFLAYDI: guruh jimgina qayta nomlansa
 * yoki joyi almashsa, xodim uni "yo'qolgan" deb qidirib yurardi.
 */
describe('edutizim menyusi', () => {
  it("guruhlar edutizimdagi TARTIBDA, eng pastda 'Future'", () => {
    expect(navByRole.admin.map((i) => i.label)).toEqual([
      'Topshiriqlar',
      'Lidlar',
      'Guruh',
      "O'quvchilar",
      "O'quv bo'limi",
      'Moliya',
      'Nazorat',
      'Boshqaruv',
      'Sotuv va marketing',
      'Hisobotlar',
      'Sozlamalar',
      'Future',
    ])
  })

  it("guruh bandining `to` si sahifa EMAS — '#' kalit (guruh bosilganda sahifa ochilmaydi)", () => {
    for (const i of navByRole.admin.filter((x) => x.children)) expect(i.to.startsWith('#')).toBe(true)
  })

  it('menyudagi har bir sahifa App.tsx da HAQIQATAN bor', () => {
    // `settings/:section` kabi parametrli marshrut ham hisoblanadi.
    const appPath = fileURLToPath(new URL('../../App.tsx', import.meta.url))
    const app = readFileSync(appPath, 'utf8')
    const patterns = [...app.matchAll(/path="([^"]+)"/g)]
      .map((m) => m[1])
      .filter((p) => p !== '*') // "topilmadi" marshruti hamma narsaga mos kelardi
      .map((p) => new RegExp('^' + p.replace(/:[^/]+/g, '[^/]+') + '$'))
    for (const to of leafRoutes(navByRole.admin)) {
      const path = to.split('?')[0].replace(/^\/admin\//, '')
      expect(patterns.some((re) => re.test(path)), to).toBe(true)
    }
  })

  it("bitta sahifa menyuda ikki marta turmaydi (edutizimning O'Z takrorlaridan tashqari)", () => {
    const leaves = leafRoutes(navByRole.admin)
    const dup = leaves.filter((to, i) => leaves.indexOf(to) !== i)
    expect(dup.every((to) => edutizimDuplicateRoutes.has(to))).toBe(true)
  })
})

describe('visibleReportGroups — ruxsat bo\'yicha filtr', () => {
  it('ruxsati yo\'q hisobot chiqmaydi, bo\'shab qolgan guruh esa umuman chizilmaydi', () => {
    const only = 'schedule.analytics'
    const groups = visibleReportGroups((p) => p === only, true)
    expect(groups).toHaveLength(1)
    expect(groups[0].items.map((i) => i.perm)).toEqual([only])
  })

  it('hech qanday ruxsat bo\'lmasa bo\'sh ro\'yxat (hub o\'zi buni yozadi)', () => {
    expect(visibleReportGroups(() => false, true)).toEqual([])
  })

  it('to\'liq ruxsatda (superadmin) katalog to\'liq qaytadi', () => {
    const groups = visibleReportGroups(() => true, true)
    expect(groups).toHaveLength(reportGroups.length)
    expect(groups.flatMap((g) => g.items)).toHaveLength(allReports.length)
  })
})

describe('superadminOnly — faqat superadmin ko\'radigan hisobotlar', () => {
  const superOnly = allReports.filter((r) => r.superadminOnly)

  it('katalogda shunday hisobot BOR (bog\'lanish hisoboti)', () => {
    // Bayroq ishlatilmay qolib ketmasin — aks holda filtr "o'lik kod" bo'lardi.
    expect(superOnly.map((r) => r.to)).toContain('/admin/students/boglanish?tab=hisobot')
  })

  it('SUPERADMIN BO\'LMAGANDA ular ro\'yxatdan tushib qoladi — ruxsati bo\'lsa ham', () => {
    // ⚠️ Aynan shu holat `perm` bilan ifodalab bo'lmaydi: `can()` admin uchun ham `true`.
    const shown = visibleReportGroups(() => true, false).flatMap((g) => g.items)
    expect(shown).toHaveLength(allReports.length - superOnly.length)
    for (const r of superOnly) expect(shown).not.toContain(r)
  })

  it('YON MENYUGA tushmaydi — Sidebar rolni bilmaydi', () => {
    // `navReports` faqat `inNav` bo'yicha quriladi va rolni ko'rmaydi; superadmin-only
    // hisobot u yerga tushsa, oddiy admin menyuda ocha olmaydigan bandni ko'rardi.
    for (const r of superOnly) expect(r.inNav).toBeFalsy()
    expect(navReports.some((r) => r.superadminOnly)).toBe(false)
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
    // edutizimning "Hisobotlar" guruhidagi sahifalar
    ['/admin/rooms/utilization', 'Hisobotlar'],
    ['/admin/crm-stats', 'Hisobotlar'],
    // edutizimda yo'q hisobotlar — "Future" ichida. Eng ANIQ moslik g'olib:
    // `/admin/subjects/analitika` ESKI "O'quv bo'limi"ni (u yerda `/admin/subjects`) ochmaydi.
    ['/admin/subjects/analitika', 'Future'],
    ['/admin/forms/statistika', 'Future'],
    ['/admin/marketing/analytics', 'Future'],
    ['/admin/settings/history', 'Future'],
    ['/admin/hisobotlar', 'Future'],
  ])('%s → «%s»', (path, label) => {
    expect(activeNavTo(admin, path)).toBe(find(label))
  })

  it.each([
    ['/admin/subjects', "O'quv bo'limi"],
    ['/admin/rooms', 'Guruh'],
    ['/admin/classes/abc', 'Guruh'],
    ['/admin/students/123', "O'quvchilar"],
    ['/admin/teachers/7', 'Boshqaruv'],
    ['/admin/boshqaruv/xodimlar', 'Boshqaruv'],
    ['/admin/boshqaruv/rollar', 'Boshqaruv'],
    ['/admin/teachers/attendance', 'Boshqaruv'],
    ['/admin/marketing/inbox', 'Future'],
    ['/admin/settings/school', 'Sozlamalar'],
    ['/admin/students/davomat', 'Nazorat'],
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
