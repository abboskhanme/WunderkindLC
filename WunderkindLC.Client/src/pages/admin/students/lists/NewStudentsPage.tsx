import { getStudents } from '@/api/services/students'
import {
  StudentListBoard,
  balanceColumn,
  dateColumn,
  groupColumn,
  moderatorColumn,
  nameColumn,
  phoneColumn,
} from './StudentListBoard'

/**
 * O'quvchilar → «Yangi o'quvchilar» (edutizim `/orders/new-student-list`): SINOVDAGI o'quvchilar.
 * Holat qoidasi SERVERDA (`StudentListView.MatchesState`), ya'ni bosh sahifadagi
 * "Yangi o'quvchilar" kartochkasi bilan bitta ta'rif.
 */
export function NewStudentsPage() {
  return (
    <StudentListBoard
      load={() => getStudents('trial')}
      columns={[
        nameColumn,
        phoneColumn,
        balanceColumn,
        groupColumn,
        moderatorColumn,
        dateColumn('app', 'Ilovani yuklab olish sanasi', (s) => s.appFirstLoginAt),
        {
          key: 'contract',
          header: 'Shartnoma',
          render: (s) => (s.contractNumber ? `№ ${s.contractNumber}` : <span className="text-[#9ca3af]">—</span>),
        },
      ]}
    />
  )
}
