import type { User } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

export interface LoginResult {
  /** Backend hamon qaytaradi (mobil ilova uchun), lekin WEB uni SAQLAMAYDI —
   *  auth `at` va CSRF `csrf` cookie'lari server tomonidan avtomatik o'rnatiladi. */
  token: string
  user: User
}

/** USE_MOCK rejimida ishlatiladigan demo foydalanuvchilar (backend seed bilan bir xil). */
const mockAccounts: Record<string, { password: string; user: User }> = {
  'admin@markaz.uz': {
    password: 'admin123',
    user: { id: 'a1', fullName: 'Aziz Karimov', role: 'admin', email: 'admin@markaz.uz' },
  },
  'teacher@markaz.uz': {
    password: 'teacher123',
    user: { id: 't1u', fullName: 'Dilnoza Saidova', role: 'teacher', email: 'teacher@markaz.uz' },
  },
  'student@markaz.uz': {
    password: 'student123',
    user: { id: 's1u', fullName: 'Jasur Tohirov', role: 'student', email: 'student@markaz.uz' },
  },
  'parent@markaz.uz': {
    password: 'parent123',
    user: { id: 'p1u', fullName: 'Nodira Yusupova', role: 'parent', email: 'parent@markaz.uz' },
  },
}

export async function login(email: string, password: string): Promise<LoginResult> {
  if (USE_MOCK) {
    await delay(400)
    const entry = mockAccounts[email.trim().toLowerCase()]
    if (!entry || entry.password !== password) {
      throw new Error("Email yoki parol noto'g'ri")
    }
    return { token: `mock-token-${entry.user.role}`, user: entry.user }
  }
  const { data } = await api.post<LoginResult>('/auth/login', { email, password })
  return data
}

/** Botdan olingan bir martalik kod bilan login (parol o'rniga) — natija shakli oddiy login bilan bir xil. */
export async function otpLogin(code: string): Promise<LoginResult> {
  if (USE_MOCK) {
    await delay(400)
    throw new Error("Kod bilan kirish demo rejimida mavjud emas")
  }
  const { data } = await api.post<LoginResult>('/auth/otp-login', { code })
  return data
}

/** Joriy tokenga mos foydalanuvchini olish (sessiyani tekshirish uchun). */
export async function fetchMe(): Promise<User> {
  const { data } = await api.get<User>('/auth/me')
  return data
}

export interface UpdateAccountPayload {
  /** Yangi login (email); o'zgarmasa joriysi yuboriladi */
  email?: string
  /** Tasdiqlash uchun joriy parol */
  currentPassword: string
  /** Yangi parol; bo'sh bo'lsa parol o'zgarmaydi */
  newPassword?: string
  /** Telefon — admin botda yangi lid xabarnomasini olishi uchun (bo'sh = o'zgarmaydi) */
  phone?: string
}

/** Joriy foydalanuvchi o'z login/parolini o'zgartiradi — yangilangan foydalanuvchini qaytaradi. */
export async function updateAccount(payload: UpdateAccountPayload): Promise<User> {
  if (USE_MOCK) {
    await delay(300)
    return {
      id: 'a1',
      fullName: 'Administrator',
      role: 'admin',
      email: payload.email ?? 'admin@markaz.uz',
    }
  }
  const { data } = await api.put<User>('/auth/account', payload)
  return data
}
