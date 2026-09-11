import axios, { AxiosError, type AxiosRequestConfig } from 'axios'
import posthog from '@/lib/posthog'
import { installApiCache } from './cache'

/**
 * Markaziy axios klienti.
 * Base URL .env faylidagi VITE_API_BASE_URL dan olinadi.
 * withCredentials: true — auth (`at`) va CSRF (`csrf`) cookie'lari har so'rovda avtomatik ketadi.
 * JWT endi localStorage'da EMAS, HttpOnly `at` cookie'sida — XSS token o'g'irlay olmaydi.
 */
export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
  // ⚠️ Timeout ATAYIN 120 s — 30 s EMAS. Axios standarti 0 (cheksiz) edi: backend osilib qolsa
  // so'rov nginx'ning `proxy_read_timeout 300s` chegarasigacha jim osilib turar, foydalanuvchi
  // esa 5 daqiqa "yuklanmoqda" ko'rib, xato ham ko'rmasdi.
  // Nega aynan 120: `GeminiService` HttpClient'i 90 s timeout bilan ishlaydi, ya'ni AI tahlil
  // so'rovi qonuniy ravishda ~90 s davom etishi mumkin. Kichikroq qiymat (30 s) AI tahlilni
  // BUZARDI. 120 s — Gemini shiftidan yuqori, nginx chegarasidan past.
  timeout: 120_000,
})

// SHAFFOF GET-KESH (25 s TTL + in-flight dedup) — adapter darajasida, qoidalar `cache.ts` da.
// Muvaffaqiyatli POST/PUT/PATCH/DELETE keshni RESURS BO'YICHA tozalaydi: mutatsiya manzilidan
// "scope" (masalan `/admin/students`) olinadi va faqat o'sha scope + `cache.ts` dagi
// CROSS_INVALIDATION xaritasida ko'rsatilgan bog'liq scope'lar o'chiriladi (masalan to'lov →
// o'quvchi balansi ham). Xaritada yo'q scope — EHTIYOT uchun butun keshni tozalaydi.
// Ilgari HAR QANDAY mutatsiya butun keshni o'chirardi: kassir bitta to'lov kiritsa o'quvchilar
// va guruhlar ro'yxati ham bekor bo'lib, 25 s kesh aynan eng band paytda ishlamasdi.
// Bitta so'rovda o'chirish: `api.get(url, { headers: { 'X-No-Cache': '1' } })`
// yoki `api.get(url, { cache: false })`. `/auth/`, blob va abort bo'lgan so'rovlar keshlanmaydi.
installApiCache(api)

/** `document.cookie` dan bitta cookie qiymatini o'qiydi (topilmasa null). */
export function readCookie(name: string): string | null {
  const prefix = `${name}=`
  for (const part of document.cookie.split('; ')) {
    if (part.startsWith(prefix)) return decodeURIComponent(part.slice(prefix.length))
  }
  return null
}

// Cookie rejimi: xavfsiz bo'lmagan (POST/PUT/PATCH/DELETE) so'rovlarga CSRF sarlavhasini qo'shamiz.
// Qiymati aynan `csrf` cookie qiymati — GET/HEAD/OPTIONS uchun kerak emas. Auth `at` cookie'si
// HttpOnly bo'lgani uchun bu yerda o'qilmaydi — brauzer uni withCredentials orqali o'zi yuboradi.
api.interceptors.request.use((config) => {
  const method = (config.method ?? 'get').toLowerCase()
  if (method === 'post' || method === 'put' || method === 'patch' || method === 'delete') {
    const csrf = readCookie('csrf')
    if (csrf) config.headers['X-CSRF-Token'] = csrf
  }
  return config
})

// So'rov konfiguratsiyasiga qo'shiladigan bayroq: bu so'rov refresh'dan keyin BIR MARTA
// qayta yuborilganini belgilaydi — aks holda cheksiz sikl bo'lardi.
type RetriableConfig = AxiosRequestConfig & { _retry?: boolean }

// SINGLE-FLIGHT: bir vaqtda kelgan bir nechta 401 uchun refresh FAQAT BIR MARTA chaqiriladi.
// Birinchi 401 shu promise'ni o'rnatadi, qolganlari SHUNI kutadi; tugagach null'ga qaytadi.
let refreshPromise: Promise<void> | null = null

/**
 * `POST /auth/refresh` — web rejimida `rt` HttpOnly cookie avtomatik ketadi (body kerak emas).
 * Muvaffaqiyatda server yangi `at`+`csrf`+`rt` cookie'larini o'rnatadi (biz body'ni e'tiborsiz
 * qoldiramiz). Xato bo'lsa promise reject bo'ladi. Single-flight'ni ta'minlash uchun mavjud
 * `refreshPromise` bo'lsa yangisini boshlamaymiz.
 */
function refreshSession(): Promise<void> {
  if (!refreshPromise) {
    refreshPromise = api
      .post('/auth/refresh')
      .then(() => undefined)
      .finally(() => {
        refreshPromise = null
      })
  }
  return refreshPromise
}

/** Sessiyani tozalab login sahifasiga qaytaramiz (refresh ham muvaffaqiyatsiz bo'lganda). */
function forceLogout(): void {
  posthog.reset()
  localStorage.removeItem('user')
  if (window.location.pathname !== '/login') {
    window.location.assign('/login')
  }
}

// Token tugagan/yaroqsiz bo'lsa (401):
//  - login/otp-login/refresh so'rovlaridan tashqari so'rovlarda avval `/auth/refresh` orqali
//    access token'ni yangilashga urinamiz va original so'rovni BIR MARTA qayta yuboramiz;
//  - refresh muvaffaqiyatsiz bo'lsa — sessiyani tozalab login sahifasiga qaytaramiz.
// Login/otp-login/refresh so'rovining o'zidagi 401 bundan mustasno (parol noto'g'ri / sessiya
// tugagan) — refresh chaqirilmaydi, cheksiz sikl bo'lmaydi.
api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const status = error.response?.status
    const config = error.config as RetriableConfig | undefined
    const url: string = config?.url ?? ''
    const isAuthCall =
      url.includes('/auth/login') ||
      url.includes('/auth/otp-login') ||
      url.includes('/auth/refresh')

    // Refresh yo'li bilan tiklab bo'lmaydigan holatlar: 401 emas, auth so'rovi, config yo'q,
    // yoki bu so'rov allaqachon bir marta qayta urinilgan.
    if (status !== 401 || isAuthCall || !config || config._retry) {
      if (status === 401 && !isAuthCall) forceLogout()
      return Promise.reject(error)
    }

    try {
      // Single-flight: parallel 401'lar bitta refresh'ni kutadi.
      await refreshSession()
    } catch {
      // Refresh muvaffaqiyatsiz (masalan `rt` ham tugagan) — sessiyani tozalaymiz.
      forceLogout()
      return Promise.reject(error)
    }

    // Refresh muvaffaqiyatli: yangi `at` cookie server tomonidan o'rnatildi, original so'rovni
    // BIR MARTA (`_retry`) qayta yuboramiz — cookie brauzer orqali avtomatik ketadi.
    config._retry = true
    return api(config)
  },
)

/**
 * Backend hali tayyor bo'lmagani uchun vaqtinchalik mock rejim.
 * .env da VITE_USE_MOCK=false qilinsa, haqiqiy API ishlatiladi.
 */
export const USE_MOCK = import.meta.env.VITE_USE_MOCK !== 'false'
