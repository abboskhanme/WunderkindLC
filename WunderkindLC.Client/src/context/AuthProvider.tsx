import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import type { User } from '@/types'
import posthog from '@/lib/posthog'
import { AuthContext } from './auth-context'
import { login as loginRequest, otpLogin, fetchMe } from '@/api/services/auth'
import { api, USE_MOCK } from '@/api/client'
import { linkTelegramChat } from '@/lib/telegramLink'

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

  // Telegram «📱 Panel» (Mini App) ichida kirilgan bo'lsa — bot chatini shu akkauntga bog'laymiz (telefon
  // so'ralmaydi, 2026-10-12). Oddiy brauzerda initData bo'sh — hech narsa qilinmaydi.
  const userId = user?.id
  useEffect(() => {
    if (!USE_MOCK && userId) void linkTelegramChat(userId)
  }, [userId])

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

  const value = useMemo(
    () => ({ user, isAuthenticated: !!user, login, loginWithCode, logout, updateUser }),
    [user, login, loginWithCode, logout, updateUser],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
