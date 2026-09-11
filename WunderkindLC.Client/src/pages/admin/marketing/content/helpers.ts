/**
 * MARKETING → KONTENT sahifalarining UMUMIY SOF FUNKSIYALARI.
 *
 * Modul ilgari bitta 1600 qatorli fayl edi; sahifalarga bo'linganda quyidagi mantiq bir
 * nechta bolaga kerak bo'lib qoldi (navbat, joylanganlar, holat, muharrir). Nusxa ko'chirish
 * o'rniga hammasi SHU YERDA — ya'ni holat rangi, sana formati va ro'yxat yuklash qoidasi
 * sahifalarda ayri ketmaydi.
 *
 * ⚠️ Bu faylda JSX YO'Q — faqat sof funksiyalar va konstantalar (komponent bilan aralashmasin:
 * eslint `react-refresh/only-export-components`, `lib/month.ts` bilan bir xil konvensiya).
 */
import { getIgPosts, type IgMediaItem, type IgPost, type IgPostStatus, type IgPostTotals, type IgPostType } from '@/api/services/instagramContent'
import { todayIso } from '@/lib/month'

/** Bitta oyda ko'pi bilan shuncha sahifa o'qiladi (50 × 4 = 200 post). */
export const MAX_PAGES = 4

/* ═══════════════════════════════════════ HOLAT RANGLARI ═══════════════════════════════════════ */

export interface StatusTone {
  bg: string
  color: string
  icon: string
}

/**
 * Holat ranglari — barcha kontent sahifalarida BIR XIL bo'lishi uchun yagona manba.
 *
 * ⚠️ Ranglar CSS o'zgaruvchilaridan olinadi (`var(--…)`), ya'ni mavzu (light/dark) almashsa
 * o'z-o'zidan moslashadi — qattiq hex yozilsa qorong'i mavzuda o'qilmay qolardi.
 */
export const STATUS_STYLE: Record<IgPostStatus, StatusTone> = {
  scheduled: { bg: 'var(--primary-soft)', color: 'var(--primary)', icon: 'clock' },
  processing: { bg: 'var(--warning-soft)', color: 'var(--warning)', icon: 'refresh' },
  published: { bg: 'var(--success-soft)', color: 'var(--success)', icon: 'check' },
  failed: { bg: 'var(--danger-soft)', color: 'var(--danger)', icon: 'warn' },
  cancelled: { bg: 'var(--surface-2)', color: 'var(--text-3)', icon: 'close' },
}

/* ═══════════════════════════════════════ RO'YXATNI YUKLASH ═══════════════════════════════════════ */

export interface LoadedPosts {
  items: IgPost[]
  /** Holatlar bo'yicha jamlanma — BUTUN topilma bo'yicha (birinchi sahifadan olinadi). */
  totals: IgPostTotals | null
  /** Chegaradan tashqarida qolgan postlar soni (0 bo'lsa hammasi yuklandi). */
  truncated: number
}

/**
 * Oylik ro'yxatni TO'LIQ yuklaydi (kerak bo'lsa `MAX_PAGES` gacha ketma-ket sahifa).
 *
 * ⚠️ Kalendar katagidagi son va ro'yxat AYNAN bitta manbadan chiqsin — shuning uchun kun
 * bo'yicha filtr KLIENTDA qilinadi. Aks holda katakda "3" turib, ro'yxatda 2 ta post
 * ko'rinishi mumkin edi ("raqamlar to'g'ri kelmayapti" holati).
 *
 * ⚠️ `totals` FAQAT birinchi sahifadan olinadi: u butun topilma bo'yicha hisoblanadi, ya'ni
 * keyingi sahifalarda ayni o'sha sonlar qaytadi.
 *
 * 🔴 QATORLAR `id` BO'YICHA DEDUP QILINADI. Backend `OrderByDescending(p => p.ScheduledAt)` +
 * `Skip/Take` bilan sahifalaydi va IKKILAMCHI tartiblovchisi YO'Q — ya'ni bir xil `ScheduledAt`
 * li postlarning tartibi so'rovdan so'rovga o'zgarishi mumkin. Sahifa chegarasida (50 ga karrali)
 * bitta post IKKI MARTA kelishi yoki umuman TUSHIB QOLISHI mumkin edi; birinchisi React `key`
 * dublikatini va kalendarda yolg'on sonni berardi. Dedup — arzon va ishonchli himoya.
 * (Asl yechim backendda: `.ThenByDescending(p => p.Id)`.)
 */
export async function loadAllPosts(params: {
  from: string
  to: string
  status: IgPostStatus | 'all'
}): Promise<LoadedPosts> {
  const rows: IgPost[] = []
  const seen = new Set<string>()
  let total = 0
  let sums: IgPostTotals | null = null
  // Xom (dedupdan OLDINGI) qatorlar soni: sahifalashni TO'XTATISH qarori serverning sanog'i
  // bilan solishtiriladi, dedup natijasi bilan emas — aks holda dublikat bo'lgan joyda
  // sikl bekordan-bekor keyingi sahifani so'rab ketardi.
  let fetched = 0

  for (let page = 1; page <= MAX_PAGES; page++) {
    const res = await getIgPosts({ from: params.from, to: params.to, status: params.status, page })
    if (page === 1) {
      sums = res.totals
      total = res.total
    }
    fetched += res.items.length
    for (const item of res.items) {
      if (seen.has(item.id)) continue
      seen.add(item.id)
      rows.push(item)
    }
    // Bo'sh sahifa yoki hammasi yig'ildi — keyingi so'rov bekorga ketardi.
    if (res.items.length === 0 || fetched >= res.total) break
  }

  // ⚠️ `truncated` DEDUPDAN KEYINGI songa qarab hisoblanadi: agar sahifalash beqarorligi
  // tufayli bir post tushib qolgan bo'lsa, ro'yxatda serverdagidan kam qator bo'ladi va buni
  // JIM o'tkazib yuborish mumkin emas — foydalanuvchi "yana N tasi sig'madi" deb ogohlantiriladi.
  return { items: rows, totals: sums, truncated: Math.max(0, total - rows.length) }
}

/* ═══════════════════════════════════════ POST TURI QOIDALARI ═══════════════════════════════════════ */

/** Post turiga qarab media 9:16 bo'lishi kerakmi (preview ramkasi ham shunga qarab chiziladi). */
export function isVertical(type: IgPostType): boolean {
  return type === 'story' || type === 'reels' || type === 'video'
}

/**
 * Post turida shu media turi TAQIQLANGANMI.
 *
 * ⚠️ Story ham, KARUSEL ham rasm va videoni birga qabul qiladi (backend `ValidateMedia`
 * ikkalasini ham o'tkazadi) — shuning uchun u yerda hech narsa bloklanmaydi. Reels/video esa
 * faqat video, oddiy rasm posti esa faqat rasm.
 */
export function mediaKindLocked(type: IgPostType, kind: 'image' | 'video'): boolean {
  if (type === 'reels' || type === 'video') return kind === 'image'
  if (type === 'image') return kind === 'video'
  return false
}

/** Post turi uchun ikonka nomi (`mk.tsx` dagi `Icon` bilan bir xil kalitlar). */
export function postTypeIcon(type: IgPostType): string {
  switch (type) {
    case 'image': return 'image'
    case 'video': return 'film'
    case 'reels': return 'play'
    case 'story': return 'clock'
    case 'carousel': return 'layers'
    default: return 'image'
  }
}

/* ═══════════════════════════════════════ KICHIK YORDAMCHILAR ═══════════════════════════════════════ */

/**
 * Birinchi MUSBAT sonni tanlaydi, hech biri bo'lmasa 0.
 *
 * ⚠️ `0` bu yerda «noma'lum» degani (backend bilan bir xil kelishuv), shuning uchun oddiy
 * `??` yaramaydi: u 0 ni HAQIQIY qiymat deb qabul qilib, keyingi manbaga o'tmasdi.
 */
export function firstPositive(...values: (number | undefined)[]): number {
  for (const v of values) if (typeof v === 'number' && v > 0) return v
  return 0
}

/** Uzun matnni qirqadi va oxiriga "…" qo'yadi (chegaraga tegmasa — o'zgarishsiz qaytadi). */
export function trim(text: string, max: number): string {
  return text.length <= max ? text : `${text.slice(0, max - 1)}…`
}

/* ═══════════════════════════════════════ SANA VA O'LCHAM FORMATLARI ═══════════════════════════════════════ */

/** O'zbekcha oy nomlari — indeks 0 = yanvar (ya'ni oy raqamidan 1 ayiriladi). */
const MONTHS = [
  'yanvar', 'fevral', 'mart', 'aprel', 'may', 'iyun',
  'iyul', 'avgust', 'sentabr', 'oktabr', 'noyabr', 'dekabr',
]

/** Hafta kunlari — `Date.getDay()` (0 = yakshanba) tartibida. */
const WEEKDAYS = [
  'yakshanba', 'dushanba', 'seshanba', 'chorshanba', 'payshanba', 'juma', 'shanba',
]

/**
 * "2026-08-22T14:30:00" → "22.08.2026 14:30". Bo'sh bo'lsa "—".
 *
 * ⚠️ `new Date(...)` ATAYIN ishlatilmaydi: backend vaqti mintaqasiz ISO ("yyyy-MM-ddTHH:mm:ss")
 * va uni `Date` orqali o'tkazish brauzer mintaqasiga qarab soatni SURIB yuborardi — navbatda
 * ko'rsatilgan vaqt server rejalashtirganidan farq qilib qolardi.
 */
export function fmtWhen(iso: string): string {
  if (!iso) return '—'
  const day = iso.slice(0, 10)
  const time = iso.slice(11, 16)
  if (day.length !== 10) return '—'
  const [y, m, d] = day.split('-')
  return time ? `${d}.${m}.${y} ${time}` : `${d}.${m}.${y}`
}

/** "2026-08-22T14:30:00" → "14:30" (vaqti yo'q bo'lsa bo'sh satr). */
export function fmtTime(iso: string): string {
  return (iso || '').slice(11, 16)
}

/**
 * "2026-08-22" → "22-avgust, shanba". Bugun bo'lsa "Bugun · 22-avgust".
 *
 * ⚠️ Bugungi kun ALOHIDA belgilanadi: kunlarga bo'lingan ro'yxatda operator "qaysi biri bugun"
 * ni sanalarni solishtirmasdan ko'rishi kerak.
 */
export function fmtDayTitle(day: string): string {
  if (!day || day.length < 10) return '—'
  const y = Number(day.slice(0, 4))
  const m = Number(day.slice(5, 7))
  const d = Number(day.slice(8, 10))
  if (!y || !m || !d) return day
  const label = `${d}-${MONTHS[m - 1] ?? ''}`
  if (day === todayIso()) return `Bugun · ${label}`
  // Hafta kunini faqat shu yerda `Date` bilan hisoblaymiz — natija KUNGA bog'liq, soatga emas.
  const weekday = WEEKDAYS[new Date(y, m - 1, d).getDay()]
  return `${label}, ${weekday}`
}

/**
 * 3600000 → "3.4 MB"; 0 → "noma'lum".
 *
 * ⚠️ `0 = noma'lum` kelishuvi backend bilan bir xil (`IgMediaItem`): fayl sarlavhasidan
 * o'qib bo'lmagan o'lcham "0 B" deb ko'rsatilsa foydalanuvchi uni haqiqiy qiymat deb o'ylardi.
 */
export function fmtBytes(bytes: number): string {
  if (!bytes || bytes <= 0) return "noma'lum"
  if (bytes < 1024) return `${bytes} B`
  const kb = bytes / 1024
  if (kb < 1024) return `${Math.round(kb * 10) / 10} KB`
  return `${Math.round((kb / 1024) * 10) / 10} MB`
}

/* ═══════════════════════════════════════ KUNLARGA GURUHLASH ═══════════════════════════════════════ */

/** Kun ("yyyy-MM-dd") → postlar soni (kalendar kataklari uchun). */
export function countsByDay(items: IgPost[]): Record<string, number> {
  const map: Record<string, number> = {}
  for (const p of items) {
    const d = (p.scheduledAt || '').slice(0, 10)
    if (d) map[d] = (map[d] ?? 0) + 1
  }
  return map
}

/**
 * Kunlarga guruhlaydi. Kunlar O'SISH tartibida, kun ichida ham vaqt bo'yicha o'sish tartibida.
 *
 * ⚠️ Navbat KELAJAKKA qaraydi — "eng yangisi tepada" qoidasi bu yerda ishlamaydi: operator
 * "bugun, keyin ertaga nima chiqadi" deb kunlar bo'ylab pastga o'qiydi.
 *
 * ⚠️ Sanasi bo'sh post guruhga UMUMAN tushmaydi: `scheduledAt` backendda majburiy, ya'ni
 * bo'sh qiymat ma'lumot buzuqligi — uni "bugun" ga qo'shib qo'yish chalg'itardi.
 */
export function groupByDay(items: IgPost[]): { day: string; items: IgPost[] }[] {
  const map = new Map<string, IgPost[]>()
  for (const p of items) {
    const d = (p.scheduledAt || '').slice(0, 10)
    if (!d) continue
    const bucket = map.get(d)
    if (bucket) bucket.push(p)
    else map.set(d, [p])
  }
  return [...map.entries()]
    .sort((a, b) => a[0].localeCompare(b[0]))
    .map(([day, rows]) => ({
      day,
      // ISO satrlar leksikografik solishtirilsa ham to'g'ri tartib beradi (`Date` shart emas).
      items: [...rows].sort((a, b) => (a.scheduledAt || '').localeCompare(b.scheduledAt || '')),
    }))
}

/* ═══════════════════════════════════════ FAYLNI BRAUZERDA O'LCHASH ═══════════════════════════════════════ */

/**
 * Tanlangan faylni BRAUZERDA o'lchaydi (serverga YUBORMAYDI).
 *
 * ⚠️ `URL.createObjectURL` bilan yaratilgan manzil har holatda `revokeObjectURL` bilan
 * bo'shatiladi — aks holda katta video butun sessiya davomida xotirada qolardi.
 */
export async function measureLocalFile(file: File): Promise<Partial<IgMediaItem>> {
  const isVideo = file.type.startsWith('video/')
  const src = URL.createObjectURL(file)
  try {
    if (isVideo) {
      const el = document.createElement('video')
      el.preload = 'metadata'
      await loadMedia(el, src)
      return {
        kind: 'video',
        sizeBytes: file.size,
        width: el.videoWidth,
        height: el.videoHeight,
        durationSeconds: Number.isFinite(el.duration) ? Math.round(el.duration * 10) / 10 : 0,
      }
    }
    const img = new Image()
    await loadMedia(img, src)
    return {
      kind: 'image',
      sizeBytes: file.size,
      width: img.naturalWidth,
      height: img.naturalHeight,
      durationSeconds: 0,
    }
  } finally {
    URL.revokeObjectURL(src)
  }
}

/** `load`/`error` hodisalarini Promise'ga o'raydi (video uchun `loadedmetadata`). */
function loadMedia(el: HTMLImageElement | HTMLVideoElement, src: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const ok = () => resolve()
    const fail = () => reject(new Error("Faylni o'qib bo'lmadi — format qo'llab-quvvatlanmaydi."))
    if (el instanceof HTMLVideoElement) el.addEventListener('loadedmetadata', ok, { once: true })
    else el.addEventListener('load', ok, { once: true })
    el.addEventListener('error', fail, { once: true })
    el.src = src
  })
}
