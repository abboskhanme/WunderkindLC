import { describe, expect, it } from 'vitest'
import type { KpiTier } from '@/api/services/kpi'
import {
  KPI_ROLES,
  RULE_GROUPS,
  TICKET_STATUSES,
  TIER_TABLES,
  blockProgress,
  callDirectionLabel,
  checkStateLabel,
  coefOf,
  coefTone,
  diffRules,
  effectiveMonthOptions,
  emptyRules,
  formatCoef,
  formatNorm,
  formatPercent,
  fromPercentInput,
  isAutoChecked,
  isEffectiveMonthAllowed,
  isIntakeRole,
  isPastMonth,
  nextMonth,
  normStatus,
  profileOf,
  roleLabel,
  signalTone,
  ticketCosts,
  ticketStatus,
  tierOf,
  tierRange,
  toPercentInput,
  visibleRuleGroups,
  visibleTierTables,
} from '../model'
import type { KpiProfileDto } from '@/api/services/kpi'

/**
 * KPI bo'limining SOF MANTIQI.
 *
 * ⚠️ Bu testlarning eng muhimi — `tierOf`/`coefOf`: ular serverdagi `KpiRules.Coef` ning
 * KLIENT NUSXASI (`.claude/rules/kpi.md` §3, Excel `INDEX/MATCH(...,1)`). Chegaralar shu
 * yerda qulflanadi: serverdagi qoida o'zgarsa test qizaradi va nusxa jimgina eskirib
 * qolmaydi.
 */

/** Excel «KPI Kiruvchi Admin» dagi konversiya jadvali (lid → sinovga kelish). */
const CONVERSION: KpiTier[] = [
  { from: 0, to: 0.2499, coef: 0.5, note: 'Normadan keskin past.' },
  { from: 0.25, to: 0.2999, coef: 0.8, note: 'Minimal chegara.' },
  { from: 0.3, to: 0.3599, coef: 1.0, note: 'NORMA.' },
  { from: 0.36, to: 0.4299, coef: 1.2, note: 'Normadan yuqori.' },
  { from: 0.43, to: 1, coef: 1.4, note: "Zo'r natija." },
]

/** Excel «KPI Chiquvchi Admin» dagi ushlab qolish jadvali (oylik KETISH foizi). */
const RETENTION: KpiTier[] = [
  { from: 0, to: 0.0399, coef: 1.15 },
  { from: 0.04, to: 0.0599, coef: 1.1 },
  { from: 0.06, to: 0.0799, coef: 1.0 },
  { from: 0.08, to: 0.0999, coef: 0.8 },
  { from: 0.1, to: 0.1199, coef: 0.6 },
  { from: 0.12, to: 1, coef: 0.4 },
]

describe("tierOf / coefOf — pog'ona tanlash (server `KpiRules.Coef` nusxasi)", () => {
  it("`from <= x` bo'lgan ENG OXIRGI qatorni oladi", () => {
    expect(coefOf(CONVERSION, 0.3286)).toBe(1.0)
    expect(coefOf(CONVERSION, 0.5)).toBe(1.4)
    expect(coefOf(CONVERSION, 0.2)).toBe(0.5)
  })

  it("CHEGARADAGI qiymat KEYINGI pog'onaga tushadi (`from` KIRADI)", () => {
    // Aynan shu joyda xato qilish oson: 0.30 — "norma"ning BOSHI, oldingi pog'ona emas.
    expect(coefOf(CONVERSION, 0.3)).toBe(1.0)
    expect(coefOf(CONVERSION, 0.2999)).toBe(0.8)
    expect(coefOf(CONVERSION, 0.36)).toBe(1.2)
    expect(coefOf(CONVERSION, 0.43)).toBe(1.4)
  })

  it("`to` va keyingi `from` ORASIDAGI tirqishda pastki pog'ona qoladi", () => {
    // Excel jadvalida 0.2999 va 0.30 orasida "teshik" bor; MATCH(...,1) pastdagini oladi.
    expect(coefOf(CONVERSION, 0.29995)).toBe(0.8)
  })

  it("birinchi `from` dan PAST qiymat eng past pog'onani oladi (Excel #N/A o'rniga)", () => {
    // ⚠️ Manfiy/nol konversiya butun hisobni xatoga tushirmasligi kerak.
    expect(coefOf(CONVERSION, -1)).toBe(0.5)
    expect(coefOf(CONVERSION, 0)).toBe(0.5)
  })

  it("bo'sh yoki berilmagan jadval — NEYTRAL koeffitsient (1)", () => {
    expect(coefOf([], 0.5)).toBe(1)
    expect(coefOf(null, 0.5)).toBe(1)
    expect(coefOf(undefined, 0.5)).toBe(1)
    expect(tierOf([], 0.5)).toBeNull()
  })

  it("jadval TARTIBSIZ berilsa ham to'g'ri tanlaydi", () => {
    // Muharrirda qator qo'shilganda tartib buzilishi mumkin — natija o'zgarmasligi kerak.
    const shuffled = [CONVERSION[3], CONVERSION[0], CONVERSION[4], CONVERSION[1], CONVERSION[2]]
    expect(coefOf(shuffled, 0.3286)).toBe(1.0)
    expect(coefOf(shuffled, 0.44)).toBe(1.4)
  })

  it("KETISH foizi jadvali: past foiz — YUQORI koeffitsient", () => {
    expect(coefOf(RETENTION, 0.0688)).toBe(1.0) // NORMA ssenariysi (6,875%)
    expect(coefOf(RETENTION, 0.13125)).toBe(0.4) // "Zaif oy"
    expect(coefOf(RETENTION, 0.05231)).toBe(1.1) // "Yaxshi oy"
    expect(coefOf(RETENTION, 0.12)).toBe(0.4) // aniq chegara
  })

  it("tanlangan pog'onaning IZOHI ham qaytadi (xodim shu matnni ko'radi)", () => {
    expect(tierOf(CONVERSION, 0.33)?.note).toBe('NORMA.')
  })

  it("`tierRange` — oraliqning o'qiladigan ko'rinishi", () => {
    expect(tierRange(CONVERSION[2])).toBe('30% – 36%')
  })
})

describe('formatlar', () => {
  it('foiz — vergul bilan, ortiqcha nol tashlanadi', () => {
    expect(formatPercent(0.3286)).toBe('32,9%')
    expect(formatPercent(0.8)).toBe('80%')
    expect(formatPercent(0)).toBe('0%')
    expect(formatPercent(1)).toBe('100%')
  })

  it("foizda noto'g'ri son tire beradi (NaN ekranga chiqmaydi)", () => {
    expect(formatPercent(Number.NaN)).toBe('—')
    expect(formatCoef(Number.NaN)).toBe('—')
    expect(formatNorm(Number.NaN)).toBe('—')
  })

  it('koeffitsient — × belgisi bilan', () => {
    expect(formatCoef(1)).toBe('×1')
    expect(formatCoef(1.15)).toBe('×1,15')
    expect(formatCoef(0.75)).toBe('×0,75')
  })

  it('kunlik norma — bitta kasr xona', () => {
    expect(formatNorm(13.52)).toBe('13,5')
    expect(formatNorm(3)).toBe('3')
  })

  it("koeffitsient rangi: 1 dan yuqori — yashil, past — qizil, roppa-rosa 1 — betaraf", () => {
    expect(coefTone(1.2)).toBe('green')
    expect(coefTone(0.8)).toBe('red')
    expect(coefTone(1)).toBe('default')
  })

  it("serverdagi signal `tone` matni Badge rangiga o'giriladi", () => {
    expect(signalTone('bad')).toBe('red')
    expect(signalTone('warn')).toBe('amber')
    expect(signalTone('ok')).toBe('green')
    expect(signalTone('nomalum')).toBe('default')
  })
})

describe('foiz maydonlari — ekranda foiz, bazada ulush', () => {
  it("0.8 → 80 va orqaga, suzuvchi nuqta 'quyrug'i' YO'Q", () => {
    // ⚠️ `0.8 * 100` JavaScript'da 80.00000000000001 beradi — maydonda shu ko'rinardi.
    expect(toPercentInput(0.8)).toBe(80)
    expect(toPercentInput(0.0399)).toBe(3.99)
    expect(fromPercentInput(80)).toBe(0.8)
    expect(fromPercentInput(3.99)).toBe(0.0399)
  })

  it("aylanma o'zgarish qiymatni BUZMAYDI", () => {
    for (const v of [0, 0.01, 0.0399, 0.32, 0.55, 0.95, 1]) {
      expect(fromPercentInput(toPercentInput(v))).toBeCloseTo(v, 8)
    }
  })
})

describe('normStatus — kunlik reja va fakt (`KpiNormDto.plan` HAQIQIY son)', () => {
  it('reja bajarilganda "ok", farq esa ishorali', () => {
    expect(normStatus(5, 3.46)).toMatchObject({ ok: true, delta: '+1,5' })
    expect(normStatus(2, 3.46).ok).toBe(false)
    expect(normStatus(2, 3.46).delta).toBe('−1,5')
  })

  it("oddiy normada `pct` — rejaning BAJARILGAN ulushi", () => {
    expect(normStatus(2, 4).pct).toBe(50)
    expect(normStatus(4, 4)).toMatchObject({ ok: true, pct: 100, delta: '0' })
  })

  it('reja 0 bo\'lsa BAJARILGAN hisoblanadi (bajarishga narsa yo\'q)', () => {
    expect(normStatus(0, 0)).toMatchObject({ ok: true, pct: 100 })
  })

  it("TESKARI ko'rsatkichda kamroq — yaxshiroq ('ketma-ket 3 dars kelmaganlar', norma 0)", () => {
    // ⚠️ Busiz bunday katak fakt 0 bo'lganda "reja bajarilmadi" bo'lib qizarib turardi.
    expect(normStatus(0, 0, true).ok).toBe(true)
    expect(normStatus(2, 0, true).ok).toBe(false)
    expect(normStatus(2, 0, true).delta).toBe('−2')
  })

  it("TESKARIDA chegara 0 bo'lsa oraliq holat YO'Q: yo toza (100), yo buzilgan (0)", () => {
    expect(normStatus(0, 0, true).pct).toBe(100)
    expect(normStatus(1, 0, true).pct).toBe(0)
  })

  it("TESKARIDA chegara > 0 bo'lsa `pct` — chegaraning BAND qilingan ulushi", () => {
    // Ya'ni to'lgan chiziq YOMON; rang baribir `ok` bo'yicha beriladi.
    expect(normStatus(2, 5, true)).toMatchObject({ ok: true, pct: 40, delta: '+3' })
    expect(normStatus(5, 5, true)).toMatchObject({ ok: true, pct: 100, delta: '0' })
    // Chegaradan oshgani chiziqni 100% dan OSHIRMAYDI (bar konteynerdan chiqib ketmasin).
    expect(normStatus(9, 5, true)).toMatchObject({ ok: false, pct: 100, delta: '−4' })
  })
})

describe("qo'ng'iroq yo'nalishi", () => {
  it("`Call` entity'sining NATIV qiymatlari tarjima qilinadi", () => {
    // ⚠️ Qisqartirilgan `in`/`out` ATAYIN ishlatilmaydi — ikki tomon mos kelmay qolardi.
    expect(callDirectionLabel('inbound')).toBe('Kiruvchi')
    expect(callDirectionLabel('outbound')).toBe('Chiquvchi')
  })

  it("noma'lum qiymat O'ZI qaytadi, bo'sh qiymat — tire", () => {
    expect(callDirectionLabel('internal')).toBe('internal')
    expect(callDirectionLabel('')).toBe('—')
  })
})

describe('cheklist', () => {
  it("`na` bandi bajarilish foiziga KIRMAYDI", () => {
    // "Talab qilinmadi" bandi normani buzmasligi kerak — aks holda xodim uni belgilamay
    // qo'yishga undalardi.
    expect(blockProgress(['done', 'done', 'na'])).toEqual({ done: 2, total: 2, pct: 100 })
    expect(blockProgress(['done', 'failed'])).toEqual({ done: 1, total: 2, pct: 50 })
  })

  it("hamma band `na` bo'lsa foiz 0, lekin jami ham 0 (bo'linish xatosi yo'q)", () => {
    expect(blockProgress(['na', 'na'])).toEqual({ done: 0, total: 0, pct: 0 })
  })

  it("TIZIM belgilagan band — `source == 'auto'` VA kalit bor bo'lganda", () => {
    expect(isAutoChecked('auto', 'kpi.today_numbers')).toBe(true)
    expect(isAutoChecked('manual', 'kpi.today_numbers')).toBe(false)
    // Kalit yo'q bo'lsa band avtomatlashtirilmagan — qo'lda belgilanadi.
    expect(isAutoChecked('auto', null)).toBe(false)
  })

  it('holat yorliqlari', () => {
    expect(checkStateLabel('done')).toBe('Bajarildi')
    expect(checkStateLabel('failed')).toBe('Bajarilmadi')
    expect(checkStateLabel('na')).toBe('Talab qilinmadi')
    expect(checkStateLabel('')).toBe('Belgilanmagan')
  })
})

describe('tiket holatlari', () => {
  it("PUL faqat `confirmed` da yechiladi", () => {
    // ⚠️ Modulning eng ko'p yanglishtiradigan joyi: taklif — hali jarima EMAS.
    expect(ticketCosts('confirmed')).toBe(true)
    expect(ticketCosts('proposed')).toBe(false)
    expect(ticketCosts('disputed')).toBe(false)
    expect(ticketCosts('cancelled')).toBe(false)
  })

  it("noma'lum holat `proposed` ga tushadi (ro'yxat buzilmasin)", () => {
    expect(ticketStatus('nomalum').value).toBe('proposed')
  })

  it("katalogda AYNAN to'rt holat bor va faqat bittasi pul yechadi", () => {
    expect(TICKET_STATUSES).toHaveLength(4)
    expect(TICKET_STATUSES.filter((s) => s.costs)).toHaveLength(1)
  })
})

describe('rollar', () => {
  it('uchta rol va ularning yorliqlari', () => {
    expect(KPI_ROLES.map((r) => r.code)).toEqual([
      'intake_admin',
      'retention_admin',
      'call_operator',
    ])
    expect(roleLabel('retention_admin')).toBe('Chiquvchi admin')
  })

  it("noma'lum rol kodi O'ZI qaytadi (yorliq jimgina yo'qolmasin)", () => {
    expect(roleLabel('nomalum_rol')).toBe('nomalum_rol')
  })

  it("call operator KIRUVCHI oqim qoidalari bilan ishlaydi", () => {
    expect(isIntakeRole('intake_admin')).toBe(true)
    expect(isIntakeRole('call_operator')).toBe(true)
    expect(isIntakeRole('retention_admin')).toBe(false)
  })
})

describe("oy qoidalari — `EffectiveFrom` versiyalash (kpi.md §5)", () => {
  it("`nextMonth` yil chegarasidan o'tadi", () => {
    expect(nextMonth('2026-01')).toBe('2026-02')
    expect(nextMonth('2026-12')).toBe('2027-01')
  })

  it("O'TGAN oy TAQIQLANGAN, joriy va kelasi oy — mumkin", () => {
    // ⚠️ Yopilgan oy o'z versiyasi bilan muzlatilgan; orqadan qayta yozish tasdiqlangan
    // natijani jimgina boshqa qilib qo'yardi.
    expect(isEffectiveMonthAllowed('2026-08', '2026-09')).toBe(false)
    expect(isEffectiveMonthAllowed('2026-09', '2026-09')).toBe(true)
    expect(isEffectiveMonthAllowed('2026-10', '2026-09')).toBe(true)
  })

  it("buzuq oy qiymati rad etiladi", () => {
    expect(isEffectiveMonthAllowed('2026-9', '2026-09')).toBe(false)
    expect(isEffectiveMonthAllowed('', '2026-09')).toBe(false)
  })

  it("variantlar JORIY oydan boshlanadi va o'tgan oy ro'yxatda YO'Q", () => {
    const opts = effectiveMonthOptions(3, '2026-11')
    expect(opts).toEqual(['2026-11', '2026-12', '2027-01'])
    expect(opts.every((m) => isEffectiveMonthAllowed(m, '2026-11'))).toBe(true)
  })

  it('`isPastMonth` — yopilgan oyni ajratadi', () => {
    expect(isPastMonth('2026-08', '2026-09')).toBe(true)
    expect(isPastMonth('2026-09', '2026-09')).toBe(false)
  })
})

describe("qoidalar katalogi", () => {
  it("har bir maydon `KpiRuleSetJson` da HAQIQATAN bor", () => {
    // Yozuv xatosi maydonni jimgina "har doim bo'sh" qilib qo'yardi.
    const empty = emptyRules()
    for (const g of RULE_GROUPS)
      for (const f of g.fields) expect(Object.keys(empty), f.key).toContain(f.key)
    for (const t of TIER_TABLES) expect(Object.keys(empty)).toContain(t.key)
  })

  it('bir maydon ikki guruhda takrorlanmagan', () => {
    const keys = RULE_GROUPS.flatMap((g) => g.fields.map((f) => f.key))
    expect(new Set(keys).size).toBe(keys.length)
  })

  it("KIRUVCHI rolda chiquvchi maydonlari KO'RINMAYDI (va aksincha)", () => {
    const intake = visibleRuleGroups('intake_admin').map((g) => g.key)
    expect(intake).toContain('intake')
    expect(intake).not.toContain('retention')

    const retention = visibleRuleGroups('retention_admin').map((g) => g.key)
    expect(retention).toContain('retention')
    expect(retention).not.toContain('intake')

    // Umumiy va birlik iqtisodiyoti — ikkalasida ham.
    expect(intake).toContain('common')
    expect(retention).toContain('unit')
  })

  it("pog'ona jadvallari ham ROL bo'yicha filtrlanadi", () => {
    expect(visibleTierTables('intake_admin').map((t) => t.key)).toEqual([
      'efficiency',
      'conversion',
    ])
    expect(visibleTierTables('retention_admin').map((t) => t.key)).toEqual([
      'efficiency',
      'retention',
      'extension',
    ])
    // Call operator — kiruvchi bilan bir xil struktura.
    expect(visibleTierTables('call_operator').map((t) => t.key)).toEqual([
      'efficiency',
      'conversion',
    ])
  })

  it("bo'sh to'plamda `monthlyCap` NULL (chegara yo'q), qolgani nol", () => {
    const empty = emptyRules()
    expect(empty.monthlyCap).toBeNull()
    expect(empty.guarantee).toBe(0)
    expect(empty.conversion).toEqual([])
  })
})

describe('diffRules — "nimadan nimaga"', () => {
  it("o'zgarmagan to'plamda farq YO'Q", () => {
    expect(diffRules(emptyRules(), emptyRules())).toEqual([])
  })

  it('sonli maydon farqi yorliq bilan chiqadi', () => {
    const before = emptyRules()
    const after = { ...before, ticketFine: 50000 }
    const diff = diffRules(before, after)
    expect(diff).toHaveLength(1)
    expect(diff[0].label).toBe('Tiket jarimasi')
    expect(diff[0].from).toBe('0')
    // ⚠️ `Intl.NumberFormat('ru-RU')` UZILMAYDIGAN bo'shliq (U+00A0) qo'yadi — qiymatni
    // oddiy bo'shliqli satr bilan solishtirish bu yerda YOLG'ON xato berardi.
    expect(diff[0].to.replace(/\s/g, ' ')).toBe('50 000')
  })

  it("foizli maydon farqi FOIZDA ko'rsatiladi", () => {
    const before = emptyRules()
    const after = { ...before, qualityNorm: 0.8 }
    expect(diffRules(before, after)[0]).toMatchObject({ from: '0%', to: '80%' })
  })

  it("pog'ona jadvali qator-qator emas, BUTUNLIGICHA \"o'zgartirildi\" deb belgilanadi", () => {
    // Har qatorni matn bilan yozish tarixni o'qib bo'lmas qilib yuborardi.
    const before = emptyRules()
    const after = { ...before, conversion: CONVERSION }
    const diff = diffRules(before, after)
    expect(diff).toHaveLength(1)
    expect(diff[0].label).toContain('jadvali')
  })
})

describe('profileOf', () => {
  const p = (userId: string): KpiProfileDto => ({
    id: `id-${userId}`,
    userId,
    userName: `Xodim ${userId}`,
    position: '',
    roleCode: 'intake_admin',
    roleLabel: 'Kiruvchi admin',
    startMonth: '2026-09',
    guaranteeUntilMonth: null,
    isActive: true,
    note: null,
    currentSalary: 2_500_000,
    salaries: [],
  })

  it('topilgan xodimni qaytaradi', () => {
    expect(profileOf([p('a'), p('b')], 'b')?.userName).toBe('Xodim b')
  })

  it("topilmasa NULL — ism o'rniga id ekranga chiqmasin", () => {
    expect(profileOf([p('a')], 'yoq')).toBeNull()
    expect(profileOf([], '')).toBeNull()
  })
})
