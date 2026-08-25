import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import type { User } from '@/types'
import posthog from '@/lib/posthog'
import { AuthContext } from './auth-context'
import { login as loginRequest, otpLogin, fetchMe } from '@/api/services/auth'
import { api, USE_MOCK } from '@/api/client'
import { setFcmToken, getFcmToken, registerDevice, unregisterDevice, pushBase } from '@/api/services/push'
import { initWebPush, isWebPushSupported } from '@/api/services/webpush'

// JWT endi localStorage'da EMAS — HttpOnly `at` cookie'sida (XSS token o'g'irlay olmaydi).
// Faqat `user` optimistik startup uchun saqlanadi (maxfiy sir emas), haqiqat manbai — GET /auth/me.
const USER_KEY = 'user'

function readStoredUser(): User | null {
  try {
    const raw = localStorage.getItem(USER_KEY)
    return raw ? (JSON.parse(raw) as User) : null
  } catch {
    return null
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  // Sahifa yangilanganda user'ni localStorage'dan OPTIMISTIK tiklaymiz (tez startup).
  // Sessiyaning haqiqiyligi keyin GET /auth/me bilan tasdiqlanadi (pastdagi effekt).
  const [user, setUser] = useState<User | null>(() => readStoredUser())
  const identifiedUserId = useRef<string | null>(null)

  const identifyUser = useCallback((u: User) => {
    if (!u.id || identifiedUserId.current === u.id) return

    if (identifiedUserId.current) posthog.reset()

    posthog.identify(u.id, {
      name: u.fullName,
      role: u.role,
      ...(u.email ? { email: u.email } : {}),
    })
    identifiedUserId.current = u.id
  }, [])

  const logout = useCallback(() => {
    // Push: qurilma tokenini o'chirib qo'yamiz. Auth `at` cookie'si hali o'chirilmagan bo'lishi
    // uchun buni logout endpointidan OLDIN qilamiz — so'rov cookie bilan avtomatik avtorizatsiyalanadi.
    const role = readStoredUser()?.role
    const fcm = getFcmToken()
    if (role && fcm) unregisterDevice(role, fcm).catch(() => {})

    // Serverga chiqish signalini yuboramiz — u `at`, `csrf` va `up_at` cookie'larini o'chiradi
    // (umumiy kompyuterda keyingi odam login'siz kira olmasin). Best-effort: server yetib
    // bormasa ham (offline va h.k.) foydalanuvchi baribir chiqishi shart, shuning uchun xatoni
    // yutib yuboramiz va quyidagi klient tozalash HAR HOLDA bajariladi.
    if (!USE_MOCK) api.post('/auth/logout').catch(() => {})

    posthog.reset()
    identifiedUserId.current = null
    localStorage.removeItem(USER_KEY)
    setUser(null)
  }, [])

  // Login/OTP ikkalasi ham bir xil "token + user" natija shakli qaytaradi, lekin WEB tokenni
  // SAQLAMAYDI: server `at`+`csrf` cookie'larini javobda avtomatik o'rnatgan. Shuning uchun
  // applySession faqat user'ni oladi.
  const applySession = useCallback((u: User) => {
    localStorage.setItem(USER_KEY, JSON.stringify(u))
    identifyUser(u)
    setUser(u)
    // Push: Flutter token mavjud bo'lsa — qurilmani shu foydalanuvchiga bog'laymiz.
    const fcm = getFcmToken()
    if (fcm) registerDevice(u.role, fcm).catch(() => {})
    return u
  }, [identifyUser])

  const login = useCallback(
    async (email: string, password: string) => {
      const { user: u } = await loginRequest(email, password)
      return applySession(u)
    },
    [applySession],
  )

  const loginWithCode = useCallback(
    async (code: string) => {
      const { user: u } = await otpLogin(code)
      return applySession(u)
    },
    [applySession],
  )

  const updateUser = useCallback((u: User) => {
    localStorage.setItem(USER_KEY, JSON.stringify(u))
    setUser(u)
  }, [])

  // Saqlangan sessiya bilan sahifa yangilanganda PostHog identifikatsiyasini tiklaymiz.
  useEffect(() => {
    if (user) identifyUser(user)
  }, [identifyUser, user])

  // Real rejimda sessiyani startupda /me bilan tekshiramiz. Token HttpOnly cookie'da bo'lgani uchun
  // JS uni ko'rmaydi — haqiqat manbai `at` cookie bilan ketadigan GET /auth/me javobi.
  // Optimistik saqlangan user bo'lmasa (mehmon), /me ni bejiz chaqirmaymiz: 401 interceptori
  // login sahifasida keraksiz redirect/tozalashga urinardi.
  useEffect(() => {
    if (USE_MOCK || !readStoredUser()) return
    fetchMe()
      .then((u) => {
        localStorage.setItem(USER_KEY, JSON.stringify(u))
        identifyUser(u)
        setUser(u)
      })
      .catch(() => logout())
  }, [identifyUser, logout])

  // Web/PWA push: NATIVE token bo'lmasa (oddiy brauzer/PWA), student/teacher kirganda Firebase JS
  // SDK orqali FCM token olib, joriy foydalanuvchiga bog'laymiz. Native ilovada window.__FCM_TOKEN__
  // bo'lgani uchun bu blok o'tkazib yuboriladi. initWebPush idempotent (tokenni keshlaydi).
  useEffect(() => {
    if (!user) return
    if (getFcmToken()) return // native token mavjud — web push shart emas
    if (!pushBase(user.role)) return // faqat student/teacher'da register endpointi bor
    if (!isWebPushSupported()) return
    const role = user.role
    initWebPush()
      .then((token) => {
        if (!token) return
        setFcmToken(token)
        registerDevice(role, token, 'web').catch(() => {})
      })
      .catch(() => {})
  }, [user])

  // Flutter native FCM tokenini eshitamiz: window.postMessage({type:'fcm-token', token})
  // yoki window.__FCM_TOKEN__. Token kelganda — agar kirilgan bo'lsa darhol register qilamiz
  // (token yangilanganda ham qayta bog'lanadi).
  useEffect(() => {
    const tryRegister = (token: string) => {
      setFcmToken(token)
      const u = readStoredUser()
      // Sessiya bor-yo'qligini cookie'dan bila olmaymiz; saqlangan user bo'lsa register qilamiz —
      // sessiya haqiqiy bo'lmasa so'rov 401 bo'lib jimgina yutiladi.
      if (u) registerDevice(u.role, token).catch(() => {})
    }
    // Flutter to'g'ridan-to'g'ri chaqirishi uchun global funksiya (eng ishonchli yo'l).
    window.registerFcmToken = (token: string) => {
      if (typeof token === 'string' && token) tryRegister(token)
    }
    // Flutter ilova ochilishida window.__FCM_TOKEN__ ni oldindan qo'ygan bo'lishi mumkin.
    const initial = getFcmToken()
    if (initial) tryRegister(initial)

    const onMsg = (e: MessageEvent) => {
      const d = e.data as { type?: string; token?: string } | null
      if (d && d.type === 'fcm-token' && typeof d.token === 'string' && d.token) tryRegister(d.token)
    }
    window.addEventListener('message', onMsg)
    return () => {
      window.removeEventListener('message', onMsg)
      delete window.registerFcmToken
    }
  }, [])

  const value = useMemo(
    () => ({ user, isAuthenticated: !!user, login, loginWithCode, logout, updateUser }),
    [user, login, loginWithCode, logout, updateUser],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
