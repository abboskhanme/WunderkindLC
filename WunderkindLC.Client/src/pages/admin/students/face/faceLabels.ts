import type { BadgeTone } from '@/components/ui/Badge'
import type { FaceCheckStatus } from '@/api/services/face'

/**
 * YUZ BILAN KIRISH — yorliqlar va SOF funksiyalar (komponentlarda xom satr bo'lmasin).
 * Testlangan: `__tests__/faceLabels.test.ts`.
 */

// ---------- Holat ----------

export function statusLabel(status: FaceCheckStatus | string): string {
  switch (status) {
    case 'approved':
      return 'Tasdiqlangan'
    case 'rejected':
      return 'Rad etilgan'
    case 'pending':
      return 'Kutilmoqda'
    default:
      return status || '—'
  }
}

/** `Badge` uchun rang. Noma'lum holat kulrang bo'ladi — yo'qolib ketmaydi. */
export function statusTone(status: FaceCheckStatus | string): BadgeTone {
  switch (status) {
    case 'approved':
      return 'green'
    case 'rejected':
      return 'red'
    case 'pending':
      return 'amber'
    default:
      return 'default'
  }
}

// ---------- Ball (kosinus → foiz) ----------

/**
 * Kosinus o'xshashligini foizga aylantiradi: `0.83` → `"83%"`.
 *
 * ⚠️ Solishtirish umuman bo'lmagan urinishda (masalan `pending` — etalon ham, profil rasmi ham
 * yo'q) server `null` beradi: bu "0%" EMAS, "hisoblanmagan" — shuning uchun chiziqcha chiqadi.
 * Manfiy kosinus (nazariy jihatdan -1 gacha) 0% ga qisiladi.
 */
export function scorePercent(score: number | null | undefined): string {
  if (score === null || score === undefined || !Number.isFinite(score)) return '—'
  const pct = Math.round(Math.min(1, Math.max(0, score)) * 100)
  return `${pct}%`
}

/** Ball chegaradan o'tganmi — jadvalda rang berish uchun (chegara sozlamalardan keladi). */
export function scorePassed(score: number | null | undefined, threshold: number): boolean {
  return typeof score === 'number' && Number.isFinite(score) && score >= threshold
}

// ---------- Platforma / qurilma ----------

export function platformLabel(platform: string): string {
  const p = (platform || '').trim().toLowerCase()
  if (p === 'android') return 'Android'
  if (p === 'ios' || p === 'iphone' || p === 'ipados') return 'iOS'
  if (p === 'web') return 'Brauzer'
  return platform?.trim() || '—'
}

// ---------- Kadr sifati ----------

/**
 * Klient (telefon) o'lchagan kadr sifati. Barcha maydonlar IXTIYORIY: eski ilova ularni
 * yubormasligi mumkin, shuning uchun yo'q maydon ko'rsatilmaydi (nol deb hisoblanmaydi).
 */
export interface FaceQualityInfo {
  /** Kadrda topilgan yuzlar soni */
  faces?: number
  /** Aniqlik 0..1 */
  sharpness?: number
  /** O'rtacha yorug'lik 0..1 */
  brightness?: number
  /** Yuz kadrning qancha qismini egallagan 0..1 */
  faceRatio?: number
  /** Chapga/o'ngga burilish (gradus, ±) */
  yaw?: number
  /** Yon egilish (gradus, ±) */
  roll?: number
  eyesOpen?: boolean
}

/**
 * QABUL CHEGARALARI — backend `FaceMatch.DefaultLimits` NUSXASI.
 * ⚠️ Bu yerda faqat KO'RSATISH uchun ishlatiladi (qaysi ko'rsatkich "yomon" bo'lganini
 * bo'yash). Haqiqiy qaror har doim serverda qabul qilinadi; chegaralar o'zgarsa bu yerdagi
 * qiymatlar ham yangilansin, aks holda ranglar chalg'itadi.
 */
export const QUALITY_LIMITS = {
  minSharpness: 0.15,
  minBrightness: 0.2,
  maxBrightness: 0.92,
  minFaceRatio: 0.06,
  maxYaw: 25,
  maxRoll: 25,
} as const

/** JSON'dan chiqqan qiymatni songa keltiradi (raqam yoki raqamli matn); aks holda `undefined`. */
function num(v: unknown): number | undefined {
  if (typeof v === 'number') return Number.isFinite(v) ? v : undefined
  if (typeof v === 'string' && v.trim() !== '') {
    const n = Number(v)
    return Number.isFinite(n) ? n : undefined
  }
  return undefined
}

function flag(v: unknown): boolean | undefined {
  if (typeof v === 'boolean') return v
  if (typeof v === 'number') return Number.isFinite(v) ? v !== 0 : undefined
  return undefined
}

/**
 * `quality` JSON MATNINI o'qiydi.
 *
 * ⚠️ HECH QACHON istisno tashlamaydi: bo'sh, buzuq yoki obyekt bo'lmagan JSON (`"[]"`, `"5"`,
 * `"null"`) — hammasi `null` qaytaradi va UI "sifat ma'lumoti yo'q" deb ko'rsatadi. Sifat
 * ma'lumoti QULAYLIK uchun, xavfsizlik uchun emas — u tufayli sahifa yiqilmasligi kerak.
 */
export function parseQuality(raw: string | null | undefined): FaceQualityInfo | null {
  if (!raw || !raw.trim()) return null
  let parsed: unknown
  try {
    parsed = JSON.parse(raw)
  } catch {
    return null
  }
  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return null

  const o = parsed as Record<string, unknown>
  const info: FaceQualityInfo = {
    faces: num(o.faces),
    sharpness: num(o.sharpness),
    brightness: num(o.brightness),
    faceRatio: num(o.faceRatio),
    yaw: num(o.yaw),
    roll: num(o.roll),
    eyesOpen: flag(o.eyesOpen),
  }
  // Bitta ham tanish maydon bo'lmasa — "ma'lumot yo'q" bilan bir xil (bo'sh chiplar chizilmasin).
  const hasAny = Object.values(info).some((v) => v !== undefined)
  return hasAny ? info : null
}

/** Ko'rsatiladigan bitta ko'rsatkich. `ok=false` — chegaradan chiqqan (qizil chiziladi). */
export interface FaceQualityMetric {
  key: string
  label: string
  value: string
  ok: boolean
}

const pct = (v: number) => `${Math.round(v * 100)}%`
const deg = (v: number) => `${Math.round(v)}°`

/**
 * Sifat ko'rsatkichlarini O'QILADIGAN ro'yxatga aylantiradi (xom JSON ekranga chiqmasin).
 * Faqat MAVJUD maydonlar qaytadi — yo'qini "0" deb ko'rsatish yolg'on bo'lardi.
 */
export function qualityMetrics(q: FaceQualityInfo | null): FaceQualityMetric[] {
  if (!q) return []
  const out: FaceQualityMetric[] = []

  if (q.faces !== undefined) {
    out.push({
      key: 'faces',
      label: 'Yuzlar',
      value: q.faces === 1 ? '1 ta' : `${q.faces} ta`,
      ok: q.faces === 1,
    })
  }
  if (q.sharpness !== undefined) {
    out.push({
      key: 'sharpness',
      label: 'Tiniqlik',
      value: pct(q.sharpness),
      ok: q.sharpness >= QUALITY_LIMITS.minSharpness,
    })
  }
  if (q.brightness !== undefined) {
    out.push({
      key: 'brightness',
      label: "Yorug'lik",
      value: pct(q.brightness),
      ok:
        q.brightness >= QUALITY_LIMITS.minBrightness &&
        q.brightness <= QUALITY_LIMITS.maxBrightness,
    })
  }
  if (q.faceRatio !== undefined) {
    out.push({
      key: 'faceRatio',
      label: "Yuz o'lchami",
      value: pct(q.faceRatio),
      ok: q.faceRatio >= QUALITY_LIMITS.minFaceRatio,
    })
  }
  if (q.yaw !== undefined) {
    out.push({
      key: 'yaw',
      label: 'Burilish',
      value: deg(q.yaw),
      ok: Math.abs(q.yaw) <= QUALITY_LIMITS.maxYaw,
    })
  }
  if (q.roll !== undefined) {
    out.push({
      key: 'roll',
      label: 'Egilish',
      value: deg(q.roll),
      ok: Math.abs(q.roll) <= QUALITY_LIMITS.maxRoll,
    })
  }
  if (q.eyesOpen !== undefined) {
    out.push({
      key: 'eyesOpen',
      label: "Ko'zlar",
      value: q.eyesOpen ? 'ochiq' : 'yumuq',
      ok: q.eyesOpen,
    })
  }

  return out
}

// ---------- Etalon manbai ----------

export function faceSourceLabel(source: string): string {
  switch (source) {
    case 'photo':
      return 'Profil rasmidan'
    case 'admin':
      return 'Administrator tasdiqlagan selfidan'
    default:
      return source || '—'
  }
}

// ---------- Takrorlanadigan izohlar (bir joyda) ----------

export const FACE_APPROVE_HINT =
  "Tasdiqlansa shu selfi ETALON bo'ladi — keyingi kirishlar shunga solishtiriladi."

export const FACE_PRIVACY_NOTE =
  "Selfi — BIOMETRIK ma'lumot. Har bir o'quvchidan faqat oxirgi bir nechta urinish saqlanadi " +
  "(quyidagi «Saqlanadigan selfilar» soni), eskilari yozuvi bilan birga avtomatik o'chiriladi."
