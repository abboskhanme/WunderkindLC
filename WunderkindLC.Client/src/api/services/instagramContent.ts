import { api } from '../client'

/**
 * MARKETING → INSTAGRAM KONTENT REJALASHTIRISH (E2) — admin API klienti
 * (`/api/admin/instagram/content/...`).
 *
 * Modul postlarni NAVBATGA qo'yadi va vaqti kelganda Instagram'ga joylaydi.
 *
 * ⚠️ REJALASHTIRISH BIZNIKI, META'NIKI EMAS: Instagram'da `scheduled_publish_time` yo'q va
 * media konteyneri 24 soatda o'ladi. Shuning uchun vaqt `scheduledAt` da turadi va konteyner
 * faqat chop etish payti yaratiladi. Frontend uchun ma'nosi: post «Rejalashtirilgan» bo'lib
 * turgani Instagram'da hech narsa band qilinmagani degani — vaqtni istagancha o'zgartirsa
 * bo'ladi.
 *
 * ⚠️ JOYLANGAN POST QAYTARIB BO'LMAYDI: Instagram API'si tahrirlashni ham, o'chirishni ham
 * qo'llab-quvvatlamaydi. `DELETE` joylangan postda faqat CRM yozuvini o'chiradi.
 *
 * ⚠️ Media manzili OCHIQ HTTPS bo'lishi shart — faylni Meta O'ZI yuklab oladi. CRM'ning oddiy
 * `/uploads` papkasi login ortida (`UploadsGuard`), ya'ni u yerdagi manzil ISHLAMAYDI.
 * Shu sababli fayl `uploadIgMedia` orqali ALOHIDA ochiq papkaga (`/uploads/marketing-public/`)
 * yuklanadi; manzilni qo'lda (tashqi CDN'dan) berish ham qoladi.
 */

// ═══════════════════════════════════════════════ TIPLAR

/** Post turi (backend: `IgPublishConst.PostTypes`). */
export type IgPostType = 'image' | 'video' | 'reels' | 'story' | 'carousel'

/** Post holati (backend: `IgPublishConst.Statuses`). */
export type IgPostStatus = 'scheduled' | 'processing' | 'published' | 'failed' | 'cancelled'

/** Media turi — `IgMediaJson.Kind`. */
export type IgMediaKind = 'image' | 'video'

/**
 * Bitta media elementi (backend `IgMediaJson`, camelCase).
 *
 * ⚠️ `thumbOffsetMs`: **-1 = berilmagan**, 0 — haqiqiy qiymat (birinchi kadr).
 * ⚠️ `sizeBytes` / `durationSeconds` / `width` / `height`: **0 = "noma'lum"**, bunday holatda
 *    backend tegishli tekshiruvni O'TKAZIB YUBORADI. Ya'ni noaniq qiymat yozgandan ko'ra 0
 *    qoldirilgani yaxshi — aks holda to'g'ri media bekorga rad etilardi.
 */
export interface IgMediaItem {
  /** OCHIQ HTTPS manzil — Meta faylni o'zi yuklab oladi. */
  url: string
  kind: IgMediaKind
  /** 0 = noma'lum. */
  sizeBytes: number
  /** 0 = noma'lum (video uchun soniya). */
  durationSeconds: number
  /** 0 = noma'lum. */
  width: number
  /** 0 = noma'lum. */
  height: number
  /** Reels muqovasi (ixtiyoriy, HTTPS). */
  coverUrl: string
  /** Reels muqova kadri (ms). **-1 = berilmagan**. */
  thumbOffsetMs: number
  altText: string
  /** ⚠️ Karusel BOLASIDA matn ishlamaydi — backend uni xato deb qaytaradi. */
  caption: string
}

/** Post sozlamalari (backend `IgOptionsJson`). */
export interface IgPostOptions {
  /** Reels'ni lentaga ham chiqarish (faqat Reels uchun ma'noli). */
  shareToFeed: boolean
  locationId: string
  /** Hammualliflar (≤3) — ular Instagram'da taklifni QABUL QILISHI kerak. */
  collaborators: string[]
  /** Reels audio nomi — Instagram'da BIR MARTA o'zgartiriladi. */
  audioName: string
}

/** Rejalashtirilgan post (backend `IgPostDto`). */
export interface IgPost {
  id: string
  postType: IgPostType
  /** Backend bergan o'zbekcha yorliq ("Rasm", "Reels", …). */
  postTypeLabel: string
  caption: string
  media: IgMediaItem[]
  options: IgPostOptions
  /** ISO "yyyy-MM-ddTHH:mm:ss" — BIZNING navbatimiz vaqti. */
  scheduledAt: string
  status: IgPostStatus
  /** Backend bergan o'zbekcha yorliq ("Joylandi", "Xato", …). */
  statusLabel: string
  /** Meta'da konteyner yaratilganmi (id'ning o'zi UI'ga kerak emas). */
  hasContainer: boolean
  containerStatus: string
  mediaId: string
  permalink: string
  attempts: number
  /** O'zbekcha sabab — backend Meta xato kodini tarjima qilib beradi. */
  error: string
  createdBy: string
  createdAt: string
  publishedAt: string
}

/** Holatlar bo'yicha sanoq — BUTUN topilma bo'yicha (ko'rinadigan sahifadan emas). */
export interface IgPostTotals {
  total: number
  scheduled: number
  processing: number
  published: number
  failed: number
  cancelled: number
}

export interface IgPostList {
  items: IgPost[]
  total: number
  page: number
  pageSize: number
  totals: IgPostTotals
}

/** Yaratish/tahrirlash so'rovi (backend `IgPostPayload`). */
export interface IgPostPayload {
  postType: IgPostType
  caption: string
  media: IgMediaItem[]
  options: IgPostOptions
  /** ISO; bo'sh bo'lsa backend "hozir" deb oladi. */
  scheduledAt: string
}

/** O'chirish natijasi: "bekor qilindi" va "yozuv o'chdi" ATAYIN ajratilgan. */
export interface IgPostDeleteResult {
  cancelled: boolean
  removed: boolean
  message: string
}

/**
 * Kunlik chop etish limiti.
 *
 * 🔴 `unknown === true` (yoki `total === 0`) — Meta jami kvotani bermadi. Bunday holatda
 * ekranda **"noma'lum"** yoziladi. Taxminiy 50/100 KO'RSATILMAYDI: Meta hujjatlari
 * qo'llanmada 100, reference namunasida 50 deb ZID yozadi.
 */
export interface IgPostLimit {
  usage: number
  total: number
  unknown: boolean
  /** Backend tayyorlagan matn ("2 / 100" yoki "2 (jami noma'lum)"). */
  text: string
  error: string
}

/** Kontent bo'limi diagnostikasi. `scopeGranted === null` — "noma'lum". */
export interface IgContentStatus {
  /** `CenterMeta.InstagramPublishEnabled` — modul yoqilganmi. */
  enabled: boolean
  accountConnected: boolean
  /** null = noma'lum (berilgan OAuth ruxsatlari saqlanmaydi). */
  scopeGranted: boolean | null
  /** Kerakli scope nomi — "Qayta ulash" maslahatida ko'rsatiladi. */
  publishScope: string
  scheduled: number
  processing: number
  failed: number
  publishedThisWeek: number
}

// ═══════════════════════════════════════════════ CHEGARALAR (backend bilan bir xil)

/**
 * §5.5 chegaralari — backend `IgPublishConst` bilan AYNAN bir xil bo'lishi shart.
 * Bu yerda ular faqat foydalanuvchini OLDINDAN ogohlantirish uchun: yakuniy qaror baribir
 * serverda (`InstagramPublishContract.ValidatePost`).
 */
export const IG_LIMITS = {
  captionChars: 2200,
  hashtags: 30,
  mentions: 20,
  altTextChars: 1000,
  collaborators: 3,
  imageMb: 8,
  reelsMb: 300,
  storyVideoMb: 100,
  reelsSeconds: { min: 3, max: 900 },
  storyVideoSeconds: { min: 3, max: 60 },
  feedRatio: { min: 0.8, max: 1.91 },
  feedWidth: { min: 320, max: 1440 },
  carouselItems: { min: 2, max: 10 },
} as const

/** Post turlari — tanlash ro'yxati uchun (yorliq + qisqa izoh). */
export const IG_POST_TYPES: ReadonlyArray<{ id: IgPostType; label: string; hint: string }> = [
  { id: 'image', label: 'Rasm', hint: 'Lentaga bitta JPEG rasm' },
  { id: 'video', label: 'Video', hint: 'Lentaga video (9:16)' },
  { id: 'reels', label: 'Reels', hint: 'Vertikal video, 3–900 s' },
  { id: 'story', label: 'Story', hint: '24 soatlik, 9:16' },
  { id: 'carousel', label: 'Karusel', hint: '2–10 ta element' },
]

/** Holat kalitlari — filtr chiplari uchun. */
export const IG_POST_STATUSES: ReadonlyArray<{ id: IgPostStatus; label: string }> = [
  { id: 'scheduled', label: 'Rejalashtirilgan' },
  { id: 'processing', label: 'Joylanmoqda' },
  { id: 'published', label: 'Joylandi' },
  { id: 'failed', label: 'Xato' },
  { id: 'cancelled', label: 'Bekor qilingan' },
]

/** Faqat `scheduled` post tahrirlanadi (backend ham 400 qaytaradi, §5.9). */
export function isEditable(post: IgPost): boolean {
  return post.status === 'scheduled'
}

/** Media turi post turiga qarab: reels/video — video, qolgani rasm (story ikkalasi ham bo'ladi). */
export function defaultKind(type: IgPostType): IgMediaKind {
  return type === 'reels' || type === 'video' ? 'video' : 'image'
}

/** Bo'sh media elementi — `thumbOffsetMs = -1` ("berilmagan"), o'lchamlar 0 ("noma'lum"). */
export function emptyMedia(kind: IgMediaKind = 'image'): IgMediaItem {
  return {
    url: '', kind, sizeBytes: 0, durationSeconds: 0, width: 0, height: 0,
    coverUrl: '', thumbOffsetMs: -1, altText: '', caption: '',
  }
}

/** Standart sozlamalar (backend `IgOptionsJson` bilan bir xil). */
export function emptyOptions(): IgPostOptions {
  return { shareToFeed: true, locationId: '', collaborators: [], audioName: '' }
}

/**
 * Hashtag/mention sanagichi — backend `InstagramPublishContract.CountTags` bilan BIR XIL qoida:
 * belgi satr boshida yoki harf/raqam/`_` BO'LMAGAN belgidan keyin turishi va undan keyin
 * kamida bitta harf/raqam/`_` kelishi shart.
 *
 * ⚠️ `abc#def` hashtag EMAS, `ali@mail.uz` mention EMAS — aks holda ekrandagi son server
 * bergan xato bilan to'g'ri kelmasdi.
 */
function countTags(text: string, marker: string): number {
  const wordish = (c: string) => /[\p{L}\p{N}_]/u.test(c)
  let count = 0
  for (let i = 0; i < text.length; i++) {
    if (text[i] !== marker) continue
    if (i > 0 && wordish(text[i - 1])) continue
    if (i + 1 >= text.length) continue
    if (!wordish(text[i + 1])) continue
    count++
  }
  return count
}

export const countHashtags = (text: string): number => countTags(text, '#')
export const countMentions = (text: string): number => countTags(text, '@')

/** Manzil ochiq HTTPS'mi (faqat sxema — "haqiqatan ochiqmi" ni tarmoqsiz bilib bo'lmaydi). */
export function isHttpsUrl(url: string): boolean {
  return /^https:\/\/\S+$/i.test(url.trim())
}

/** Kengaytma JPEG'mi (so'rov qismi hisobga olinmaydi) — Instagram FAQAT JPEG qabul qiladi. */
export function isJpegUrl(url: string): boolean {
  return /\.(jpg|jpeg)(\?|#|$)/i.test(url.trim())
}

/** Kengaytma MP4/MOV'mi. */
export function isVideoUrl(url: string): boolean {
  return /\.(mp4|mov)(\?|#|$)/i.test(url.trim())
}

// ═══════════════════════════════════════════════ SO'ROVLAR

/** Bo'sh qiymatlarni tashlab, faqat to'ldirilgan filtrlarni yuboradi. */
function clean(params: object): Record<string, unknown> {
  return Object.fromEntries(
    Object.entries(params).filter(([, v]) => v !== undefined && v !== null && v !== ''),
  )
}

/**
 * Postlar ro'yxati + jamlanma.
 * @param to Tugash KUNI — backend uni kun oxirigacha cho'zadi.
 */
export async function getIgPosts(params: {
  from?: string
  to?: string
  status?: IgPostStatus | 'all'
  page?: number
} = {}): Promise<IgPostList> {
  const { data } = await api.get<IgPostList>('/admin/instagram/content/posts', { params: clean(params) })
  return data
}

/** Bitta post (tahrirlash oynasi uchun). */
export async function getIgPost(id: string): Promise<IgPost> {
  const { data } = await api.get<IgPost>(`/admin/instagram/content/posts/${id}`)
  return data
}

/** Yangi reja. Server media va caption'ni SAQLASHDAN OLDIN tekshiradi. */
export async function createIgPost(payload: IgPostPayload): Promise<IgPost> {
  const { data } = await api.post<IgPost>('/admin/instagram/content/posts', payload)
  return data
}

/** Tahrirlash — FAQAT `scheduled` holatida (aks holda 400 va o'zbekcha sabab). */
export async function updateIgPost(id: string, payload: IgPostPayload): Promise<IgPost> {
  const { data } = await api.put<IgPost>(`/admin/instagram/content/posts/${id}`, payload)
  return data
}

/**
 * Bekor qilish yoki yozuvni o'chirish — natija javobda AJRATILGAN:
 * `cancelled` (post joylanmaydi) yoki `removed` (yozuv o'chdi).
 *
 * ⚠️ Joylangan postda bu FAQAT CRM yozuvini o'chiradi — Instagram'dagi post QOLADI.
 */
export async function deleteIgPost(id: string): Promise<IgPostDeleteResult> {
  const { data } = await api.delete<IgPostDeleteResult>(`/admin/instagram/content/posts/${id}`)
  return data
}

/**
 * «Hoziroq joylash» / «Qayta urinish».
 *
 * ⚠️ So'rov joylanishni KUTMAYDI: rasm odatda shu yerdayoq joylanadi, video/reels esa
 * `processing` bo'lib qoladi va uni worker oxiriga yetkazadi.
 */
export async function publishIgPost(id: string): Promise<IgPost> {
  const { data } = await api.post<IgPost>(`/admin/instagram/content/posts/${id}/publish`)
  return data
}

/**
 * Kunlik chop etish limiti.
 *
 * ⚠️ Bu endpoint HAR chaqirilganda Meta'ga so'rov yuboradi — AVTO-YANGILANISHGA
 * QO'SHILMAYDI. Faqat sahifa ochilganda va qo'lda «Yangilash» bosilganda so'raladi.
 */
export async function getIgContentLimit(): Promise<IgPostLimit> {
  const { data } = await api.get<IgPostLimit>('/admin/instagram/content/limit')
  return data
}

/** Bo'lim diagnostikasi — "nega post chiqmayapti" savolining sabablari (faqat baza). */
export async function getIgContentStatus(): Promise<IgContentStatus> {
  const { data } = await api.get<IgContentStatus>('/admin/instagram/content/status')
  return data
}

// ═══════════════════════════════════════════════ MEDIA YUKLASH (§5.6)

/**
 * Yuklangan media haqidagi javob (backend `IgUploadedMediaDto`).
 *
 * Maydon nomlari `IgMediaItem` bilan AYNAN bir xil — natijani post payload'iga to'g'ridan-to'g'ri
 * qo'yish uchun.
 *
 * ⚠️ `0` = «noma'lum» (fayl sarlavhasidan o'qib bo'lmadi) — `IgMediaItem` dagi bir xil kelishuv.
 */
export interface IgUploadedMedia {
  /** OCHIQ (absolut) HTTPS manzil — Meta faylni SHU manzildan yuklab oladi. */
  url: string
  kind: IgMediaKind
  sizeBytes: number
  width: number
  height: number
  durationSeconds: number
}

/**
 * Media faylni serverga yuklaydi.
 *
 * 🔴 Fayl odatdagi `/uploads` ga EMAS, ALOHIDA ochiq papkaga (`/uploads/marketing-public/`)
 * tushadi: Instagram faylni O'ZI yuklab oladi, ya'ni manzil login talab qilmasligi shart
 * (`UploadsGuard` ortidagi `/uploads` Meta uchun 404 bo'lardi — xato kodi `2207052`).
 *
 * ⚠️ Dev muhitida (http) qaytadigan manzil HTTPS bo'lmaydi va server post saqlashda uni
 * ATAYIN rad etadi — bu yashirilmaydi, lokalda haqiqiy post joylab bo'lmaydi.
 *
 * Ruxsat: `marketing.content` (yozish).
 */
export async function uploadIgMedia(file: File): Promise<IgUploadedMedia> {
  const form = new FormData()
  form.append('file', file)
  const { data } = await api.post<IgUploadedMedia>('/admin/instagram/content/media', form, {
    headers: { 'Content-Type': 'multipart/form-data' },
  })
  return data
}

// ═══════════════════════════════════════════════ AI BILAN CAPTION YOZISH (§5.10)

/** Matn uslubi (backend `InstagramCaptionService.Tones`). */
export type IgCaptionTone = 'friendly' | 'expert' | 'energetic' | 'sales'

/** Tanlash ro'yxatining bitta qatori — YORLIQ ham backenddan keladi (drift bo'lmasin). */
export interface IgCaptionOption {
  id: string
  label: string
}

/** Uslub/til ro'yxatlari + standart qiymatlar. */
export interface IgCaptionMeta {
  tones: IgCaptionOption[]
  languages: IgCaptionOption[]
  defaultTone: string
  defaultLanguage: string
  /** `false` — `.env` da `GEMINI_API_KEY` yo'q; tugma OLDINDAN o'chiriladi. */
  geminiConfigured: boolean
}

export interface IgCaptionRequest {
  postType: IgPostType
  /** Foydalanuvchi yozgan MAVZU — yagona majburiy maydon. */
  topic: string
  language?: string
  tone?: string
}

/**
 * AI natijasi.
 *
 * ⚠️ `caption` — TAYYOR matn: hashtaglar allaqachon oxiriga qo'shilgan va chegaralarga
 * (2200 belgi / 30 hashtag / 20 mention) solishtirilgan. `hashtags` esa faqat KO'RSATISH
 * uchun — uni matnga QAYTA qo'shish takror bo'lardi.
 */
export interface IgCaptionResult {
  ok: boolean
  caption: string
  hashtags: string[]
  /** `ok === false` bo'lsa — foydalanuvchiga ko'rsatiladigan o'zbekcha sabab. */
  error: string
}

/** Uslub/til ro'yxatlari (sahifa ochilganda bir marta). */
export async function getIgCaptionMeta(): Promise<IgCaptionMeta> {
  const { data } = await api.get<IgCaptionMeta>('/admin/instagram/content/caption/meta')
  return data
}

/**
 * Mavzudan post matnini yozdiradi.
 *
 * ⚠️ Javob HAR DOIM 200 — muvaffaqiyat `ok` bayrog'ida. Sabab: xatolarning aksariyati tashqi
 * va vaqtinchalik (kalit sozlanmagan, Gemini timeout, format buzuq), foydalanuvchiga esa
 * umumiy "so'rov xato ketdi" emas, AYNAN sabab kerak. Shuning uchun chaqiruvchi `ok` ni
 * TEKSHIRISHI shart.
 */
export async function generateIgCaption(payload: IgCaptionRequest): Promise<IgCaptionResult> {
  const { data } = await api.post<IgCaptionResult>('/admin/instagram/content/caption', payload)
  return data
}
