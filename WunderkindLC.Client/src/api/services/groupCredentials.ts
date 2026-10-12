import { localIsoDate } from '@/lib/localDate'
import { api, USE_MOCK } from '../client'

/**
 * Guruh «Login/parollar» (CRM Phase 6): guruhning tirik a'zolari login va JORIY paroli bilan.
 * Faqat `HasFullAccess("students.list")` (admin/superadmin yoki to'liq «O'quvchilar» ruxsati) — boshqalarga 403.
 */
export interface GroupCredentialRow {
  studentId: string
  fullName: string
  /** a'zolik holati: active | trial | frozen */
  status: string
  yearFreeze: boolean
  /** tizim akkaunti bormi (yo'q bo'lsa login bo'sh) */
  hasAccount: boolean
  login: string
  /** `known = false` bo'lsa null */
  password: string | null
  known: boolean
  source: string
  /** bog'langan Telegram chatlari soni */
  telegramChats: number
}

export interface GroupCredentials {
  vaultConfigured: boolean
  groupName: string
  rows: GroupCredentialRow[]
}

export interface GroupCredentialsEnsureResult extends GroupCredentials {
  created: number
  regenerated: number
}

export async function getGroupCredentials(groupId: string): Promise<GroupCredentials> {
  if (USE_MOCK) return { vaultConfigured: true, groupName: 'Demo', rows: [] }
  const { data } = await api.get<GroupCredentials>(`/admin/classes/${groupId}/credentials`)
  return data
}

/** Akkaunti yo'qlarga akkaunt + paroli NOMA'LUMLARGA yangi parol (ularning eski paroli ishlamay qoladi). */
export async function ensureGroupCredentials(groupId: string): Promise<GroupCredentialsEnsureResult> {
  const { data } = await api.post<GroupCredentialsEnsureResult>(`/admin/classes/${groupId}/credentials/ensure`, {
    onlyUnknown: true,
  })
  return data
}

/** «№, F.I.SH., Login, Parol, Telegram, Holat» Excel fayli (serverda auditga yoziladi). */
export async function downloadGroupCredentials(groupId: string): Promise<void> {
  if (USE_MOCK) {
    alert('Eksport faqat real serverda ishlaydi (VITE_USE_MOCK=false).')
    return
  }
  const res = await api.get(`/admin/classes/${groupId}/credentials/export`, { responseType: 'blob' })
  const url = URL.createObjectURL(res.data as Blob)
  const a = document.createElement('a')
  a.href = url
  const cd = (res.headers['content-disposition'] as string | undefined) ?? ''
  // RFC 5987 (`filename*=UTF-8''...`) — guruh nomida kirill/apostrof bo'lsa server shu shaklda beradi.
  const star = cd.match(/filename\*=UTF-8''([^;]+)/i)
  const plain = cd.match(/filename="?([^";]+)"?/)
  a.download = star ? decodeURIComponent(star[1]) : plain?.[1] ?? `login_parollar_${localIsoDate(new Date())}.xlsx`
  document.body.appendChild(a)
  a.click()
  a.remove()
  URL.revokeObjectURL(url)
}
