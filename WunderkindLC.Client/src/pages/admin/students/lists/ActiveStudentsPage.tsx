import { getStudents } from '@/api/services/students'
import {
  StudentListBoard,
  balanceColumn,
  dateColumn,
  moderatorColumn,
  nameColumn,
  phoneColumn,
} from './StudentListBoard'

/**
 * O'quvchilar → «Aktiv o'quvchilar» (edutizim `/students/active`): kamida bitta AKTIV a'zoligi
 * bor o'quvchilar. Ta'rif serverda — bosh sahifadagi "Aktiv o'quvchilar" kartochkasi bilan bir xil.
 */
export function ActiveStudentsPage() {
  return (
    <StudentListBoard
      load={() => getStudents('active')}
      columns={[
        nameColumn,
        phoneColumn,
        balanceColumn,
        dateColumn('paid', "To'lov sanasi", (s) => s.lastPaymentDate),
        dateColumn('created', 'Yaratilgan sanasi', (s) => s.createdAt || s.enrollmentDate),
        moderatorColumn,
        dateColumn('app', 'Ilovani yuklab olish sanasi', (s) => s.appFirstLoginAt),
      ]}
    />
  )
}
