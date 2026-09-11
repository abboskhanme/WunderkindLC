import { api } from '../client'

/**
 * MARKETING → REKLAMA STATISTIKASI (Meta Ads Insights) — admin API klienti
 * (`/api/admin/instagram/adsstats`).
 *
 * Modul "reklamaga qancha sarfladik → nechta lid keldi → nechtasi o'quvchi bo'ldi →
 * qancha pul to'ladi" zanjirini ko'rsatadi. Ads Manager bu zanjirning FAQAT birinchi
 * yarmini biladi, CRM esa ikkinchisini.
 *
 * ⚠️ System User tokeni HECH QACHON javobga tushmaydi — faqat `tokenSet` bayrog'i.
 *
 * ⚠️ PUL HAR DOIM `*Minor` (tiyin/sent) — formatlash UI'da (`formatAdsMoney`).
 *    Valyuta va `currencyOffset` butun hisobot uchun BITTA qiymat.
 *
 * ⚠️ `cplMinor`, `cacMinor`, `roi` — `null` BO'LISHI MUMKIN (bo'luvchi nol). Ular nol bilan
 *    ALMASHTIRILMAYDI: "0 so'mga lid" bilan "hisoblab bo'lmadi" bir xil ko'rinib qolardi.
 */

// ═══════════════════════════════════════════════ TIPLAR

/** Platforma filtri. `all` — hammasi (kesimda esa "ajratilmagan" degani, pastga qarang). */
export type IgAdsPlatform = 'all' | 'instagram' | 'facebook'

/** Daraxt tuguni darajasi. `total` — jamlanma qatori (haqiqiy Meta tuguni EMAS). */
export type IgRoiLevel = 'campaign' | 'adset' | 'ad' | 'total'

/** Bo'sh qiymatlarni tashlab, faqat to'ldirilgan filtrlarni yuboradi. */
function clean(params: object): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  )
}

/**
 * DIAGNOSTIKA — "nega statistika yo'q" savolining barcha sabablari bitta javobda.
 * Ekran ochilganda birinchi so'raladigan endpoint shu.
 */
export interface IgAdsStatus {
  /** Modulning o'zi yoqilganmi (`CenterMeta.InstagramAdsStatsEnabled`). */
  enabled: boolean
  /** Reklama akkaunti ulanganmi. */
  connected: boolean
  /** `act_1234567890` — PREFIKS bilan. */
  adAccountId: string
  name: string
  /** Akkaunt valyutasi ISO kodi ("USD", "UZS"). Ulanmagan bo'lsa bo'sh. */
  currency: string
  /** Kasr xonalari soni: `major = minor / 10^offset`. */
  currencyOffset: number
  /** Statistika kunlari AYNAN shu zonada kesiladi (markaz vaqtidan farq qilishi mumkin). */
  timezoneName: string
  /** ⚠️ Token QIYMATI EMAS — faqat "sozlangan/sozlanmagan". */
  tokenSet: boolean
  connectedAt: string
  connectedBy: string
  lastSyncAt: string
  lastError: string
  /** Avtomatik sinxronizatsiya soati (markaz vaqti). */
  syncHour: number
  /** Birinchi ulanishda necha kunlik tarix yuklanadi. */
  backfillDays: number
  /** Bazadagi kunlik statistika qatorlari soni (shu akkaunt bo'yicha). */
  insightRows: number
  /** Kampaniya/adset/e'lon yozuvlari soni. */
  entityRows: number
  /** Eng oxirgi statistika kuni ("yyyy-MM-dd") — "ulangan, lekin ma'lumot yo'q" holatini ko'rsatadi. */
  lastStatDate: string
}

/**
 * Hisobotning BITTA qatori — kampaniya, adset yoki e'lon (ichma-ich `children`).
 */
export interface IgRoiNode {
  level: IgRoiLevel
  /** Meta'dagi id (jamlanma qatorda bo'sh). */
  id: string
  /** Nomi; sinxronlanmagan tugunda — id'ning O'ZI (sun'iy "Noma'lum" yozilmaydi). */
  name: string
  status: string
  spendMinor: number
  impressions: number
  /**
   * Qamrovning PASTKI chegarasi (qatorlar bo'yicha MAX).
   * ⚠️ Bu ANIQ son EMAS — `reachApprox` ga qarang.
   */
  reach: number
  /** Qamrovning YUQORI chegarasi (xom yig'indi, takrorlar bilan). */
  reachUpper: number
  /** HAR DOIM `true`: Meta kunlar/platformalar bo'yicha noyob odamlarni dedup qilmaydi. */
  reachApprox: boolean
  clicks: number
  linkClicks: number
  /** Meta hisoblagan lidlar (`LeadsOnsite + LeadsPixel`). */
  metaLeads: number
  /**
   * Meta hisoblagan BOSHLANGAN YOZISHMALAR
   * (`onsite_conversion.messaging_conversation_started_7d`).
   *
   * "Xabar yuborish" (Click-to-Direct) reklamasining ASOSIY natijasi: bunday reklamada forma
   * umuman bo'lmaydi, ya'ni `metaLeads` nol turaveradi.
   * ⚠️ Lidlar bilan QO'SHILMAYDI — bitta odam ikkalasini ham qilishi mumkin.
   * ⚠️ 7 kunlik oyna tufayli son orqaga qarab o'zgaradi.
   */
  msgStarted: number
  /** CRM'ga kelgan XOM lid qatorlari (dublikatlar bilan). */
  adLeadRows: number
  /** TAKRORSIZ CRM lidlari — barcha konversiya sanoqlari AYNAN shular bo'yicha. */
  crmLeads: number
  /** Shulardan CRM'da endi mavjud bo'lmaganlari (o'chirilgan). */
  crmLeadsDeleted: number
  /** Lid narxi. `null` — hisoblab bo'lmadi (xarajat yoki lid nol). */
  cplMinor: number | null
  /** O'quvchi bo'lgan lidlar soni. */
  converted: number
  /** Sof o'quv to'lovi qilganlar soni. */
  paid: number
  /** ⚠️ BUTUN UMR bo'yicha sof o'quv to'lovi (xarajat esa faqat tanlangan oraliqda). */
  revenueMinor: number
  /** Mijoz narxi. `null` — hisoblab bo'lmadi. */
  cacMinor: number | null
  /** `(Daromad − Xarajat) / Xarajat`. `1.5` = "+150%". `null` — xarajat nol. */
  roi: number | null
  children: IgRoiNode[]
}

/** Kunlik qator (grafik uchun). ⚠️ Qamrov ATAYIN yo'q — u kunlar bo'yicha qo'shilmaydi. */
export interface IgRoiDay {
  date: string
  spendMinor: number
  impressions: number
  clicks: number
  metaLeads: number
  crmLeads: number
}

/** Platforma kesimi. `platform === 'all'` — Meta bo'linma bermagan, ya'ni AJRATILMAGAN qatorlar. */
export interface IgRoiPlatform {
  platform: IgAdsPlatform
  spendMinor: number
  impressions: number
  metaLeads: number
  crmLeads: number
}

/** Barcha hisobot javoblarida takrorlanadigan qism (sarlavha + izohlar). */
interface IgRoiCommon {
  /** `false` — akkaunt ulanmagan; javob baribir 200 va qolgani BO'SH. */
  connected: boolean
  from: string
  to: string
  platform: IgAdsPlatform
  campaignId: string
  currency: string
  currencyOffset: number
  /** Statistika QAYSI darajadan yig'ilgan: `ad` | `adset` | `campaign` (bo'sh — ma'lumot yo'q). */
  insightLevel: string
  totals: IgRoiNode
  /** ⚠️ O'zbekcha OGOHLANTIRISHLAR — ekranda ko'rsatilishi SHART, yutib yuborilmaydi. */
  notes: string[]
  /**
   * Meta bergan ATRIBUTSIYA OYNASI (`attribution_setting`, masalan `7d_click,1d_view`) —
   * Meta konversiyalari qaysi oyna bo'yicha sanalgani.
   *
   * ⚠️ Meta bergan HOLICHA ko'rsatiladi, **tarjima qilinmaydi**: bu Ads Manager'dagi
   * sozlamaning texnik nomi. Bo'sh — Meta bermagan.
   */
  attributionSetting: string
}

/** KPI + kunlik qator + platforma kesimi (kampaniya daraxtisiz — u og'ir). */
export interface IgRoiOverview extends IgRoiCommon {
  adAccountId: string
  adAccountName: string
  timezoneName: string
  lastSyncAt: string
  lastError: string
  daily: IgRoiDay[]
  platforms: IgRoiPlatform[]
}

/** Kampaniya → adset → e'lon daraxti (jamlanma bilan birga). */
export interface IgRoiCampaigns extends IgRoiCommon {
  campaigns: IgRoiNode[]
}

/** TO'LIQ hisobot — yuqoridagi ikkalasi bitta javobda. */
export interface IgRoiReport extends IgRoiOverview {
  campaigns: IgRoiNode[]
}

/** Sinxronizatsiya natijasi + YANGILANGAN holat (klient ikkinchi so'rov yubormasin). */
export interface IgAdsSyncResult {
  /** ⚠️ HTTP 200 bo'lsa ham `false` bo'lishi mumkin — sabab `error` da. */
  ok: boolean
  rows: number
  error: string
  status: IgAdsStatus
}

/** Hisobot endpointlarining umumiy filtrlari. */
export interface IgAdsFilters {
  from?: string
  to?: string
  platform?: IgAdsPlatform
  campaignId?: string
}

// ═══════════════════════════════════════════════ SO'ROVLAR

const BASE = '/admin/instagram/adsstats'

/** Modul holati — ulanish, token, oxirgi sinxronizatsiya va bazadagi qatorlar soni. */
export async function getIgAdsStatus(): Promise<IgAdsStatus> {
  const { data } = await api.get<IgAdsStatus>(`${BASE}/status`)
  return data
}

/** KPI + kunlik qator + platforma kesimi. Har filtr o'zgarganda shu chaqiriladi. */
export async function getIgAdsOverview(filters: IgAdsFilters): Promise<IgRoiOverview> {
  const { data } = await api.get<IgRoiOverview>(`${BASE}/overview`, {
    params: clean({ ...filters, platform: filters.platform === 'all' ? '' : filters.platform }),
  })
  return data
}

/** Kampaniya → adset → e'lon daraxti (jadval uchun). */
export async function getIgAdsCampaigns(filters: IgAdsFilters): Promise<IgRoiCampaigns> {
  const { data } = await api.get<IgRoiCampaigns>(`${BASE}/campaigns`, {
    params: clean({ ...filters, platform: filters.platform === 'all' ? '' : filters.platform }),
  })
  return data
}

/** TO'LIQ ROI hisoboti (jamlanma + kunlik + platforma + daraxt bitta javobda). */
export async function getIgAdsRoi(filters: IgAdsFilters): Promise<IgRoiReport> {
  const { data } = await api.get<IgRoiReport>(`${BASE}/roi`, {
    params: clean({ ...filters, platform: filters.platform === 'all' ? '' : filters.platform }),
  })
  return data
}

/**
 * QO'LDA sinxronizatsiya. Sanalar berilmasa — odatdagi kunlik siyosat.
 * ⚠️ Ruxsat: `marketing.settings` (yozish).
 */
export async function syncIgAdsStats(since?: string, until?: string): Promise<IgAdsSyncResult> {
  const { data } = await api.post<IgAdsSyncResult>(`${BASE}/sync`, { since, until })
  return data
}

// ═══════════════════════════════════════════════ FORMATLASH

/** Backenddagi `MetaCurrency.MaxOffset` bilan bir xil chegara. */
const MAX_OFFSET = 6

/** Offsetni ruxsat etilgan oraliqqa qisadi (bazadagi buzuq qiymat hisobni buzmasin). */
function clampOffset(offset: number): number {
  if (!Number.isFinite(offset) || offset < 0) return 0
  return offset > MAX_OFFSET ? MAX_OFFSET : Math.trunc(offset)
}

/** MINOR → MAJOR son (grafik o'qi uchun — u matn emas, RAQAM talab qiladi). */
export function adsMoneyMajor(minor: number, offset: number): number {
  return (minor ?? 0) / 10 ** clampOffset(offset)
}

/**
 * MINOR → odam o'qiydigan matn: `120000000` + offset 2 + "UZS" → `"1 200 000 UZS"`.
 *
 * Backenddagi `MetaCurrency.FormatMinor` bilan AYNAN bir xil qoida:
 * guruh ajratgichi — oddiy probel (madaniyatga bog'liq emas, `toLocaleString` ishlatilmaydi),
 * kasr qismi NOL bo'lsa umuman chizilmaydi (so'mda tiyin shovqin bo'lardi, dollarda esa
 * "312.45" baribir ko'rinadi).
 */
export function formatAdsMoney(minor: number, offset: number, currency?: string): string {
  const o = clampOffset(offset)
  const factor = 10 ** o
  const value = Math.round(minor ?? 0)

  const negative = value < 0
  const abs = Math.abs(value)
  const whole = Math.floor(abs / factor)
  const frac = abs % factor

  let out = negative ? '-' : ''
  out += whole.toLocaleString('en-US').replace(/,/g, ' ')
  if (frac !== 0 && o > 0) out += `.${String(frac).padStart(o, '0')}`

  const code = (currency ?? '').trim()
  if (code.length > 0) out += ` ${code.toUpperCase()}`
  return out
}

/**
 * ROI nisbatini foizga aylantiradi: `1.5` → `"+150%"`, `-1` → `"−100%"`.
 * ⚠️ `null` — "hisoblab bo'lmadi", `"0%"` EMAS.
 */
export function formatRoi(roi: number | null): string {
  if (roi == null || !Number.isFinite(roi)) return '—'
  const pct = roi * 100
  const sign = pct > 0 ? '+' : pct < 0 ? '−' : ''
  return `${sign}${Math.abs(pct).toFixed(pct >= 100 || pct <= -100 ? 0 : 1)}%`
}
