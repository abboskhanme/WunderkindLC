import { useMemo, useState, type ReactNode } from 'react'
import { Link } from 'react-router-dom'
import {
  IconCalendarShare,
  IconChevronDown,
  IconCircle,
  IconCircleCheck,
  IconCircleMinus,
  IconDownload,
  IconSortAscending,
  IconSortDescending,
} from '@tabler/icons-react'
import type { AbsenceReason, JournalEntry } from '@/types'
import type { GroupJournal, GroupJournalStudent } from '@/api/services/journal'
import { balanceTextCls, cn, exportToCsv, formatDate, formatMoney } from '@/lib/utils'
import type { BackState } from '@/lib/nav'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { autoPresent, uzMonths } from './shared'

type CellKind = 'present' | 'late' | 'absent' | 'empty' | 'blocked'

export interface AttendancePct {
  held: number
  absent: number
  pct: number | null
}

/**
 * Guruh sahifasi → "Davomat" tabi (edutizim ko'rinishi): sarlavhada guruh nomi + "Export",
 * saralash ("Ism bo'yicha | Qo'shilgan sana bo'yicha" + yo'nalish), yil va oy tanlovi; ostida
 * "Aktiv o'quvchi" va "Muzlatilgan o'quvchilar" bo'limlari, har dars — alohida ustun
 * ("1-dars / 02.09"): yashil ✓ — keldi, qizil ⊖ — kelmadi, kulrang ○ — belgilanmagan.
 *
 * ⚠️ Ma'lumot va QOIDALAR — "Jurnal" tabi bilan AYNAN bir xil manbadan (`GET /admin/journal/group`):
 * avto-"keldi" — `autoPresent` (yagona joy), a'zolikdan/guruh boshlanishidan oldingi va
 * muzlatilgandan keyingi darslar bo'sh. Katak bosilsa — o'sha jurnal oynasi (davomat/baho),
 * sarlavha bosilsa — shu kun uchun hammaga davomat. Muzlatilganlar — faqat ko'rish.
 * Oxirgi ustun — oy bo'yicha davomat foizi (ilgarigi "Davomat" jadvali).
 */
export function GroupAttendanceTab({
  journal,
  loading,
  onLoadMonth,
  entryMap,
  reasonById,
  conductedSet,
  phones,
  attendance,
  rescheduledDates,
  today,
  back,
  onCellClick,
  onHeaderClick,
}: {
  journal: GroupJournal
  loading: boolean
  onLoadMonth: (month: string) => void
  entryMap: Map<string, JournalEntry>
  reasonById: Map<string, AbsenceReason>
  conductedSet: Set<string>
  /** studentId → telefon (guruh ro'yxatidan). */
  phones: Map<string, string>
  attendance: Map<string, AttendancePct>
  /** Shu oyga KO'CHIRILGAN darslarning yangi sanalari. */
  rescheduledDates: Set<string>
  today: string
  back: BackState
  onCellClick: (s: GroupJournalStudent, date: string) => void
  onHeaderClick: (date: string) => void
}) {
  const [sortBy, setSortBy] = useState<'name' | 'joined'>('joined')
  const [asc, setAsc] = useState(true)
  const g = journal.group
  const columns = journal.columns

  const sortRows = (list: GroupJournalStudent[]) => {
    const out = [...list].sort((a, b) =>
      sortBy === 'name'
        ? a.fullName.localeCompare(b.fullName, 'uz')
        : (a.memberStart || '').localeCompare(b.memberStart || '') || a.fullName.localeCompare(b.fullName, 'uz'),
    )
    return asc ? out : out.reverse()
  }
  const active = useMemo(
    () => sortRows(journal.students.filter((s) => s.status !== 'frozen')),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [journal, sortBy, asc],
  )
  const frozen = useMemo(
    () => sortRows(journal.students.filter((s) => s.status === 'frozen')),
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [journal, sortBy, asc],
  )

  // Yil / oy tanlovi — jurnal ro'yxati bergan oylar ("YYYY-MM") bo'yicha.
  const years = useMemo(() => [...new Set(journal.months.map((m) => m.slice(0, 4)))].sort(), [journal.months])
  const curYear = journal.month.slice(0, 4)
  const monthsOfYear = journal.months.filter((m) => m.startsWith(curYear))

  const kindOf = (s: GroupJournalStudent, date: string, isFrozenRow: boolean): CellKind => {
    const e = entryMap.get(`${s.studentId}|${date}`)
    const reason = e?.reasonId ? reasonById.get(e.reasonId) : undefined
    const blocked =
      (!!g.startDate && date < g.startDate) ||
      (!!s.memberStart && date < s.memberStart) ||
      (isFrozenRow && !!s.frozenAt && date > s.frozenAt)
    if (e?.grade != null || e?.mastery != null) return 'present'
    if (reason) return reason.isLate ? 'late' : 'absent'
    if (
      autoPresent({
        hasGrade: false,
        hasReason: false,
        conducted: conductedSet.has(date),
        explicitPresent: !!e?.present,
        date,
        presentDefaultFrom: s.presentDefaultFrom,
        blocked,
      })
    )
      return 'present'
    return blocked ? 'blocked' : 'empty'
  }

  const exportCsv = () => {
    const head = ['№', 'Ism', 'Telefon raqam', 'Balans', ...columns.map((c, i) => `${i + 1}-dars (${formatDate(c.date)})`), 'Davomat %']
    const sym: Record<CellKind, string> = { present: '+', late: 'kech', absent: '-', empty: '', blocked: '' }
    const line = (s: GroupJournalStudent, i: number, isFrozenRow: boolean) => [
      String(i + 1),
      isFrozenRow ? `${s.fullName} (muzlatilgan)` : s.fullName,
      phones.get(s.studentId) ?? '',
      String(s.balance),
      ...columns.map((c) => sym[kindOf(s, c.date, isFrozenRow)]),
      attendance.get(s.studentId)?.pct != null ? `${attendance.get(s.studentId)!.pct}%` : '',
    ]
    exportToCsv(`${g.name}-davomat-${journal.month}.csv`, head, [
      ...active.map((s, i) => line(s, i, false)),
      ...frozen.map((s, i) => line(s, i, true)),
    ])
  }

  const selectCls =
    'h-9 appearance-none rounded-lg border border-black/25 bg-white pl-3 pr-8 text-[14px] text-black outline-none hover:border-black/60 focus:border-brand-600'

  return (
    <div className="rounded-xl border border-[#dbe0e6] bg-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
      {/* Sarlavha + boshqaruv */}
      <div className="flex flex-wrap items-end justify-between gap-3 border-b border-[#e0e0e0] px-4 py-3">
        <div className="flex flex-wrap items-end gap-3">
          <div>
            <p className="text-[12px] text-[#6b7280]">Guruh nomi</p>
            <p className="text-[16px] font-bold text-black">{g.name}</p>
          </div>
          <button
            type="button"
            onClick={exportCsv}
            className="inline-flex min-h-[31px] items-center gap-1.5 rounded-lg bg-brand-600 px-3 text-[12px] font-medium text-white transition-colors hover:bg-brand-700"
          >
            <IconDownload className="h-5 w-5" /> Export
          </button>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <div className="flex rounded-lg border border-[#dbe0e6] bg-white p-1">
            {(['name', 'joined'] as const).map((k) => (
              <button
                key={k}
                type="button"
                onClick={() => setSortBy(k)}
                className={cn(
                  'h-8 rounded-lg px-3 text-[13px] font-medium transition-colors',
                  sortBy === k ? 'bg-brand-600 text-white' : 'text-[#333] hover:bg-[#f0f2f2]',
                )}
              >
                {k === 'name' ? "Ism bo'yicha" : "Qo'shilgan sana bo'yicha"}
              </button>
            ))}
          </div>
          <TintedIconButton label={asc ? "O'sish tartibida" : 'Kamayish tartibida'} onClick={() => setAsc((v) => !v)}>
            {asc ? <IconSortAscending className="h-5 w-5" /> : <IconSortDescending className="h-5 w-5" />}
          </TintedIconButton>
          <div className="relative">
            <select
              aria-label="Yil"
              value={curYear}
              onChange={(e) => {
                const ms = journal.months.filter((m) => m.startsWith(e.target.value))
                if (ms.length) onLoadMonth(ms[ms.length - 1])
              }}
              className={cn(selectCls, 'w-28')}
            >
              {years.map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
            <IconChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
          </div>
          <div className="relative">
            <select
              aria-label="Oy"
              value={journal.month}
              onChange={(e) => onLoadMonth(e.target.value)}
              className={cn(selectCls, 'w-36')}
            >
              {monthsOfYear.map((m) => (
                <option key={m} value={m}>
                  {uzMonths[Number(m.slice(5, 7)) - 1] ?? m}
                </option>
              ))}
            </select>
            <IconChevronDown className="pointer-events-none absolute right-2.5 top-1/2 h-4 w-4 -translate-y-1/2 text-[#6b7280]" />
          </div>
        </div>
      </div>

      {!g.courseId ? (
        <p className="px-4 py-12 text-center text-sm text-[#6b7280]">
          Guruhga kurs biriktirilmagan — jurnal yuritib bo'lmaydi.
        </p>
      ) : active.length === 0 && frozen.length === 0 ? (
        <p className="px-4 py-12 text-center text-sm text-[#6b7280]">Bu guruhda o'quvchi yo'q.</p>
      ) : columns.length === 0 ? (
        <p className="px-4 py-12 text-center text-sm text-[#6b7280]">Bu oyda guruh kunlariga dars to'g'ri kelmadi.</p>
      ) : (
        <div className={cn('space-y-4 p-3 transition-opacity', loading && 'opacity-60')}>
          <Legend />
          <Section title="Aktiv o'quvchi" count={active.length}>
            <Grid
              rows={active}
              frozenRows={false}
              columns={columns}
              startDate={g.startDate}
              today={today}
              kindOf={kindOf}
              phones={phones}
              attendance={attendance}
              rescheduledDates={rescheduledDates}
              back={back}
              onCellClick={onCellClick}
              onHeaderClick={onHeaderClick}
            />
          </Section>
          {frozen.length > 0 && (
            <Section title="Muzlatilgan o'quvchilar" count={frozen.length} hint="faqat ko'rish — baho/davomat saqlanadi">
              <Grid
                rows={frozen}
                frozenRows
                columns={columns}
                startDate={g.startDate}
                today={today}
                kindOf={kindOf}
                phones={phones}
                attendance={attendance}
                rescheduledDates={rescheduledDates}
                back={back}
              />
            </Section>
          )}
        </div>
      )}
    </div>
  )
}

function Legend() {
  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 px-1 text-[12px] text-[#6b7280]">
      <span className="inline-flex items-center gap-1">
        <IconCircleCheck className="h-4 w-4 text-[#2e7d32]" /> Keldi
      </span>
      <span className="inline-flex items-center gap-1">
        <IconCircleCheck className="h-4 w-4 text-amber-500" /> Kech keldi
      </span>
      <span className="inline-flex items-center gap-1">
        <IconCircleMinus className="h-4 w-4 text-[#d32f2f]" /> Kelmadi
      </span>
      <span className="inline-flex items-center gap-1">
        <IconCircle className="h-4 w-4 text-[#bdbdbd]" /> Belgilanmagan
      </span>
      <span className="inline-flex items-center gap-1">
        <IconCalendarShare className="h-4 w-4 text-sky-500" /> Ko'chirilgan dars
      </span>
      <span>· Katak — davomat/baho, sarlavhadagi sana — shu kun uchun hammaga.</span>
    </div>
  )
}

function Section({
  title,
  count,
  hint,
  children,
}: {
  title: string
  count: number
  hint?: string
  children: ReactNode
}) {
  return (
    <section>
      <div className="mb-2 flex items-center gap-2 border-l-4 border-brand-600 pl-2">
        <h3 className="text-[16px] font-semibold text-brand-600">{title}</h3>
        <span className="rounded-full bg-brand-600 px-2 py-0.5 text-[12px] font-semibold text-white">{count}</span>
        {hint && <span className="text-[12px] text-[#6b7280]">{hint}</span>}
      </div>
      {children}
    </section>
  )
}

function Grid({
  rows,
  frozenRows,
  columns,
  startDate,
  today,
  kindOf,
  phones,
  attendance,
  rescheduledDates,
  back,
  onCellClick,
  onHeaderClick,
}: {
  rows: GroupJournalStudent[]
  frozenRows: boolean
  columns: GroupJournal['columns']
  startDate: string
  today: string
  kindOf: (s: GroupJournalStudent, date: string, isFrozenRow: boolean) => CellKind
  phones: Map<string, string>
  attendance: Map<string, AttendancePct>
  rescheduledDates: Set<string>
  back: BackState
  onCellClick?: (s: GroupJournalStudent, date: string) => void
  onHeaderClick?: (date: string) => void
}) {
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full border-collapse text-[15px] text-black">
        <thead>
          <tr className="text-left align-bottom">
            <th className="sticky left-0 z-10 w-10 bg-white px-2 pb-2 font-medium">№</th>
            <th className="sticky left-10 z-10 min-w-[220px] bg-white px-2 pb-2 font-medium">Ism</th>
            <th className="whitespace-nowrap px-2 pb-2 font-medium">Telefon raqam</th>
            <th className="px-2 pb-2 font-medium">Balans</th>
            {columns.map((c, i) => {
              const before = !!startDate && c.date < startDate
              const moved = rescheduledDates.has(c.date)
              return (
                <th key={c.date} className="px-1 pb-1 text-center font-medium">
                  <button
                    type="button"
                    disabled={!onHeaderClick || before}
                    onClick={() => onHeaderClick?.(c.date)}
                    title={
                      before
                        ? 'Sana guruh yaratilishidan oldin'
                        : onHeaderClick
                          ? 'Shu kun uchun hammaga davomat (keldi / kelmadi)'
                          : undefined
                    }
                    className={cn(
                      'rounded-md px-1 py-0.5 leading-tight transition-colors',
                      onHeaderClick && !before && 'hover:bg-brand-600/10',
                      c.date === today && 'bg-brand-600/10',
                    )}
                  >
                    <span className={cn('block text-[11px]', moved ? 'text-sky-600' : 'text-brand-600')}>
                      {i + 1}-dars
                    </span>
                    <span className={cn('block text-[14px]', before && 'text-[#bdbdbd]')}>
                      {c.date.slice(8, 10)}.{c.date.slice(5, 7)}
                    </span>
                  </button>
                </th>
              )
            })}
            <th className="whitespace-nowrap px-2 pb-2 text-center font-medium">Davomat</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((s, idx) => {
            const att = attendance.get(s.studentId)
            return (
              <tr key={s.studentId} className="h-[49px] border-t border-[#eeeeee] hover:bg-black/[0.02]">
                <td className="sticky left-0 z-10 bg-white px-2">{idx + 1}</td>
                <td className="sticky left-10 z-10 bg-white px-2">
                  <Link
                    to={`/admin/students/${s.studentId}`}
                    state={back}
                    className="whitespace-nowrap text-black no-underline hover:text-brand-600 hover:underline"
                  >
                    {s.fullName}
                  </Link>
                </td>
                <td className="whitespace-nowrap px-2">{phones.get(s.studentId) || ''}</td>
                <td className={cn('whitespace-nowrap px-2', balanceTextCls(s.balance, s.debtMonths))}>
                  {formatMoney(s.balance)}
                </td>
                {columns.map((c) => {
                  const k = kindOf(s, c.date, frozenRows)
                  const clickable = !!onCellClick && k !== 'blocked'
                  return (
                    <td key={c.date} className="px-1 text-center">
                      <button
                        type="button"
                        disabled={!clickable}
                        onClick={() => onCellClick?.(s, c.date)}
                        title={`${s.fullName} — ${formatDate(c.date)}`}
                        className={cn(
                          'inline-flex h-8 w-8 items-center justify-center rounded-full transition-colors',
                          clickable ? 'hover:bg-brand-600/10' : 'cursor-default',
                        )}
                      >
                        <CellIcon kind={k} />
                      </button>
                    </td>
                  )
                })}
                <td className="px-2 text-center">
                  {att?.pct == null ? (
                    <span className="text-[#9e9e9e]">—</span>
                  ) : (
                    <span
                      className={cn(
                        'inline-flex min-w-11 justify-center rounded-md px-2 py-0.5 text-[13px] font-semibold',
                        att.pct >= 90
                          ? 'bg-emerald-50 text-emerald-700'
                          : att.pct >= 75
                            ? 'bg-amber-50 text-amber-700'
                            : 'bg-red-50 text-red-600',
                      )}
                      title={`O'tilgan: ${att.held}, qoldirgan: ${att.absent}`}
                    >
                      {att.pct}%
                    </span>
                  )}
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

function CellIcon({ kind }: { kind: CellKind }) {
  switch (kind) {
    case 'present':
      return <IconCircleCheck className="h-7 w-7 text-[#2e7d32]" stroke={1.6} />
    case 'late':
      return <IconCircleCheck className="h-7 w-7 text-amber-500" stroke={1.6} />
    case 'absent':
      return <IconCircleMinus className="h-7 w-7 text-[#d32f2f]" stroke={1.6} />
    case 'empty':
      return <IconCircle className="h-7 w-7 text-[#bdbdbd]" stroke={1.4} />
    default:
      return <span className="text-[#e0e0e0]">·</span>
  }
}
