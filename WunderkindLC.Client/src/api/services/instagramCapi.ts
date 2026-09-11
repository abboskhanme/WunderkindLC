import { api } from '../client'

/**
 * MARKETING → SOZLAMALAR sahifasining UCHTA yangi kartasi uchun API klienti:
 * **Reklama statistikasi (Ads Insights)** · **CAPI (lid sifatini Meta'ga qaytarish)** ·
 * **Kontent joylash** holati.
 *
 * ⚠️ **Nega alohida fayl (`instagram.ts` ga qo'shilmadi):** backendda ham bu uchtasi
 * `InstagramController` ning ALOHIDA `partial` qismlarida turibdi (`.AdsStats`, `.Capi`,
 * `.Content`) — har birining o'z bayrog'i, o'z tokeni va o'z navbati bor. Sozlamalar
 * ekranidagi kartalar ham shu chegarani takrorlaydi.
 *
 * ⚠️ **Bu fayl faqat SOZLASH chaqiruvlarini beradi.** Reklama statistikasining hisobotlari
 * (`/adsstats/report`, ROI jadvallari) va kontent rejasining CRUD'i AYRI fayllarda —
 * bu yerda ular takrorlanmasin (bir funksiya ikki joyda e'lon qilinsa, ikkalasi vaqt
 * o'tib bir-biridan farq qila boshlardi).
 *
 * 🔴 **MAXFIYLIK:** Access token, CAPI tokeni va Dataset ID QIYMATI hech qaysi javobda
 * YO'Q — faqat `tokenSet` / `datasetIdSet` bayroqlari. Forma tokeni bo'sh yuborilsa
 * serverda mavjudi saqlanadi, ya'ni faqat akkaunt ID'sini tuzatish uchun tokenni qayta
 * yozdirish shart emas (`ads/page` bilan bir xil naqsh).
 */

// ═══════════════════════════════════════════════ REKLAMA STATISTIKASI (Ads Insights)

/**
 * "Nega statistika yo'q" savolining barcha sabablari bitta javobda.
 *
 * ⚠️ `tokenSet` — TOKEN QIYMATI EMAS, faqat "sozlangan/sozlanmagan" bayrog'i.
 * ⚠️ Sanoqlar (`insightRows`/`entityRows`) FAQAT joriy akkaunt bo'yicha: akkaunt
 * almashtirilsa eski qatorlar bazada qoladi, lekin ular yangi akkauntga tegishli emas.
 */
export interface IgAdsStatsStatus {
  /** Modul bayrog'i (`CenterMeta.InstagramAdsStatsEnabled`). */
  enabled: boolean
  /** Reklama akkaunti ulanganmi (faol `IgAdAccount` bormi). */
  connected: boolean
  /** `act_1234567890` ko'rinishida — bazada HAR DOIM prefiksli. */
  adAccountId: string
  name: string
  currency: string
  /** Pul MINOR birlikda saqlanadi (tiyin/sent) — ekranda shu offset bilan bo'linadi. */
  currencyOffset: number
  /**
   * Kasr xonalari QAYERDAN olingani: `"meta"` — Meta o'zi bergan, `"jadval"` — bizning
   * valyuta ro'yxatimizdan hisoblangan. Ulanmagan bo'lsa bo'sh.
   *
   * ⚠️ Bu KO'RSATILISHI muhim: noto'g'ri kasr xonasi butun pul hisobini **100 barobar**
   * buzadi. Meta hujjatlari `currency_offset` maydoni bor-yo'qligida ZID bo'lgani uchun kod
   * uni ish vaqtida aniqlaydi — admin qaysi yo'l ishlaganini ko'rib tursin.
   */
  currencyOffsetSource: string
  /** Statistika kunlari AYNAN shu zonada kesiladi (Toshkent kuni bilan farq qilishi mumkin). */
  timezoneName: string
  /** Token sozlanganmi. ⚠️ Qiymat qaytmaydi. */
  tokenSet: boolean
  connectedAt: string
  connectedBy: string
  lastSyncAt: string
  lastError: string
  /** Avtomatik sinxronizatsiya soati va birinchi yuklashdagi backfill chuqurligi. */
  syncHour: number
  backfillDays: number
  /** Bazadagi kunlik statistika qatorlari va reklama iyerarxiyasi obyektlari soni. */
  insightRows: number
  entityRows: number
  /** Eng oxirgi statistika kuni ("yyyy-MM-dd") — "ulangan, lekin ma'lumot eskirgan" holati. */
  lastStatDate: string
}

/** Sinxronizatsiya natijasi + YANGILANGAN holat (klient ikkinchi so'rov yubormasin). */
export interface IgAdsSyncResult {
  /** ⚠️ HTTP baribir 200 — sabab shu yerda (sinxronizatsiya QISMAN bajarilishi mumkin). */
  ok: boolean
  rows: number
  error: string
  status: IgAdsStatsStatus
}

/** Diagnostika: modul, akkaunt, token, oxirgi sinxronizatsiya va yuklangan qatorlar. */
export async function getAdsStatsStatus(): Promise<IgAdsStatsStatus> {
  const { data } = await api.get<IgAdsStatsStatus>('/admin/instagram/adsstats/status')
  return data
}

/**
 * Reklama akkauntini ULASH.
 *
 * ⚠️ Server SAQLASHDAN OLDIN Meta'da tekshiradi — token noto'g'ri bo'lsa yoki unda
 * `ads_read` bo'lmasa xato DARHOL qaytadi va hech narsa saqlanmaydi.
 * `accessToken` bo'sh yuborilsa mavjud token o'zgarmaydi.
 */
export async function saveAdsStatsAccount(
  adAccountId: string, accessToken: string,
): Promise<IgAdsStatsStatus> {
  const { data } = await api.put<IgAdsStatsStatus>(
    '/admin/instagram/adsstats/account', { adAccountId, accessToken },
  )
  return data
}

/** Akkauntni UZISH — token tozalanadi, yig'ilgan statistika tarixi QOLADI. */
export async function disconnectAdsStatsAccount(): Promise<IgAdsStatsStatus> {
  const { data } = await api.delete<IgAdsStatsStatus>('/admin/instagram/adsstats/account')
  return data
}

/**
 * QO'LDA sinxronizatsiya.
 *
 * ⚠️ **Birinchi marta bir necha DAQIQA olishi mumkin** — 90 kunlik backfill Meta'ga
 * o'nlab so'rov yuboradi. Tugma shu vaqtda `disabled` bo'lishi kerak.
 * Sanalar berilmasa odatdagi kunlik siyosat ishlaydi.
 */
export async function syncAdsStatsNow(since?: string, until?: string): Promise<IgAdsSyncResult> {
  const { data } = await api.post<IgAdsSyncResult>(
    '/admin/instagram/adsstats/sync', { since: since ?? '', until: until ?? '' },
  )
  return data
}

// ═══════════════════════════════════════════════ CAPI (Conversions API)

/** Navbatdagi hodisaning holati. */
export type IgCapiEventStatus = 'pending' | 'sent' | 'failed' | 'skipped'

/**
 * CAPI diagnostikasi — "nega hodisa ketmayapti" savolining barcha sabablari.
 *
 * 🔴 `datasetIdSet` va `tokenSet` — faqat BAYROQ. Dataset ID ham, token ham qiymat
 * sifatida hech qachon qaytmaydi.
 */
export interface IgCapiStatus {
  enabled: boolean
  datasetIdSet: boolean
  tokenSet: boolean
  /**
   * Hodisa nomlari. 🔴 Bular Events Manager'dagi bosqich nomlari bilan AYNAN bir xil
   * bo'lishi SHART — aks holda Meta hodisani tanimaydi (ERKIN MATN, enum emas).
   */
  stageQualified: string
  stageWon: string
  /** ⚠️ Sonlar BUTUN navbat bo'yicha. */
  pending: number
  sent: number
  failed: number
  skipped: number
  lastSentAt: string
  lastError: string
}

/** Sozlamalar formasi. Dataset ID va token BO'SH yuborilsa mavjudi saqlanadi. */
export interface IgCapiSettingsPayload {
  enabled: boolean
  /** ⚠️ Bo'sh satr = "o'zgartirma" — qiymati javobga tushmagani uchun forma uni ko'rsata olmaydi. */
  datasetId: string
  /** ⚠️ Bo'sh satr = "o'zgartirma" (forma tokenni hech qachon ko'rsatmaydi). */
  token: string
  /** Bo'sh yuborilsa oldingi nom qoladi (bo'sh `event_name` bilan Meta so'rovni rad etadi). */
  stageQualified: string
  stageWon: string
}

/** Navbatdagi bitta hodisa. ⚠️ `payloadJson` ATAYIN qaytmaydi (uzun va PII xavfi). */
export interface IgCapiEvent {
  id: string
  /** CRM `Lead.Id`. */
  leadId: string
  /** Meta lid ID'si — Meta qo'llab-quvvatlash xizmati aynan shuni so'raydi. */
  leadgenId: string
  eventName: string
  eventId: string
  status: string
  attempts: number
  error: string
  eventTime: string
  createdAt: string
  sentAt: string
}

export interface IgCapiEventList {
  items: IgCapiEvent[]
  total: number
  page: number
  pageSize: number
  /** ⚠️ Jamlanma BUTUN jadval bo'yicha — filtrga va sahifaga bog'liq EMAS. */
  totals: { total: number; pending: number; sent: number; failed: number; skipped: number }
}

/** ⚠️ `ok=false` bo'lsa ham HTTP 200 — sabab `error` da (masalan "modul o'chirilgan"). */
export interface IgCapiSendResult {
  ok: boolean
  created: number
  sent: number
  error: string
}

/** Diagnostika: modul, Dataset ID, token va navbat sanoqlari. */
export async function getCapiStatus(): Promise<IgCapiStatus> {
  const { data } = await api.get<IgCapiStatus>('/admin/instagram/capi/status')
  return data
}

/** Sozlamalarni saqlash. `datasetId` yoki `token` bo'sh bo'lsa serverda mavjudi qoladi. */
export async function saveCapiSettings(payload: IgCapiSettingsPayload): Promise<IgCapiStatus> {
  const { data } = await api.put<IgCapiStatus>('/admin/instagram/capi/settings', payload)
  return data
}

/** Navbat ro'yxati (sahifalangan). `status` noma'lum bo'lsa server uni JIM e'tiborsiz qoldiradi. */
export async function getCapiEvents(
  status?: IgCapiEventStatus | '', page = 1,
): Promise<IgCapiEventList> {
  const { data } = await api.get<IgCapiEventList>('/admin/instagram/capi/events', {
    params: { status: status || undefined, page },
  })
  return data
}

/**
 * QO'LDA skan + yuborish ("kutmasdan hozir yubor").
 * Worker buni kuniga bir marta o'zi bajaradi; bu tugma sozlashdan keyin natijani
 * DARHOL ko'rish uchun.
 */
export async function sendCapiNow(): Promise<IgCapiSendResult> {
  const { data } = await api.post<IgCapiSendResult>('/admin/instagram/capi/send')
  return data
}

// ═══════════════════════════════════════════════ KONTENT JOYLASH

/**
 * Kontent joylash moduli holati (Sozlamalardagi kichik karta uchun).
 *
 * 🔴 **`scopeGranted` `null` bo'lishi MUMKIN va bu odatiy hol:** OAuth'da berilgan
 * scope'lar bazada SAQLANMAYDI, ya'ni "ruxsat bormi" savoliga aniq javob yo'q.
 * `null` = "noma'lum" — bu holatda foydalanuvchiga akkauntni QAYTA ULASH taklif qilinadi
 * (yangi ruxsat aynan qayta ulashda so'raladi).
 */
export interface IgContentStatus {
  enabled: boolean
  /** Instagram akkaunt ulangan VA tokeni bormi. */
  accountConnected: boolean
  /** `true` — bor, `false` — yo'q, **`null` — NOMA'LUM** (yuqoridagi izohga qarang). */
  scopeGranted: boolean | null
  /** Kerakli ruxsat nomi (`instagram_business_content_publish`). */
  publishScope: string
  scheduled: number
  processing: number
  failed: number
  publishedThisWeek: number
}

/** Kontent moduli holati: yoqilganmi, akkaunt ulanganmi, navbat sanoqlari. */
export async function getContentStatus(): Promise<IgContentStatus> {
  const { data } = await api.get<IgContentStatus>('/admin/instagram/content/status')
  return data
}
