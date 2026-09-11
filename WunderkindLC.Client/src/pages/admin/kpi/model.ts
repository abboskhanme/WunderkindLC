import { useCallback, useEffect, useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { useAuth } from '@/context/auth-context'
import { currentMonth, todayIso } from '@/lib/month'
import { apiErrorMessage } from '@/lib/utils'
import type { BadgeTone } from '@/components/ui/Badge'
import {
  getKpiProfiles,
  type KpiProfileDto,
  type KpiRuleSetJson,
  type KpiTier,
} from '@/api/services/kpi'

/* =====================================================================================
 *  KPI bo'limining SOF MANTIQI (JSX yo'q)
 * =====================================================================================
 *  Yorliqlar, pog'ona tanlash, foiz/koeffitsient formatlari, oy qoidalari va yuklash
 *  hooklari — beshala sahifada AYNAN bir xil bo'lishi kerak.
 *
 *  ⚠️ Komponentlar ATAYIN alohida faylda (`shared.tsx`): bitta fayl ham komponent, ham
 *  konstanta eksport qilsa Vite'ning "fast refresh" qoidasi buziladi
 *  (`react-refresh/only-export-components`) — `pages/admin/tasks` bilan bir xil bo'linish.
 */

// ---------- Rollar ----------

export interface KpiRoleMeta {
  code: string
  label: string
  /** Qisqacha: bu rol NIMAGA javob beradi. */
  hint: string
}

/**
 * KPI rollari — server `KpiRuleSeed.Roles` bilan AYNAN bir xil kalitlar.
 *
 * ⚠️ `call_operator` — kelajakdagi bo'linish: qoidalari kiruvchi admin bilan bir xil
 * STRUKTURADA, lekin bonus birligi boshqa (sinovga KELGAN har bir odam uchun).
 */
export const KPI_ROLES: KpiRoleMeta[] = [
  { code: 'intake_admin', label: 'Kiruvchi admin', hint: 'Lid → sinov darsi → shartnoma' },
  { code: 'retention_admin', label: 'Chiquvchi admin', hint: 'Ushlab qolish · uzaytirish · qarz' },
  { code: 'call_operator', label: 'Call operator', hint: 'Lid → sinovga KELISH' },
]

export function roleLabel(code: string): string {
  return KPI_ROLES.find((r) => r.code === code)?.label ?? code
}

/** Kiruvchi oqim rollari (kalkulyatorda "shartnoma/konversiya" bloki shular uchun). */
export function isIntakeRole(code: string): boolean {
  return code === 'intake_admin' || code === 'call_operator'
}

// ---------- Tiket holatlari ----------

export interface KpiTicketStatusMeta {
  value: string
  label: string
  tone: BadgeTone
  /** Bu holat PUL yechadimi. */
  costs: boolean
  hint: string
}

/**
 * Tiket holatlari — server `KpiConst` bilan bir xil kalitlar.
 *
 * ⚠️ ENG MUHIM: PULGA faqat `confirmed` ta'sir qiladi. `proposed` — taklif (AI ham,
 * xodim ham qo'yishi mumkin), `disputed` — xodim e'tiroz bildirgan, `cancelled` — bekor.
 * Shuning uchun ro'yxatda ham, oynada ham "faqat tasdiqlangani pul yechadi" ochiq
 * yoziladi: aks holda xodim har taklifni jarima deb o'ylab, e'tiroz oqimi boshlanardi.
 */
export const TICKET_STATUSES: KpiTicketStatusMeta[] = [
  {
    value: 'proposed',
    label: 'Taklif qilindi',
    tone: 'amber',
    costs: false,
    hint: "Rahbar tasdiqlamaguncha pulga TA'SIR QILMAYDI",
  },
  {
    value: 'confirmed',
    label: 'Tasdiqlandi',
    tone: 'red',
    costs: true,
    hint: 'Oylikdan jarima yechiladi va koeffitsientga ham tegadi',
  },
  {
    value: 'disputed',
    label: "E'tiroz bildirildi",
    tone: 'blue',
    costs: false,
    hint: "Xodim izoh yozdi — rahbar tasdiqlaydi yoki bekor qiladi",
  },
  {
    value: 'cancelled',
    label: 'Bekor qilindi',
    tone: 'default',
    costs: false,
    hint: 'Hisobga olinmaydi',
  },
]

export function ticketStatus(value: string): KpiTicketStatusMeta {
  return TICKET_STATUSES.find((s) => s.value === value) ?? TICKET_STATUSES[0]
}

/** Tiket PUL yechadimi — faqat `confirmed`. */
export function ticketCosts(value: string): boolean {
  return ticketStatus(value).costs
}

// ---------- Cheklist holatlari ----------

export interface KpiCheckStateMeta {
  value: string
  /** Uch holatli tugmadagi belgi. */
  mark: string
  label: string
}

/** Cheklist bandining uch holati — server: `"done" | "failed" | "na"`. */
export const CHECK_STATES: KpiCheckStateMeta[] = [
  { value: 'done', mark: '✓', label: 'Bajarildi' },
  { value: 'failed', mark: '✗', label: 'Bajarilmadi' },
  { value: 'na', mark: '–', label: 'Talab qilinmadi' },
]

export function checkStateLabel(value: string): string {
  return CHECK_STATES.find((s) => s.value === value)?.label ?? 'Belgilanmagan'
}

/** Band TIZIM tomonidan belgilanganmi (qo'lda tegib bo'lmaydi). */
export function isAutoChecked(source: string, autoCheckKey: string | null): boolean {
  return source === 'auto' && !!autoCheckKey
}

/** Blokdagi bajarilish: `na` HISOBGA KIRMAYDI (talab qilinmagan band normani buzmasin). */
export function blockProgress(states: string[]): { done: number; total: number; pct: number } {
  const total = states.filter((s) => s === 'done' || s === 'failed').length
  const done = states.filter((s) => s === 'done').length
  return { done, total, pct: total === 0 ? 0 : Math.round((done / total) * 100) }
}

// ---------- Pog'ona tanlash (server `KpiRules.Coef` nusxasi) ----------

/**
 * Pog'onalar orasidan qiymatga MOS kelganini tanlaydi.
 *
 * ⚠️ Bu — serverdagi `KpiRules.TierOf` ning AYNAN nusxasi. Nusxa ATAYIN: «Qoidalar»
 * sahifasida jadval TAHRIRLANAYOTGANDA "shu qiymat qaysi pog'onaga tushadi" ni ko'rsatish
 * kerak, saqlanmagan qoidalar uchun esa serverdan so'rab bo'lmaydi. **Serverdagi qoida
 * o'zgarsa shu funksiya ham o'zgaradi** — `__tests__/kpiModel.test.ts` chegaralarni qulflaydi.
 *
 * Qoida (Excel `INDEX/MATCH(...,1)`): `from <= x` bo'lgan ENG OXIRGI qator. Qiymat birinchi
 * `from` dan kichik bo'lsa — birinchi qator (Excel `#N/A` beradi, biz eng past pog'onani olamiz).
 */
export function tierOf(tiers: KpiTier[] | null | undefined, value: number): KpiTier | null {
  if (!tiers || tiers.length === 0) return null
  const sorted = [...tiers].sort((a, b) => a.from - b.from)
  let best = sorted[0]
  for (const t of sorted) if (t.from <= value) best = t
  return best
}

/** <see cref="tierOf"/> ning koeffitsienti; pog'ona yo'q bo'lsa neytral `1`. */
export function coefOf(tiers: KpiTier[] | null | undefined, value: number): number {
  return tierOf(tiers, value)?.coef ?? 1
}

/** Pog'ona oralig'ining o'qiladigan ko'rinishi: `"30% – 36%"`. */
export function tierRange(t: KpiTier): string {
  return `${formatPercent(t.from)} – ${formatPercent(t.to)}`
}

// ---------- Formatlar ----------

/** Ulushni foizga: `0.3286` → `"32,9%"`. */
export function formatPercent(value: number, digits = 1): string {
  if (!Number.isFinite(value)) return '—'
  return `${(value * 100).toFixed(digits).replace('.', ',').replace(/,0$/, '')}%`
}

/** Koeffitsient: `1` → `"×1"`, `1.15` → `"×1,15"`. */
export function formatCoef(value: number): string {
  if (!Number.isFinite(value)) return '—'
  return `×${String(Number(value.toFixed(3))).replace('.', ',')}`
}

/** Kunlik norma raqami: `13.52` → `"13,5"`, `3` → `"3"`. */
export function formatNorm(value: number): string {
  if (!Number.isFinite(value)) return '—'
  return String(Number(value.toFixed(1))).replace('.', ',')
}

/** Koeffitsient neytraldan (1) yuqorimi/pastmi — rang tanlash uchun. */
export function coefTone(coef: number): BadgeTone {
  if (coef > 1.0001) return 'green'
  if (coef < 0.9999) return 'red'
  return 'default'
}

/**
 * Qo'ng'iroq yo'nalishi — `Call` entity'sining NATIV qiymatlari (`inbound`/`outbound`).
 *
 * ⚠️ Qisqartirilgan `in`/`out` ATAYIN ishlatilmaydi: baza qiymati to'liq, va uni yo'lda
 * qisqartirish ikki tomonni jimgina bir-biriga mos kelmaydigan qilib qo'yardi.
 */
export function callDirectionLabel(direction: string): string {
  if (direction === 'inbound') return 'Kiruvchi'
  if (direction === 'outbound') return 'Chiquvchi'
  return direction || '—'
}

/** Serverdagi `tone` matnini `Badge` rangiga o'giradi (noma'lum qiymat — neytral). */
export function signalTone(tone: string): BadgeTone {
  if (tone === 'bad') return 'red'
  if (tone === 'warn') return 'amber'
  if (tone === 'ok') return 'green'
  return 'default'
}

// ---------- Kunlik norma: reja vs fakt ----------

export interface NormStatus {
  /** Rejaning necha foizi bajarilgan (reja 0 bo'lsa — 100%: bajarishga narsa yo'q). */
  pct: number
  /** Reja BAJARILDI (fakt >= reja). */
  ok: boolean
  /** `"+2"` yoki `"−3,5"` — rejadan farq. */
  delta: string
}

/**
 * Kunlik normaning holati.
 *
 * ⚠️ `inverse` — "KAMROQ yaxshiroq" ko'rsatkichi (`KpiNormDto.inverse`): «ketma-ket 3 dars
 * kelmaganlar», «kechikkan vazifa» va h.k., ularning normasi odatda 0. Busiz bunday katak
 * fakt 0 bo'lganda "reja bajarilmadi" bo'lib qizarib turardi.
 *
 * ⚠️ `pct` ikki rejimda IKKI xil narsani bildiradi va chiziq shu sababdan rangga qarab
 * o'qiladi: oddiy normada — rejaning bajarilgan ULUSHI, teskarisida — chegaraning BAND
 * qilingan ulushi (ya'ni to'lgan chiziq YOMON). `ok` esa ikkalasida ham bir xil ma'noda.
 */
export function normStatus(fact: number, plan: number, inverse = false): NormStatus {
  if (inverse) {
    const diff = plan - fact
    const ok = fact <= plan
    return {
      // Chegara 0 bo'lsa oraliq holat yo'q: yo toza, yo buzilgan.
      pct: plan <= 0 ? (ok ? 100 : 0) : Math.min(100, Math.round((fact / plan) * 100)),
      ok,
      delta: diff === 0 ? '0' : diff > 0 ? `+${formatNorm(diff)}` : `−${formatNorm(-diff)}`,
    }
  }
  if (plan <= 0) return { pct: 100, ok: true, delta: '0' }
  const diff = fact - plan
  return {
    pct: Math.round((fact / plan) * 100),
    ok: fact >= plan,
    delta: diff === 0 ? '0' : diff > 0 ? `+${formatNorm(diff)}` : `−${formatNorm(-diff)}`,
  }
}

// ---------- Oy qoidalari (versiyalash, kpi.md §5.4) ----------

/** "yyyy-MM" ni bir oy oldinga suradi (yil chegarasidan o'tadi). */
export function nextMonth(month: string): string {
  const y = Number(month.slice(0, 4))
  const m = Number(month.slice(5, 7))
  if (!y || !m) return month
  const d = new Date(y, m, 1)
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
}

/**
 * O'ZGARISH QAYSI OYDAN kuchga kirishi mumkin.
 *
 * ⚠️ O'TGAN oy TAQIQLANADI: o'tgan oy allaqachon yopilgan yoki yopilishga tayyor, ya'ni
 * orqaga qarab oklad/qoida o'zgartirish tasdiqlangan natijani jimgina "boshqa" qilib
 * qo'yardi. Standart tanlov — KEYINGI oy (odatdagi holat: "kelasi oydan boshlab"), lekin
 * JORIY oyga ham ruxsat (xato bilan noto'g'ri kiritilgani o'sha oyning o'zida tuzatilsin).
 */
export function isEffectiveMonthAllowed(month: string, today = currentMonth()): boolean {
  return /^\d{4}-\d{2}$/.test(month) && month >= today
}

/** Effektiv oy tanlash uchun variantlar: joriy oydan boshlab `count` ta oy. */
export function effectiveMonthOptions(count = 13, today = currentMonth()): string[] {
  const out: string[] = []
  let m = today
  for (let i = 0; i < count; i++) {
    out.push(m)
    m = nextMonth(m)
  }
  return out
}

/** Oy o'tmishdami (yopish sahifasida "hali tugamagan oy" ogohlantirishi uchun). */
export function isPastMonth(month: string, today = currentMonth()): boolean {
  return month < today
}

// ---------- MANZILDAGI holat (`?user=&month=&date=`) ----------

export interface KpiParams {
  userId: string
  month: string
  date: string
  setUserId: (v: string) => void
  setMonth: (v: string) => void
  setDate: (v: string) => void
}

/**
 * Tanlangan XODIM, OY va KUN — MANZILDA (`?user=&month=&date=`).
 *
 * Sabab `useBoardParam` dagi bilan bir xil: sahifa yangilanganda tanlov yo'qolmaydi va
 * havolani ulashganda AYNAN o'sha ko'rinish ochiladi ("Aliyevning may oyi" bo'yicha
 * savol yozishmada havola bilan beriladi).
 *
 * ⚠️ Xodim BERILMAGAN bo'lsa — joriy foydalanuvchi (server ham `userId` bo'sh bo'lsa
 * shunday qiladi). Ya'ni sahifa har doim "mening raqamlarim" dan boshlanadi.
 */
export function useKpiParams(): KpiParams {
  const [params, setParams] = useSearchParams()
  const { user } = useAuth()

  const set = useCallback(
    (key: string, value: string, fallback: string) => {
      const next = new URLSearchParams(params)
      if (value && value !== fallback) next.set(key, value)
      else next.delete(key)
      setParams(next, { replace: true })
    },
    [params, setParams],
  )

  const me = user?.id ?? ''
  return {
    userId: params.get('user') ?? me,
    month: params.get('month') ?? currentMonth(),
    date: params.get('date') ?? todayIso(),
    setUserId: (v) => set('user', v, me),
    setMonth: (v) => set('month', v, ''),
    setDate: (v) => set('date', v, ''),
  }
}

// ---------- KPI xodimlari (profillar) ----------

export interface KpiStaffState {
  profiles: KpiProfileDto[]
  loading: boolean
  error: string | null
  reload: () => void
}

/**
 * KPI profili bor xodimlar — xodim tanlagichi beshala sahifada kerak.
 *
 * ⚠️ So'rov AYNAN effektda bajariladi va setState faqat javob kelgach chaqiriladi
 * (`react-hooks/set-state-in-effect`). Qayta yuklash `tick` hisoblagichi orqali.
 *
 * ⚠️ Xato YUTILMAYDI: profillar yuklanmasa xodim tanlagich bo'sh qoladi va sahifa
 * "hech kim yo'q" bo'lib ko'rinardi — sabab ekranda yozilishi kerak.
 */
export function useKpiStaff(): KpiStaffState {
  const [profiles, setProfiles] = useState<KpiProfileDto[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)

  useEffect(() => {
    let alive = true
    getKpiProfiles()
      .then((list) => {
        if (!alive) return
        setProfiles(list)
        setError(null)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setError(apiErrorMessage(err, "Xodimlar ro'yxatini yuklab bo'lmadi"))
        setLoading(false)
      })
    return () => {
      alive = false
    }
  }, [tick])

  const reload = useCallback(() => setTick((t) => t + 1), [])
  return { profiles, loading, error, reload }
}

/** Ro'yxatdan xodimni topadi (topilmasa `null` — ism o'rniga id chiqmasin). */
export function profileOf(profiles: KpiProfileDto[], userId: string): KpiProfileDto | null {
  return profiles.find((p) => p.userId === userId) ?? null
}

// ---------- §9: hal qilinmagan ziddiyat ----------

/**
 * Excel «Boshlash» varag'i Bonus A = 6 000, B = 45 000 deydi; «KPI qoidalari» varag'i va
 * BARCHA formulalar esa A = 1 000, B = 35 000 bilan hisoblaydi. Seed FORMULA qiymati bilan
 * qilingan (NORMA oyi roppa-rosa kafolatga teng chiqadi). Qaror rahbarniki — shuning uchun
 * matn «Qoidalar» sahifasida KO'RINIB turadi, hujjat ichida ko'milib qolmaydi.
 */
export const BONUS_CONFLICT_NOTE = {
  title: 'Bonus A / B qiymati — HAL QILINMAGAN ZIDDIYAT',
  body:
    "Excel «Boshlash» varag'ida Bonus A = 6 000 so'm, Bonus B = 45 000 so'm deb yozilgan. " +
    "«KPI qoidalari» varag'i va barcha hisob formulalari esa A = 1 000 so'm, B = 35 000 so'm " +
    'bilan ishlaydi — seed ham SHU qiymatlar bilan qilingan, chunki aynan ular bilan NORMA ' +
    "oyi roppa-rosa kafolat summasiga teng chiqadi.",
  action:
    "Qaror sizniki: quyidagi maydonlarni o'zgartirsangiz, yangi qiymat KEYINGI oydan kuchga " +
    "kiradi va tasdiqlangan oylar tegilmaydi \u2014 ya'ni rahbar qarorini kutib turish SHART EMAS.",
} as const

// ---------- «Qoidalar» sahifasining maydon KATALOGI ----------

/** Bitta tahrirlanadigan konstanta. */
export interface KpiRuleField {
  key: KpiNumericRuleKey
  label: string
  hint?: string
  /** `money` — so'm, `percent` — ulush (bazada 0..1, ekranda foizda), `number` — oddiy son. */
  kind: 'money' | 'percent' | 'number'
  /** Bo'sh qoldirish mumkin (`monthlyCap` — kiruvchida shift YO'Q). */
  nullable?: boolean
}

/** `KpiRuleSetJson` ning SONLI maydonlari (pog'ona jadvallari bu yerga kirmaydi). */
export type KpiNumericRuleKey = Exclude<
  keyof KpiRuleSetJson,
  'efficiency' | 'conversion' | 'retention' | 'extension'
>

export interface KpiRuleGroup {
  key: string
  label: string
  sub: string
  /** Qaysi rollarda ko'rinadi. */
  scope: 'all' | 'intake' | 'retention'
  fields: KpiRuleField[]
}

/**
 * Qoidalar to'plamining KO'RINISH katalogi.
 *
 * ⚠️ Bu FAQAT ko'rinish: hisob serverda (`KpiCalculator`). Yangi konstanta qo'shilsa u
 * `KpiRuleSetJson` ga qo'shiladi va shu yerga ham yoziladi — aks holda maydon bazada bo'lib,
 * uni UI'dan o'zgartirib bo'lmasdi (ya'ni har o'zgarish uchun deploy kerak bo'lardi).
 */
export const RULE_GROUPS: KpiRuleGroup[] = [
  {
    key: 'common',
    label: 'Umumiy',
    sub: 'Ikkala rol uchun ham amal qiladi',
    scope: 'all',
    fields: [
      { key: 'ticketFine', label: 'Tiket jarimasi', kind: 'money', hint: 'Har TASDIQLANGAN tiket uchun' },
      { key: 'guarantee', label: 'Kafolat summasi', kind: 'money', hint: 'Kafolat muddatida oylik shundan past tushmaydi' },
      {
        key: 'monthlyCap',
        label: 'Yuqori chegara',
        kind: 'money',
        nullable: true,
        hint: "Bo'sh = chegara YO'Q. Chegara summani KESMAYDI, faqat «Yopish»da ogohlantiradi",
      },
    ],
  },
  {
    key: 'intake',
    label: 'Kiruvchi oqim',
    sub: 'Shartnoma bonusi, sifat jarimasi va oylik reja',
    scope: 'intake',
    fields: [
      { key: 'contractBase', label: 'Bitta shartnoma bonusi', kind: 'money' },
      { key: 'qualityNorm', label: 'Kelgan → shartnoma normasi', kind: 'percent' },
      { key: 'qualityPenalty', label: 'Normadan past jarima', kind: 'number', hint: 'Koeffitsientga QO‘SHILADI (manfiy son)' },
      { key: 'qualitySevereBelow', label: 'Keskin past chegarasi', kind: 'percent' },
      { key: 'qualitySeverePenalty', label: 'Keskin past jarima', kind: 'number' },
      { key: 'ticketStepThreshold', label: 'Tiket qadami', kind: 'number', hint: 'Shu sondan boshlab qo‘shimcha jarima' },
      { key: 'ticketStepPenalty', label: 'Tiket qadami jarimasi', kind: 'number' },
      { key: 'monthlyContractPlan', label: 'Oylik shartnoma rejasi', kind: 'number' },
      { key: 'workDays', label: 'Oydagi ish kuni', kind: 'number', hint: 'Kunlik norma shundan hisoblanadi' },
      { key: 'leadFloor', label: 'Lid oqimi kafolati', kind: 'number', hint: 'Lid shundan kam bo‘lsa reja pasayadi' },
      { key: 'planLeadToTrial', label: 'Reja: lid → sinov', kind: 'percent' },
      { key: 'planTrialToContract', label: 'Reja: sinov → shartnoma', kind: 'percent' },
      { key: 'touchesPerLead', label: 'Bir lidga urinish', kind: 'number' },
    ],
  },
  {
    key: 'retention',
    label: 'Chiquvchi oqim',
    sub: 'Bonus A / B / C birliklari va sababsiz ketish jarimasi',
    scope: 'retention',
    fields: [
      { key: 'activeStudentBonus', label: 'Bonus A — faol o‘quvchi birligi', kind: 'money' },
      { key: 'extensionBonus', label: 'Bonus B — uzaytirish birligi', kind: 'money' },
      { key: 'debtRate', label: 'Bonus C — yig‘ilgan qarzdan ulush', kind: 'percent' },
      { key: 'debtRateNorm', label: 'Qarz yig‘ish normasi', kind: 'percent', hint: 'Shundan past bo‘lsa Bonus C YARMIGA kesiladi' },
      { key: 'unknownReasonFine', label: 'Sababsiz ketish jarimasi', kind: 'money' },
    ],
  },
  {
    key: 'unit',
    label: 'Birlik iqtisodiyoti',
    sub: '«Yopish» sahifasidagi uch sog‘lomlik indikatori shu qiymatlardan hisoblanadi',
    scope: 'all',
    fields: [
      { key: 'unitCoursePrice', label: 'Kurs narxi (oylik)', kind: 'money' },
      { key: 'unitTeacherShare', label: 'O‘qituvchi ulushi', kind: 'percent' },
      { key: 'unitExtensionMonths', label: 'Uzaytirish davomiyligi (oy)', kind: 'number' },
      { key: 'unitMaxBonusAShare', label: 'Bonus A chegarasi', kind: 'percent' },
      { key: 'unitMaxBonusBShare', label: 'Bonus B chegarasi', kind: 'percent' },
      { key: 'unitMaxSalaryShare', label: 'Umumiy oyliklar chegarasi', kind: 'percent' },
    ],
  },
]

/** Shu ROL uchun ko'rinadigan guruhlar. */
export function visibleRuleGroups(roleCode: string): KpiRuleGroup[] {
  const wanted = isIntakeRole(roleCode) ? 'intake' : 'retention'
  return RULE_GROUPS.filter((g) => g.scope === 'all' || g.scope === wanted)
}

/** Tahrirlanadigan pog'ona jadvallari. */
export interface KpiTierTableMeta {
  key: 'efficiency' | 'conversion' | 'retention' | 'extension'
  label: string
  sub: string
  scope: 'all' | 'intake' | 'retention'
}

export const TIER_TABLES: KpiTierTableMeta[] = [
  {
    key: 'efficiency',
    label: 'Samaradorlik',
    sub: 'Topshiriqlarni muddatida bajarish ulushi → koeffitsient',
    scope: 'all',
  },
  {
    key: 'conversion',
    label: 'Konversiya (lid → sinovga kelish)',
    sub: 'Oqimning birinchi bosqichi → bonus koeffitsienti',
    scope: 'intake',
  },
  {
    key: 'retention',
    label: 'Ushlab qolish (oylik KETISH foizi)',
    sub: 'Foiz qancha PAST bo‘lsa koeffitsient shuncha YUQORI',
    scope: 'retention',
  },
  {
    key: 'extension',
    label: 'Uzaytirish',
    sub: 'Kursi tugaganlarning qanchasi davom etdi',
    scope: 'retention',
  },
]

export function visibleTierTables(roleCode: string): KpiTierTableMeta[] {
  const wanted = isIntakeRole(roleCode) ? 'intake' : 'retention'
  return TIER_TABLES.filter((t) => t.scope === 'all' || t.scope === wanted)
}

/**
 * Ulushni foiz maydoniga: `0.8` → `80`.
 *
 * ⚠️ Yaxlitlash SHART: `0.8 * 100` JavaScript'da `80.00000000000001` beradi va maydonda
 * shu ko'rinishda turardi.
 */
export function toPercentInput(value: number): number {
  return Number((value * 100).toFixed(6))
}

/** Foiz maydonidan ulushga: `80` → `0.8`. */
export function fromPercentInput(value: number): number {
  return Number((value / 100).toFixed(8))
}

/**
 * Bo'sh qoidalar to'plami — server hech narsa qaytarmagan holat uchun.
 *
 * ⚠️ Qiymatlar NOL: Excel konstantalari KLIENTDA takrorlanmaydi (yagona manba —
 * serverdagi `KpiRuleSeed`). Nol bilan to'ldirilgan forma "seed qilinmagan" ekanini
 * ochiq ko'rsatadi, noto'g'ri raqamni esa jimgina taklif qilmaydi.
 */
export function emptyRules(): KpiRuleSetJson {
  return {
    ticketFine: 0,
    guarantee: 0,
    monthlyCap: null,
    efficiency: [],
    contractBase: 0,
    qualityNorm: 0,
    qualityPenalty: 0,
    qualitySevereBelow: 0,
    qualitySeverePenalty: 0,
    leadFloor: 0,
    planLeadToTrial: 0,
    planTrialToContract: 0,
    workDays: 0,
    touchesPerLead: 0,
    monthlyContractPlan: 0,
    ticketStepThreshold: 0,
    ticketStepPenalty: 0,
    conversion: [],
    activeStudentBonus: 0,
    extensionBonus: 0,
    debtRate: 0,
    debtRateNorm: 0,
    unknownReasonFine: 0,
    retention: [],
    extension: [],
    unitCoursePrice: 0,
    unitTeacherShare: 0,
    unitExtensionMonths: 0,
    unitMaxBonusAShare: 0,
    unitMaxBonusBShare: 0,
    unitMaxSalaryShare: 0,
  }
}

/**
 * Ikki qoidalar to'plami orasidagi FARQ — "nimadan nimaga o'zgardi" tarixi uchun.
 *
 * ⚠️ Pog'ona jadvallari qator-qator solishtirilmaydi: faqat "o'zgardi" deb belgilanadi.
 * Jadvaldagi har qatorni matn bilan yozish tarixni o'qib bo'lmas qilib yuborardi.
 */
export function diffRules(
  before: KpiRuleSetJson,
  after: KpiRuleSetJson,
): { label: string; from: string; to: string }[] {
  const out: { label: string; from: string; to: string }[] = []
  for (const g of RULE_GROUPS) {
    for (const f of g.fields) {
      const a = before[f.key]
      const b = after[f.key]
      if (a === b) continue
      out.push({ label: f.label, from: showRuleValue(f, a), to: showRuleValue(f, b) })
    }
  }
  for (const t of TIER_TABLES) {
    if (JSON.stringify(before[t.key]) !== JSON.stringify(after[t.key]))
      out.push({ label: `${t.label} jadvali`, from: 'eski', to: "o'zgartirildi" })
  }
  return out
}

/** Maydon qiymatining o'qiladigan ko'rinishi (tarix qatorlari uchun). */
export function showRuleValue(field: KpiRuleField, value: number | null): string {
  if (value === null || value === undefined) return "yo'q"
  if (field.kind === 'percent') return formatPercent(value)
  if (field.kind === 'money') return new Intl.NumberFormat('ru-RU').format(value)
  return String(value)
}
