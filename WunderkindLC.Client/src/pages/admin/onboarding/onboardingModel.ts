import type { BadgeTone } from '@/components/ui/Badge'
import type {
  OnboardingFilter, OnboardingPerson, OnboardingStatus, OnboardingTask,
} from '@/api/services/onboarding'

/**
 * MOSLASHUV sahifasining SOF yordamchilari (komponentsiz — `react-refresh/only-export-components`).
 * Kechikish/foiz/holat SERVERDA hisoblanadi (`OnboardingService`) — bu yerda faqat ko'rinish
 * matnlari: "3 kun kechikdi", "bugun", "2 kun qoldi".
 */

export const statusLabels: Record<OnboardingStatus, string> = {
  active: 'Jarayonda',
  completed: 'Yakunlangan',
  cancelled: "To'xtatilgan",
}

export const statusTones: Record<OnboardingStatus, BadgeTone> = {
  active: 'blue',
  completed: 'green',
  cancelled: 'default',
}

/** Ro'yxat filtrlari — tartib bilan (birinchisi standart). */
export const filters: { key: OnboardingFilter; label: string }[] = [
  { key: 'active', label: 'Jarayonda' },
  { key: 'overdue', label: 'Kechikkanlar' },
  { key: 'completed', label: 'Yakunlangan' },
  { key: 'cancelled', label: "To'xtatilgan" },
  { key: '', label: 'Hammasi' },
]

/** Brauzerning MAHALLIY bugungi sanasi "yyyy-MM-dd" (`toISOString` — UTC, ertalab kechagi kun bo'lardi). */
export function todayIso(now: Date = new Date()): string {
  const p = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${p(now.getMonth() + 1)}-${p(now.getDate())}`
}

/** Ikki "yyyy-MM-dd" sana orasidagi kunlar (b − a). Buzuq sana — null. */
export function daysBetween(a: string, b: string): number | null {
  const re = /^(\d{4})-(\d{2})-(\d{2})$/
  const ma = re.exec(a)
  const mb = re.exec(b)
  if (!ma || !mb) return null
  const ta = Date.UTC(+ma[1], +ma[2] - 1, +ma[3])
  const tb = Date.UTC(+mb[1], +mb[2] - 1, +mb[3])
  if (Number.isNaN(ta) || Number.isNaN(tb)) return null
  return Math.round((tb - ta) / 86_400_000)
}

/** Qadam muddati matni: bajarilgan / kechikkan / bugun / ertaga / N kun qoldi. */
export function dueText(task: Pick<OnboardingTask, 'done' | 'dueDate'>, today: string): string {
  if (task.done) return 'Bajarildi'
  const left = daysBetween(today, task.dueDate)
  if (left == null) return ''
  if (left < 0) return `${-left} kun kechikdi`
  if (left === 0) return 'Bugun'
  if (left === 1) return 'Ertaga'
  return `${left} kun qoldi`
}

/** Progress chizig'ining rangi: kechikkan bo'lsa qizil, tugagan — yashil, aks holda brend. */
export function progressBarClass(pct: number, overdue: number): string {
  if (overdue > 0) return 'bg-red-500'
  if (pct >= 100) return 'bg-emerald-500'
  return 'bg-brand-500'
}

/**
 * "Tizimdagi akkaunt" tanlagichining qiymati: `teacher:<id>` | `staff:<id>` | ''. Server ikki
 * alohida maydon kutadi (`teacherId` / `staffUserId`) — bittasigina to'ladi.
 */
export function encodeAccount(teacherId: string, staffUserId: string): string {
  if (teacherId) return `teacher:${teacherId}`
  if (staffUserId) return `staff:${staffUserId}`
  return ''
}

export function decodeAccount(value: string): { teacherId: string; staffUserId: string } {
  const i = value.indexOf(':')
  const kind = i > 0 ? value.slice(0, i) : ''
  const id = i > 0 ? value.slice(i + 1) : ''
  return {
    teacherId: kind === 'teacher' ? id : '',
    staffUserId: kind === 'staff' ? id : '',
  }
}

/** Odamlar ro'yxatini turi bo'yicha ajratadi (tanlagichdagi `optgroup` uchun). */
export function groupPeople(people: OnboardingPerson[]): { teachers: OnboardingPerson[]; staff: OnboardingPerson[] } {
  return {
    teachers: people.filter((p) => p.kind === 'teacher'),
    staff: people.filter((p) => p.kind === 'staff'),
  }
}
