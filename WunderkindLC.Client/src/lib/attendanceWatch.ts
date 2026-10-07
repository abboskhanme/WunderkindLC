import type { TodayLessonMonitor } from '@/types'

/**
 * «DAVOMAT OLINMAGAN GURUHLAR» — «O'quvchilar davomati» sahifasidagi bo'lim uchun SOF yordamchilar.
 *
 * ⚠️ Qoida bu yerda EMAS: qaysi kunda dars bor (ko'chirilgan darslar, guruh umri, o'rinbosar)
 * va dars "kechikkan"mi — SERVERDA (`AttendanceWatch`, `GET /admin/dashboard/today-lessons`).
 * Bu fayl faqat kelgan ro'yxatni filtrlaydi va tartiblaydi — «Nazorat → Davomat qilinmagan
 * guruhlar» sahifasi bilan BITTA manba.
 */

/** Davomati olinmagan darslar. `onlyOverdue` — faqat boshlangan/tugaganlari (hali boshlanmagan kirmaydi). */
export function notTakenLessons(lessons: TodayLessonMonitor[], onlyOverdue: boolean): TodayLessonMonitor[] {
  return lessons
    .filter((l) => !l.attendanceDone && (!onlyOverdue || !!l.overdue))
    .sort((a, b) => {
      // Kechikkanlar tepada (eng muhim — "hozir kimga qo'ng'iroq qilish kerak"), keyin vaqt bo'yicha.
      const oa = a.overdue ? 0 : 1
      const ob = b.overdue ? 0 : 1
      if (oa !== ob) return oa - ob
      return (a.startTime || '99:99').localeCompare(b.startTime || '99:99') || a.groupName.localeCompare(b.groupName)
    })
}

/** Kechikkan darslar soni (sahifa tepasidagi kartochka). */
export function overdueCount(lessons: TodayLessonMonitor[]): number {
  return lessons.filter((l) => !l.attendanceDone && l.overdue).length
}

/** Dars bosqichining yorlig'i. */
export function phaseLabel(l: TodayLessonMonitor): string {
  if (l.phase === 'upcoming') return 'Hali boshlanmagan'
  if (l.phase === 'ended') return 'Dars tugagan'
  return 'Dars ketyapti'
}
