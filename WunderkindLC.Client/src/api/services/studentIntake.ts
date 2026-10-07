import { api } from '../client'

/*
 * QABUL CHEKLISTI (yangi o'quvchi) — server: `StudentIntakeController` (`/admin/student-intake`),
 * hisob — `StudentIntake` (Application/Services). Qoidalar: `.claude/rules/student-intake.md`.
 *
 * ⚠️ Holat (avtomatik/qo'lda, tafsilot matni, yetishmayotgan maydonlar) SERVERDA hisoblanadi —
 * klient uni QAYTA hisoblamaydi, faqat ko'rsatadi va filtrlaydi (`lib/intake.ts`).
 */

/** Band kaliti — tartib ham shu (serverdagi `StudentIntake.Items`). */
export type IntakeItemKey = 'profile' | 'levelTest' | 'contract' | 'telegram' | 'payment'

/** Profilda yetishmayotgan maydon. `key`: phone | birthDate | schoolGrade | group. */
export interface IntakeField {
  key: string
  label: string
}

export interface IntakeMark {
  note: string
  markedBy: string
  /** "yyyy-MM-ddTHH:mm:ss" (markaz vaqti). */
  markedAt: string
}

export interface IntakeItem {
  key: IntakeItemKey
  label: string
  /** Bajarilgan — avtomatik YOKI qo'lda. */
  done: boolean
  /** Mavjud yozuvlardan avtomatik aniqlandi. */
  auto: boolean
  /** "№ 12 · 05.10.2026", "Yetishmaydi: Sinf" ... */
  detail: string
  /** Faqat `profile` bandida. */
  missing: IntakeField[]
  mark: IntakeMark | null
}

export interface StudentIntake {
  studentId: string
  fullName: string
  phone: string
  groups: string[]
  memberState: string
  /** Qabul sanasi "yyyy-MM-dd". */
  since: string
  done: number
  total: number
  complete: boolean
  items: IntakeItem[]
  /** O'quvchiga aylantirilgan lid (daraja testi yuborish uchun). */
  leadId: string | null
}

export interface StudentIntakeDetail {
  checklist: StudentIntake
  /** Telegram bot username (@siz), sozlanmagan bo'lsa bo'sh. */
  telegramBot: string
}

export interface StudentIntakeList {
  items: StudentIntake[]
  days: number
  from: string
  total: number
  incomplete: number
  /** Band → nechta o'quvchida BAJARILMAGAN. */
  pendingByItem: Record<string, number>
  /** Ro'yxat qirqildi (eng yangilari qoldi). */
  truncated: boolean
}

/** Yangi o'quvchilar kesimi: oxirgi `days` kunda qabul qilinganlar + barcha sinovdagilar. */
export async function getStudentIntakeList(days?: number): Promise<StudentIntakeList> {
  const { data } = await api.get<StudentIntakeList>('/admin/student-intake', {
    params: days ? { days } : undefined,
  })
  return data
}

/** Bitta o'quvchining cheklisti (profil kartasi). */
export async function getStudentIntake(studentId: string): Promise<StudentIntakeDetail> {
  const { data } = await api.get<StudentIntakeDetail>(`/admin/student-intake/${studentId}`)
  return data
}

/**
 * Qo'lda belgi: `done=true` — "bajarildi" (izoh bilan), `false` — belgini olib tashlash.
 * Javob — yangilangan cheklist. Xato chaqiruvchiga OTILADI (u `apiErrorMessage` bilan ko'rsatadi).
 */
export async function setStudentIntakeMark(
  studentId: string,
  item: IntakeItemKey,
  done: boolean,
  note?: string,
): Promise<StudentIntakeDetail> {
  const { data } = await api.put<StudentIntakeDetail>(`/admin/student-intake/${studentId}/${item}`, {
    done,
    note: note ?? null,
  })
  return data
}
