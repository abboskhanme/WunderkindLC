import { api } from '../client'

/**
 * «Davomat xabarlari» nazorati (CRM Phase 3, U3b) — «kelmadi»/«keldi» xabarlari navbati, menejer
 * to'xtatadi / tasdiqlaydi / bekor qiladi. Ruxsat — `students.notices` (o'qish ham).
 */
export type NoticeStatus =
  | 'pending'
  | 'awaiting'
  | 'held'
  | 'approved'
  | 'sending'
  | 'done'
  | 'failed'
  | 'unknown'
  | 'expired'
  | 'cancelled'

export type NoticeAction = 'hold' | 'approve' | 'cancel'

/** Holat chiplari kalitlari (server `AbsentNoticeRules.CountKey`). */
export type NoticeCountKey = 'pending' | 'awaiting' | 'held' | 'approved' | 'sent' | 'failed' | 'cancelled' | 'expired'

export type NoticeCounts = Record<NoticeCountKey, number>

export interface AbsentNoticeItem {
  id: string
  kind: 'absent' | 'present'
  studentId: string
  studentName: string
  reasonName: string
  isExcused: boolean
  status: NoticeStatus
  /** Eng erta yuborish vaqti (markaz vaqti, ISO). */
  sendAfter: string
  decidedByName: string | null
  decidedAt: string | null
  note: string | null
  /** `manager` · `changed` (jurnal o'zgardi) · `archived`. */
  cancelReason: string | null
  period: number
  /** Telegram yetkazish (outbox) — xabar navbatga tushmagan bo'lsa null. */
  delivery: { sent: number; failed: number; blocked: number; acked: number } | null
}

export interface AbsentNoticeLesson {
  groupId: string
  groupName: string
  courseName: string
  teacherName: string
  lessonStart: string
  lessonEnd: string
  attendanceTakenAt: string | null
  takenLate: boolean
  items: AbsentNoticeItem[]
}

export interface AbsentNoticeList {
  date: string
  counts: NoticeCounts
  lessons: AbsentNoticeLesson[]
}

export async function getAbsentNotices(params: {
  date?: string
  status?: NoticeCountKey | ''
  groupId?: string
  teacherId?: string
}): Promise<AbsentNoticeList> {
  const { data } = await api.get<AbsentNoticeList>('/admin/attendance-notices', {
    params: { ...params, status: params.status || undefined },
  })
  return data
}

/** Faqat sanoqlar (bosh sahifa vidjeti). */
export async function getAbsentNoticeSummary(date?: string): Promise<{ date: string; counts: NoticeCounts }> {
  const { data } = await api.get<{ date: string; counts: NoticeCounts }>('/admin/attendance-notices/summary', {
    params: { date },
  })
  return data
}

export async function actOnAbsentNotice(id: string, action: NoticeAction, note?: string): Promise<AbsentNoticeItem> {
  const { data } = await api.put<AbsentNoticeItem>(`/admin/attendance-notices/${id}/${action}`, { note: note || null })
  return data
}

export async function bulkAbsentNotices(
  ids: string[],
  action: NoticeAction,
  note?: string,
): Promise<{ changed: number; skipped: number }> {
  const { data } = await api.put<{ changed: number; skipped: number }>('/admin/attendance-notices/bulk', {
    ids,
    action,
    note: note || null,
  })
  return data
}
