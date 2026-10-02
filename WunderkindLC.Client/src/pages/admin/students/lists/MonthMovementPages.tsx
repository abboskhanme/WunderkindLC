import type { ReactNode } from 'react'
import { useSearchParams } from 'react-router-dom'
import { getMonthMovement, type MonthMovementKind } from '@/api/services/students'
import { FilterInput } from '@/components/ui/list/FilterGrid'
import { cn } from '@/lib/utils'
import { currentMonth } from '@/lib/month'
import { groupsText } from '@/lib/studentGroups'
import {
  StudentListBoard,
  balanceColumn,
  dateColumn,
  moderatorColumn,
  nameColumn,
  phoneColumn,
  type StudentColumn,
} from './StudentListBoard'

/**
 * Bosh sahifa kartochkalarining RO'YXATLARI (`lib/homeCards.ts`):
 * - «Ketgan o'quvchilar» — "Yangi o'quvchidan ketganlar" / "Aktiv o'quvchidan ketganlar";
 * - «Birinchi to'lovni qilganlar».
 *
 * Tanlov SERVERDA (`GET /admin/students/month-movement`) — kartochka sonini hisoblaydigan AYNAN
 * o'sha funksiyadan, ya'ni qatorlar soni kartochkadagi son bilan teng. Bu yerda qayta hisob YO'Q.
 *
 * Oy va tur MANZILDA (`?month=yyyy-MM&type=new|active`) — kartochkadan aynan o'sha oy bilan
 * kelinadi, havola ulashilsa ham o'sha ro'yxat ochiladi. Yozish `replace` bilan (`useUrlFilters`
 * naqshi): har tanlov tarixga yozilsa "orqaga" sahifadan chiqmay qolardi.
 */

const MONTH_RE = /^\d{4}-(0[1-9]|1[0-2])$/

/** Manzildagi oy — noto'g'ri/yo'q bo'lsa bo'sh (server joriy oyni oladi). */
function monthParam(sp: URLSearchParams): string {
  const m = sp.get('month') ?? ''
  return MONTH_RE.test(m) ? m : ''
}

function usePatchParams() {
  const [sp, setSp] = useSearchParams()
  const set = (patch: Record<string, string>) =>
    setSp(
      (prev) => {
        const next = new URLSearchParams(prev)
        for (const [k, v] of Object.entries(patch)) {
          if (v) next.set(k, v)
          else next.delete(k)
        }
        return next
      },
      { replace: true },
    )
  return { sp, set }
}

function MovementHeader({
  title,
  hint,
  month,
  onMonth,
  children,
}: {
  title: string
  hint: string
  month: string
  onMonth: (month: string) => void
  children?: ReactNode
}) {
  return (
    <div className="mb-3 space-y-2">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-[18px] font-semibold text-black">{title}</h1>
        <div className="flex flex-wrap items-center gap-2">
          {children}
          <div className="w-[170px]">
            <FilterInput
              type="month"
              aria-label="Oy"
              value={month || currentMonth()}
              onChange={(e) => onMonth(MONTH_RE.test(e.target.value) ? e.target.value : '')}
            />
          </div>
        </div>
      </div>
      <p className="text-[12px] text-[#6b7280]">{hint}</p>
    </div>
  )
}

/** Hozirgi guruhi: arxivdagi o'quvchida eski `className` yorlig'i "hozirgi" bo'lib ko'rinmasin. */
const currentGroupColumn: StudentColumn = {
  key: 'current',
  header: 'Hozirgi holati',
  render: (s) =>
    s.isArchived ? (
      <span className="rounded-full bg-red-50 px-2 py-0.5 text-[11px] font-semibold text-red-600">Arxivda</span>
    ) : (
      <span className="text-[#333]">{groupsText(s.groupStates, '', '—')}</span>
    ),
}

export function LeftStudentsPage() {
  const { sp, set } = usePatchParams()
  const month = monthParam(sp)
  const type = sp.get('type') === 'new' ? 'new' : 'active'
  const kind: MonthMovementKind = type === 'new' ? 'leftNew' : 'leftActive'

  return (
    <div>
      <MovementHeader
        title="Ketgan o'quvchilar"
        hint={
          "Tanlangan oyda kursdan butunlay ketganlar — bosh sahifadagi kartochka bilan bir xil qoida. " +
          "Guruh almashtirish va sertifikat bilan tugatish bu yerga kirmaydi."
        }
        month={month}
        onMonth={(m) => set({ month: m })}
      >
        <div className="flex gap-1.5">
          {(
            [
              ['new', "Yangi o'quvchidan"],
              ['active', "Aktiv o'quvchidan"],
            ] as const
          ).map(([key, label]) => (
            <button
              key={key}
              type="button"
              onClick={() => set({ type: key })}
              className={cn(
                'rounded-lg px-3.5 py-2 text-[13px] font-semibold transition-colors',
                type === key
                  ? 'bg-brand-600 text-white'
                  : 'border border-slate-200 bg-white text-slate-700 hover:bg-slate-50',
              )}
            >
              {label}
            </button>
          ))}
        </div>
      </MovementHeader>

      {/* `key` — oy/tur almashganda ro'yxat qaytadan yuklanadi (StudentListBoard bir marta yuklaydi). */}
      <StudentListBoard
        key={`${kind}:${month}`}
        load={() => getMonthMovement(kind, month || undefined)}
        columns={[
          nameColumn,
          phoneColumn,
          balanceColumn,
          {
            key: 'leftGroup',
            header: 'Ketgan guruhi',
            render: (s) => s.movementGroup || <span className="text-[#9ca3af]">—</span>,
          },
          dateColumn('leftAt', 'Ketgan sana', (s) => s.movementDate),
          currentGroupColumn,
          moderatorColumn,
        ]}
      />
    </div>
  )
}

export function FirstPaymentStudentsPage() {
  const { sp, set } = usePatchParams()
  const month = monthParam(sp)

  return (
    <div>
      <MovementHeader
        title="Birinchi to'lovni qilganlar"
        hint="Markazga BIRINCHI o'quv to'lovini tanlangan oyda qilgan o'quvchilar (qaytarilgan to'lov hisobga olinmaydi)."
        month={month}
        onMonth={(m) => set({ month: m })}
      />
      <StudentListBoard
        key={month}
        load={() => getMonthMovement('firstPayment', month || undefined)}
        columns={[
          nameColumn,
          phoneColumn,
          balanceColumn,
          dateColumn('firstPaid', "Birinchi to'lov sanasi", (s) => s.movementDate),
          currentGroupColumn,
          moderatorColumn,
        ]}
      />
    </div>
  )
}
