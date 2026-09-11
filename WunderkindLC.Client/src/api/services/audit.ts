import type { AuditLog } from '@/types'
import { delay } from '@/lib/utils'
import { api, USE_MOCK } from '../client'

export interface AuditFilters {
  /** FinanceTransaction | TeacherSalary | ClassFee | ... (texnik tur) */
  entityType?: string
  /** Bitta yozuv tarixi uchun */
  entityId?: string
  /** O'quvchiga oid o'zgarishlar */
  studentId?: string
  /** O'qituvchiga oid o'zgarishlar */
  teacherId?: string
  /** Guruhga oid o'zgarishlar (guruh yozuvining o'zi + a'zolik hodisalari — faollashtirish/muzlatish/ko'chirish/chiqarish) */
  groupId?: string
  /**
   * BO'LIM kaliti (`students` | `classes` | `finance` | ... | `other`). Turdan farqi: bo'lim —
   * foydalanuvchi ko'radigan bo'lim, bir bo'limga bir nechta texnik tur tushadi. Xarita SERVERDA
   * (`AuditSections`) — klient uni o'zi hisoblamaydi.
   */
  section?: string
  /** Xodim (ActorName) bo'yicha — "kim o'zgartirgan". */
  actor?: string
  /** Izoh (summary) ichidan qidiruv. */
  q?: string
  action?: string
  from?: string
  to?: string
  limit?: number
  offset?: number
}

/** O'zgarishlar tarixini olish (filtrlar bo'yicha, vaqt kamayish tartibida) */
export async function getAuditLogs(filters: AuditFilters = {}): Promise<AuditLog[]> {
  if (USE_MOCK) {
    await delay()
    return []
  }
  const { data } = await api.get<AuditLog[]>('/admin/audit', { params: filters })
  return data
}

/** Tarixdagi bitta bo'lim (chip): kalit, nom va shu filtrlardagi yozuvlar soni. */
export interface AuditSection {
  key: string
  label: string
  count: number
}

export interface AuditSectionsResult {
  sections: AuditSection[]
  /** Barcha bo'limlar bo'yicha jami — "Hammasi" chipi uchun. */
  total: number
  /** Tarixda uchragan xodim nomlari (filtr ro'yxati). */
  actors: string[]
}

/**
 * Bo'limlar + har birida nechta yozuv borligi. Sanoq AYNAN uzatilgan filtrlar (davr/qidiruv/
 * xodim/amal) bo'yicha, ya'ni chipdagi son ochilganda chiqadigan son bilan bir xil.
 */
export async function getAuditSections(
  filters: Pick<AuditFilters, 'action' | 'from' | 'to' | 'actor' | 'q'> = {},
): Promise<AuditSectionsResult> {
  if (USE_MOCK) {
    await delay()
    return { sections: [], total: 0, actors: [] }
  }
  const { data } = await api.get<AuditSectionsResult>('/admin/audit/sections', { params: filters })
  return data
}
