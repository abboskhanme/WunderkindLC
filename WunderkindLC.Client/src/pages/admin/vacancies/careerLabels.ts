import type { BadgeTone } from '@/components/ui/Badge'
import type { ApplicationStatus, EmploymentType, Vacancy } from '@/api/services/career'

/**
 * KARYERA bo'limining yagona yorliq/rang jadvallari — vakansiya kartalari, arizalar ro'yxati
 * va tafsilot modali shu yerdan oladi (matn bir joyda tursin).
 * Bosqich kalitlari backenddagi `CareerService.Stages` bilan bir xil.
 */

export const employmentLabels: Record<EmploymentType, string> = {
  full: "To'liq bandlik",
  part: 'Yarim stavka',
  shift: 'Smenali',
  remote: 'Masofaviy',
}

export const employmentOptions: { value: EmploymentType; label: string }[] = [
  { value: 'full', label: employmentLabels.full },
  { value: 'part', label: employmentLabels.part },
  { value: 'shift', label: employmentLabels.shift },
  { value: 'remote', label: employmentLabels.remote },
]

/** Bosqich → ro'yxatdagi rang (Badge tone). */
export const statusTones: Record<ApplicationStatus, BadgeTone> = {
  new: 'blue',
  review: 'violet',
  interview: 'amber',
  trial: 'teal',
  hired: 'green',
  rejected: 'red',
}

/** Server bosqichlar katalogini bermaguncha ishlatiladigan zaxira yorliqlar. */
export const statusLabels: Record<ApplicationStatus, string> = {
  new: 'Yangi ariza',
  review: "Ko'rib chiqilmoqda",
  interview: 'Suhbatga taklif',
  trial: 'Sinov bosqichi',
  hired: 'Ishga qabul qilindi',
  rejected: 'Rad etildi',
}

export const statusIcons: Record<ApplicationStatus, string> = {
  new: '📥',
  review: '🔍',
  interview: '🗣',
  trial: '🎯',
  hired: '✅',
  rejected: '❌',
}

/** Filtr chiplari va bosqich tanlagichdagi tartib. */
export const statusOrder: ApplicationStatus[] = [
  'new',
  'review',
  'interview',
  'trial',
  'hired',
  'rejected',
]

/** Vakansiya maoshini bitta satrga jamlaydi (ilovadagi ko'rinish bilan bir xil mantiq). */
export function salaryText(v: Pick<Vacancy, 'salaryFrom' | 'salaryTo' | 'salaryNote'>): string {
  const fmt = (n: number) => n.toLocaleString('ru-RU')
  if (v.salaryFrom > 0 && v.salaryTo > 0 && v.salaryTo > v.salaryFrom)
    return `${fmt(v.salaryFrom)} – ${fmt(v.salaryTo)} so'm`
  if (v.salaryFrom > 0) return `${fmt(v.salaryFrom)} so'mdan`
  if (v.salaryTo > 0) return `${fmt(v.salaryTo)} so'mgacha`
  return v.salaryNote || 'Kelishilgan holda'
}

/** "yyyy-MM-dd" muddat o'tib ketganmi. */
export function isExpired(deadline: string): boolean {
  if (!deadline || deadline.length !== 10) return false
  const today = new Date().toISOString().slice(0, 10)
  return deadline < today
}
