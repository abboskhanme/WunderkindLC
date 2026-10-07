import { useMemo } from 'react'
import { Link } from 'react-router-dom'
import { getStudents } from '@/api/services/students'
import { getStudentIntakeList } from '@/api/services/studentIntake'
import { useAsync } from '@/hooks/useAsync'
import { intakeIndex, pendingLabels, progressTone } from '@/lib/intake'
import { cn } from '@/lib/utils'
import {
  StudentListBoard,
  balanceColumn,
  dateColumn,
  groupColumn,
  moderatorColumn,
  nameColumn,
  phoneColumn,
} from './StudentListBoard'

const intakeChipCls = {
  complete: 'bg-emerald-50 text-emerald-700',
  partial: 'bg-amber-50 text-amber-800',
  low: 'bg-rose-50 text-rose-700',
} as const

/**
 * O'quvchilar → «Yangi o'quvchilar» (edutizim `/orders/new-student-list`): SINOVDAGI o'quvchilar.
 * Holat qoidasi SERVERDA (`StudentListView.MatchesState`), ya'ni bosh sahifadagi
 * "Yangi o'quvchilar" kartochkasi bilan bitta ta'rif.
 *
 * «Qabul» ustuni — qabul cheklisti progressi (`/admin/student-intake`, alohida so'rov). Barcha
 * sinovdagilar cheklist qamrovida bor (`StudentIntake.InCohort`), ya'ni ustun bo'sh qolmaydi.
 * Filtr ("to'liq emas") va qo'lda belgilash — «Qabul cheklisti» sahifasida.
 */
export function NewStudentsPage() {
  // Ikkilamchi ma'lumot: xato bo'lsa ro'yxat baribir ishlaydi, sabab esa tepada ko'rinadi.
  const intake = useAsync(() => getStudentIntakeList(), [])
  const index = useMemo(() => intakeIndex(intake.data?.items), [intake.data])

  return (
    <div>
      <div className="mb-2 flex flex-wrap items-center justify-end gap-3">
        {intake.error && <span className="text-[12px] text-red-600">Qabul cheklisti yuklanmadi: {intake.error}</span>}
        <Link to="/admin/students/qabul" className="text-[13px] font-semibold text-brand-600 hover:underline">
          Qabul cheklisti
          {intake.data ? ` · to'liq emas: ${intake.data.incomplete}` : ''} →
        </Link>
      </div>
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
          {
            key: 'intake',
            header: 'Qabul',
            render: (s) => {
              const r = index.get(s.id)
              if (!r) return <span className="text-[#9ca3af]">—</span>
              return (
                <Link
                  to={`/admin/students/${s.id}`}
                  title={r.complete ? "Qabul cheklisti to'liq" : `Qolgan: ${pendingLabels(r).join(', ')}`}
                  className={cn(
                    'inline-flex rounded-full px-2 py-0.5 text-[12px] font-semibold',
                    intakeChipCls[progressTone(r.done, r.total)],
                  )}
                >
                  {r.done}/{r.total}
                </Link>
              )
            },
          },
        ]}
      />
    </div>
  )
}
