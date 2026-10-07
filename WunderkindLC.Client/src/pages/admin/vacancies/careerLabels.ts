import type { BadgeTone } from '@/components/ui/Badge'
import type { ApplicationStatus, CareerStage, EmploymentType, Vacancy } from '@/api/services/career'

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
  review: 'default',
  test: 'indigo',
  trial: 'teal',
  interview: 'amber',
  director: 'violet',
  hired: 'green',
  rejected: 'red',
}

/** Server bosqichlar katalogini bermaguncha ishlatiladigan zaxira yorliqlar. */
export const statusLabels: Record<ApplicationStatus, string> = {
  new: 'CV qabul qilindi',
  review: "Ko'rib chiqilmoqda (eski)",
  test: 'Test',
  trial: 'Demo dars',
  interview: 'HR suhbati',
  director: 'Rahbar (direktor) suhbati',
  hired: 'Ishga qabul qilindi',
  rejected: 'Rad etildi',
}

export const statusIcons: Record<ApplicationStatus, string> = {
  new: '📥',
  review: '🔍',
  test: '📝',
  trial: '🎯',
  interview: '🗣',
  director: '👔',
  hired: '✅',
  rejected: '❌',
}

/**
 * Filtr chiplari va bosqich tanlagichdagi tartib — yig'ilishdagi VORONKA:
 * CV → Test → Demo dars → HR suhbati → Rahbar suhbati → Ishga qabul, oxirida "Rad etildi".
 * Eski `review` bu yerda YO'Q ({@link legacyStatuses}) — u faqat mavjud arizada ko'rsatiladi.
 */
export const statusOrder: ApplicationStatus[] = [
  'new',
  'test',
  'trial',
  'interview',
  'director',
  'hired',
  'rejected',
]

/** ESKI bosqichlar: yangi o'tish uchun taklif qilinmaydi (server ham rad etadi). */
export const legacyStatuses: ApplicationStatus[] = ['review']

/**
 * Bosqich tanlagichidagi variantlar: server katalogi (bo'lsa) yoki zaxira ro'yxat; ESKI bosqich
 * faqat ariza HOZIR o'sha bosqichda bo'lsa qoladi (izohini yangilash uchun).
 */
export function stageOptions(stages: CareerStage[], current: ApplicationStatus): CareerStage[] {
  const list: CareerStage[] = stages.length > 0
    ? stages
    : [...statusOrder, ...legacyStatuses].map((k, i) => ({
        key: k,
        label: statusLabels[k],
        candidateText: '',
        icon: statusIcons[k],
        order: i + 1,
        isFinal: k === 'hired' || k === 'rejected',
        isLegacy: legacyStatuses.includes(k),
      }))
  return list.filter((s) => !(s.isLegacy ?? legacyStatuses.includes(s.key)) || s.key === current)
}

/** Bosqich nomi: server katalogidan, bo'lmasa zaxira yorliq (noma'lum kalit — o'zi). */
export function stageLabel(stages: CareerStage[], key: ApplicationStatus): string {
  return stages.find((s) => s.key === key)?.label ?? statusLabels[key] ?? key
}

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
