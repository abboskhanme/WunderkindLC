import type { IntakeItem, IntakeItemKey, StudentIntake } from '@/api/services/studentIntake'

/*
 * QABUL CHEKLISTI — klient tomonidagi SOF yordamchilar (filtr, holat, ustun sarlavhalari).
 * ⚠️ Band "bajarildimi" degan qaror bu yerda QAYTA hisoblanmaydi — u serverdan keladi
 * (`StudentIntake` — Application/Services). Bu fayl faqat KO'RSATISH va FILTR uchun.
 * Komponent fayllari bilan aralashtirilmaydi (`react-refresh/only-export-components`).
 */

/** Jadval ustunlari uchun QISQA sarlavhalar (to'liq yorliq serverdan keladi). Tartib — serverdagidek. */
export const INTAKE_COLUMNS: ReadonlyArray<{ key: IntakeItemKey; short: string }> = [
  { key: 'profile', short: "Ma'lumot" },
  { key: 'levelTest', short: 'Test' },
  { key: 'contract', short: 'Shartnoma' },
  { key: 'telegram', short: 'Telegram' },
  { key: 'payment', short: "To'lov" },
]

/** Qamrov oynasi variantlari (kun). Server 7..365 ga qirqadi. */
export const INTAKE_DAY_OPTIONS = [30, 60, 90, 180] as const
export const INTAKE_DEFAULT_DAYS = 60

export type IntakeStatusFilter = 'incomplete' | 'complete' | 'all'

export interface IntakeFilter {
  status: IntakeStatusFilter
  /** Shu band BAJARILMAGANLAR (bo'sh — filtr yo'q). */
  item: IntakeItemKey | ''
  q: string
}

/** Band holati: avtomatik bajarilgan · qo'lda belgilangan · kutilmoqda. */
export type IntakeItemState = 'auto' | 'manual' | 'pending'

export function itemState(item: Pick<IntakeItem, 'auto' | 'done'>): IntakeItemState {
  if (item.auto) return 'auto'
  return item.done ? 'manual' : 'pending'
}

/** Progress rangi: hammasi bajarilgan · yarmidan ko'pi · kam. */
export function progressTone(done: number, total: number): 'complete' | 'partial' | 'low' {
  if (total > 0 && done >= total) return 'complete'
  return done * 2 >= total ? 'partial' : 'low'
}

/**
 * Ro'yxat filtri (klientda — ro'yxat bir so'rovda keladi). Qidiruv: ism, guruh nomi yoki telefon
 * RAQAMLARI ("901234567" ham "+998 90 123 45 67" ni topadi; kamida 3 raqam).
 */
export function matchesIntake(row: StudentIntake, f: IntakeFilter): boolean {
  if (f.status === 'incomplete' && row.complete) return false
  if (f.status === 'complete' && !row.complete) return false
  if (f.item) {
    const it = row.items.find((i) => i.key === f.item)
    if (!it || it.done) return false
  }
  const needle = f.q.trim().toLowerCase()
  if (!needle) return true
  if (row.fullName.toLowerCase().includes(needle)) return true
  if (row.groups.some((g) => g.toLowerCase().includes(needle))) return true
  const digits = needle.replace(/\D/g, '')
  return digits.length >= 3 && row.phone.replace(/\D/g, '').includes(digits)
}

/** O'quvchi id → cheklist (ro'yxat ustuni uchun tez qidiruv). */
export function intakeIndex(rows: readonly StudentIntake[] | null | undefined): Map<string, StudentIntake> {
  return new Map((rows ?? []).map((r) => [r.studentId, r]))
}

/** Bajarilmagan bandlarning yorliqlari (tooltip / qisqa izoh uchun). */
export function pendingLabels(row: Pick<StudentIntake, 'items'>): string[] {
  return row.items.filter((i) => !i.done).map((i) => i.label)
}
