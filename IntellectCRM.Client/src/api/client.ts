import axios from 'axios'
import posthog from '@/lib/posthog'

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
})

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

// Token tugagan/yaroqsiz bo'lsa (401) — sessiyani tozalab login sahifasiga qaytaramiz.
// Login so'rovining o'zidagi 401 (parol noto'g'ri) bundan mustasno — uni LoginPage ko'rsatadi.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    const status = error.response?.status
    const url: string = error.config?.url ?? ''
    const isLoginCall = url.includes('/auth/login')
    if (status === 401 && !isLoginCall) {
      posthog.reset()
      localStorage.removeItem('user')
      if (window.location.pathname !== '/login') {
        window.location.assign('/login')
      }
    }
    return Promise.reject(error)
  },
)

/**
 * Backend hali tayyor bo'lmagani uchun vaqtinchalik mock rejim.
 * .env da VITE_USE_MOCK=false qilinsa, haqiqiy API ishlatiladi.
 */
export const USE_MOCK = import.meta.env.VITE_USE_MOCK !== 'false'
