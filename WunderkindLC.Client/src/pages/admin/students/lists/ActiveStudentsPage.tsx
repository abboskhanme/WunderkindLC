import { getStudents } from '@/api/services/students'
import { TelegramLinkCell } from '@/components/messaging/TelegramLinkCell'
import {
  StudentListBoard,
  balanceColumn,
  dateColumn,
  moderatorColumn,
  nameColumn,
  phoneColumn,
  type StudentColumn,
} from './StudentListBoard'

/** TELEGRAM: «Ulangan» = botga ulangan chat bor YOKI akkauntga kirilgan (egasining qarori, 2026-10-15). */
const telegramColumn: StudentColumn = {
  key: 'telegram',
  header: 'Telegram',
  render: (s) => (
    <TelegramLinkCell
      linked={s.telegramLinked}
      relations={s.telegramRelations}
      blocked={s.telegramBlocked}
      cabinet={!!s.appFirstLoginAt}
    />
  ),
}

/**
 * O'quvchilar → «Aktiv o'quvchilar» (edutizim `/students/active`): kamida bitta AKTIV a'zoligi
 * bor o'quvchilar. Ta'rif serverda — bosh sahifadagi "Aktiv o'quvchilar" kartochkasi bilan bir xil.
 */
export function ActiveStudentsPage() {
  return (
    <StudentListBoard
      load={() => getStudents('active')}
      telegramFilter
      columns={[
        nameColumn,
        phoneColumn,
        telegramColumn,
        balanceColumn,
        dateColumn('paid', "To'lov sanasi", (s) => s.lastPaymentDate),
        dateColumn('created', 'Yaratilgan sanasi', (s) => s.createdAt || s.enrollmentDate),
        moderatorColumn,
        dateColumn('app', 'Ilovani yuklab olish sanasi', (s) => s.appFirstLoginAt),
      ]}
    />
  )
}
