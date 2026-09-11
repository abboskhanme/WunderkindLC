import { api } from '../client'

/* ============================================================
   Push qurilma tokeni (FCM) ro'yxati — Flutter WebView ilova uchun.
   Token NATIVE Flutter tarafda olinadi va WebView'ga uzatiladi:
     • window.__FCM_TOKEN__ = "<token>"   yoki
     • window.postMessage({ type: 'fcm-token', token: '<token>' }, '*')
   Web (AuthProvider) login'da register, logout'da unregister qiladi —
   token doim OXIRGI kirgan foydalanuvchiga bog'lanadi.
   ============================================================ */

declare global {
  interface Window {
    __FCM_TOKEN__?: string
    /** Flutter to'g'ridan-to'g'ri chaqiradi: controller.runJavaScript("window.registerFcmToken('<token>')"). */
    registerFcmToken?: (token: string) => void
  }
}

let fcmToken: string | null = null

/** Flutter'dan kelgan FCM tokenini saqlash. */
export function setFcmToken(token: string) {
  fcmToken = token
  try {
    window.__FCM_TOKEN__ = token
  } catch {
    /* sandbox */
  }
}

/** Joriy FCM token (postMessage yoki window.__FCM_TOKEN__ dan). */
export function getFcmToken(): string | null {
  return fcmToken || window.__FCM_TOKEN__ || null
}

/** Rol bo'yicha push endpoint bazasi. Faqat student va teacher'da register endpointi bor. */
export function pushBase(role: string | undefined): string | null {
  if (role === 'teacher') return '/teacher'
  if (role === 'student') return '/student'
  return null // parent/admin/staff — register endpointi yo'q
}

/** Qurilma tokenini joriy foydalanuvchiga ro'yxatdan o'tkazadi (JWT axios interceptordan).
 *  platform: 'android' (native Flutter) yoki 'web' (brauzer/PWA). */
export async function registerDevice(
  role: string | undefined,
  token: string,
  platform: 'android' | 'web' = 'android',
): Promise<void> {
  const base = pushBase(role)
  if (!base || !token) return
  await api.post(`${base}/notifications/register`, {
    token,
    platform,
    deviceName: (navigator.userAgent || '').slice(0, 80),
  })
}

/** Qurilma tokenini o'chiradi. So'rov `at` cookie'si bilan avtorizatsiyalanadi (withCredentials);
 *  shuning uchun logout endpointidan OLDIN, cookie hali o'chirilmagan paytda chaqiriladi. */
export async function unregisterDevice(role: string | undefined, token: string): Promise<void> {
  const base = pushBase(role)
  if (!base || !token) return
  await api.delete(`${base}/notifications/register`, {
    params: { token },
  })
}
