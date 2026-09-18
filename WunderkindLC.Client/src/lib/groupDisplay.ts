/**
 * Guruh bo'limining (edutizim ko'rinishi) SOF ko'rsatish funksiyalari: kun/vaqt matnlari,
 * a'zolik holati belgisi, "bugun davomat qilinmagan" to'plami va guruh tarixi qatori.
 * Hisob-kitob YO'Q — faqat mavjud ma'lumotni o'qiladigan shaklga keltirish.
 * Testlar: `lib/__tests__/groupDisplay.test.ts`.
 */

/** Hafta kunlari (0=Dushanba .. 6=Yakshanba) — backend kontrakti. */
export const WEEKDAYS = ['Dushanba', 'Seshanba', 'Chorshanba', 'Payshanba', 'Juma', 'Shanba', 'Yakshanba']
export const WEEKDAYS_SHORT = ['Du', 'Se', 'Cho', 'Pay', 'Ju', 'Sha', 'Yak']

// Toq kunlar = Du/Cho/Ju (0,2,4); juft kunlar = Se/Pay/Sha (1,3,5) — Guruhlar ro'yxatidagi
// eski filtr bilan AYNAN bir xil ta'rif.
const ODD_DAYS = [0, 2, 4]
const EVEN_DAYS = [1, 3, 5]

/** Guruh kunlari to'liq toq/juft naqshga mos kelsa 'odd'/'even', aks holda 'other'. */
export function dayKind(days?: number[]): 'odd' | 'even' | 'other' {
  if (!days || days.length === 0) return 'other'
  if (days.every((d) => ODD_DAYS.includes(d))) return 'odd'
  if (days.every((d) => EVEN_DAYS.includes(d))) return 'even'
  return 'other'
}

/**
 * edutizim "KUN" ustuni: to'liq toq naqsh (Du/Cho/Ju) — "Toq kunlar", juft (Se/Pay/Sha) —
 * "Juft kunlar", qolgani — qisqa kunlar ro'yxati ("Du, Se"). Bo'sh — "—".
 */
export function formatGroupDays(days?: number[]): string {
  if (!days || days.length === 0) return '—'
  const sorted = [...days].sort((a, b) => a - b)
  if (sorted.length === 3 && sorted.join() === ODD_DAYS.join()) return 'Toq kunlar'
  if (sorted.length === 3 && sorted.join() === EVEN_DAYS.join()) return 'Juft kunlar'
  return sorted.map((d) => WEEKDAYS_SHORT[d] ?? '?').join(', ')
}

/** "07:00 - 09:00" (edutizim "DARS VAQTI"). Bittasi bo'lsa — o'zi, ikkalasi bo'sh — "—". */
export function formatLessonTime(start?: string, end?: string): string {
  if (start && end) return `${start} - ${end}`
  return start || end || '—'
}

const ddmmyyyy = (iso?: string | null) => {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '')
  return m ? `${m[3]}.${m[2]}.${m[1]}` : ''
}

/** edutizim "GURUH VAQTI" chipi: "dd.mm.yyyy - dd.mm.yyyy". Sana yo'q bo'lsa — "". */
export function formatGroupPeriod(start?: string | null, end?: string | null): string {
  const a = ddmmyyyy(start)
  const b = ddmmyyyy(end)
  if (!a && !b) return ''
  return `${a || '…'} - ${b || '…'}`
}

const toMinutes = (t?: string) => {
  const m = /^(\d{1,2}):(\d{2})/.exec(t ?? '')
  return m ? Number(m[1]) * 60 + Number(m[2]) : null
}

/** "Dars davomiyligi": "2 soat", "1 soat 30 daqiqa", "45 daqiqa". Buzuq vaqt — "". */
export function lessonDurationLabel(start?: string, end?: string): string {
  const a = toMinutes(start)
  const b = toMinutes(end)
  if (a == null || b == null || b <= a) return ''
  const mins = b - a
  const h = Math.floor(mins / 60)
  const m = mins % 60
  if (h === 0) return `${m} daqiqa`
  return m === 0 ? `${h} soat` : `${h} soat ${m} daqiqa`
}

/** "HH:mm" vaqt guruh darsi ichidami (`[start, end)`) — "Dars vaqti" filtri. */
export function lessonCovers(start: string | undefined, end: string | undefined, time: string): boolean {
  const t = toMinutes(time)
  const a = toMinutes(start)
  if (t == null || a == null) return false
  const b = toMinutes(end)
  if (b == null || b <= a) return t === a
  return t >= a && t < b
}

/**
 * A'zolik holati belgisi (guruh sahifasi, "Guruh o'quvchilari"). `yearFreeze` — «Aktiv
 * muzlatish»: holat baribir "frozen", bu faqat aniqroq (indigo) yozuv (`year-freeze.md` §5).
 * `isActive === false` — guruhdan chiqqan (yakunlangan) a'zolik.
 */
export function membershipBadge(
  status: string,
  yearFreeze?: boolean,
  isActive: boolean = true,
): { label: string; cls: string } {
  if (!isActive)
    return status === 'completed'
      ? { label: 'Tugatgan', cls: 'bg-slate-100 text-slate-600' }
      : { label: 'Chiqqan', cls: 'bg-slate-100 text-slate-500' }
  switch (status) {
    case 'active':
      return { label: 'Aktiv', cls: 'bg-emerald-50 text-emerald-700' }
    case 'frozen':
      return yearFreeze
        ? { label: 'Aktiv muzlatilgan', cls: 'bg-indigo-50 text-indigo-700' }
        : { label: 'Muzlatilgan', cls: 'bg-sky-50 text-sky-700' }
    default:
      return { label: 'Sinov', cls: 'bg-amber-50 text-amber-700' }
  }
}

/**
 * Bugun darsi BOR, lekin davomati HALI OLINMAGAN guruhlar (edutizimdagi sariq `#FFFF04` qator).
 * Manba — mavjud "Darslar monitoringi" (`GET /admin/dashboard/today-lessons`): qoida o'sha yerda,
 * bu yerda faqat to'plamga aylantiriladi.
 */
export function notAttendedGroupIds(lessons: { groupId: string; attendanceDone: boolean }[]): Set<string> {
  return new Set(lessons.filter((l) => !l.attendanceDone).map((l) => l.groupId))
}

/** Guruh tarixi jadvalining qatori (edutizim "Guruh tarixi ma'lumotlari"). */
export interface GroupHistoryRow {
  id: string
  student: string
  moderator: string
  oldTeacher: string
  teacher: string
  at: string
  type: string
}

const TEACHER_CHANGE = /o'qituvchisi almashtirildi \(.*\):\s*(.+?)\s*→\s*(.+)$/

/**
 * Audit yozuvidan jadval qatori: O'QUVCHI — `studentId` bo'yicha ism (a'zolar ro'yxatidan),
 * MODERATOR — amalni qilgan xodim, ESKI O'QITUVCHI / O'QITUVCHI — faqat "Guruh o'qituvchisi
 * almashtirildi" yozuvidan (server shu matnni NOM bilan yozadi), TURI — yozuvning o'zbekcha izohi.
 */
export function toGroupHistoryRow(
  log: {
    id: string
    timestamp: string
    actorName?: string
    summary: string
    studentId?: string
    entityType?: string
    entityId?: string
  },
  studentNames: Map<string, string>,
): GroupHistoryRow {
  const m = TEACHER_CHANGE.exec(log.summary)
  // A'zolik hodisasi `EntityId = "{groupId}:{studentId}"` (audit.md §2) — studentId yozilmagan
  // eski qatorlarda o'quvchi shu yerdan olinadi.
  const sid =
    log.studentId ||
    (log.entityType === 'Membership' && log.entityId?.includes(':') ? log.entityId.split(':')[1] : '')
  return {
    id: log.id,
    student: sid ? (studentNames.get(sid) ?? '') : '',
    moderator: log.actorName ?? '',
    oldTeacher: m ? m[1] : '',
    teacher: m ? m[2] : '',
    at: log.timestamp,
    type: log.summary,
  }
}
