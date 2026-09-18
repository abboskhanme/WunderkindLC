import { getArchivedStudents } from '@/api/services/students'
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
 * O'quvchilar → «Arxiv o'quvchilar» (edutizim `/students/archive`).
 * ⚠️ Barcha turdagi arxiv (guruh, o'qituvchi, lid ...) bizdagi «Arxiv (barcha)» sahifasida
 * qoladi — u "Future" menyusida, chunki edutizimda unday sahifa yo'q.
 */
export function ArchivedStudentsPage() {
  return (
    <StudentListBoard
      load={getArchivedStudents}
      columns={[
        nameColumn,
        phoneColumn,
        balanceColumn,
        { ...groupColumn, header: 'Arxivlangan guruh' },
        dateColumn('archived', 'Arxivlangan sana', (s) => s.archivedAt ?? ''),
        {
          key: 'reason',
          header: 'Sabab',
          render: (s) => s.archiveReason || <span className="text-[#9ca3af]">—</span>,
        },
        moderatorColumn,
      ]}
    />
  )
}
