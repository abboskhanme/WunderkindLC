import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertCircle, ChevronLeft, ChevronRight, GraduationCap, Percent, Search, Snowflake, UserPlus, Users } from 'lucide-react'
import { getTeacherStudents, type TeacherStudentRow, type TeacherStudents } from '@/api/services/teacher'
import { apiErrorMessage, cn } from '@/lib/utils'
import { currentMonth, shiftMonth } from '@/lib/month'
import { monthTitle, pctTone } from '@/lib/teacherPortal'
import { Avatar, EmptyState, Segmented, SkeletonRows, StatTile, TCard, TPage } from '@/components/teacher/TeacherUi'
import { TelegramLinkCell } from '@/components/messaging/TelegramLinkCell'

type StatusFilter = 'all' | 'active' | 'trial' | 'frozen' | 'low'

const STATUS: Record<string, { label: string; cls: string }> = {
  active: { label: 'Faol', cls: 'bg-emerald-50 text-emerald-700' },
  trial: { label: 'Sinovda', cls: 'bg-violet-50 text-violet-700' },
  frozen: { label: 'Muzlatilgan', cls: 'bg-sky-50 text-sky-700' },
}

/** "Davomati past" chegarasi (%) — filtr va ogohlantirish rangi bilan bir xil (`pctTone`). */
const LOW_PCT = 60

/**
 * O'QUVCHILARIM — barcha guruhlardagi o'quvchilar bitta ro'yxatda: guruh, holat, tanlangan
 * oydagi davomat (keldi / kelmadi / kech, foiz). Ma'lumot `GET /api/teacher/students`
 * (jurnaldagi to'lov darvozasi bilan: jurnalda ko'rinmagan o'quvchi bu yerda ham yo'q).
 * Qidiruv va filtrlar klientda — bir zumda ishlaydi.
 */
export function TeacherStudentsPage() {
  const [month, setMonth] = useState(currentMonth)
  const [data, setData] = useState<TeacherStudents | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [q, setQ] = useState('')
  const [group, setGroup] = useState('')
  const [status, setStatus] = useState<StatusFilter>('all')

  useEffect(() => {
    let alive = true
    getTeacherStudents(month)
      .then((d) => alive && setData(d))
      .catch((err) => alive && setError(apiErrorMessage(err, "O'quvchilarni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [month])

  const rows = useMemo(() => (data && data.month === month ? data.rows : null), [data, month])
  const groups = useMemo(() => {
    const m = new Map<string, string>()
    for (const r of rows ?? []) m.set(r.groupId, r.groupName)
    return [...m.entries()].sort((a, b) => a[1].localeCompare(b[1]))
  }, [rows])

  const filtered = useMemo(() => {
    const s = q.trim().toLowerCase()
    return (rows ?? []).filter(
      (r) =>
        (!s || r.fullName.toLowerCase().includes(s)) &&
        (!group || r.groupId === group) &&
        (status === 'all' ||
          (status === 'low' ? r.attendancePct !== null && r.attendancePct < LOW_PCT : r.status === status)),
    )
  }, [rows, q, group, status])

  const scope = (rows ?? []).filter((r) => !group || r.groupId === group)
  const unique = new Set(scope.map((r) => r.studentId)).size
  const withPct = scope.filter((r) => r.attendancePct !== null && r.status !== 'frozen')
  const avg = withPct.length ? Math.round(withPct.reduce((a, r) => a + (r.attendancePct ?? 0), 0) / withPct.length) : null
  const cnt = (s: string) => scope.filter((r) => r.status === s).length
  const lowCount = scope.filter((r) => r.attendancePct !== null && r.attendancePct < LOW_PCT).length
  const isCurrent = month === currentMonth()

  const changeMonth = (delta: number) => {
    setError(null)
    setMonth((m) => shiftMonth(m, delta))
  }

  return (
    <TPage
      title="O'quvchilarim"
      sub={rows ? `${unique} ta o'quvchi · ${groups.length} ta guruh` : 'Yuklanmoqda…'}
      actions={
        <div className="flex w-full items-center gap-1.5 sm:w-auto">
          <button
            type="button"
            onClick={() => changeMonth(-1)}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-white text-mute hover:text-brand-600"
            aria-label="Oldingi oy"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <span className="flex h-9 min-w-0 flex-1 items-center justify-center rounded-lg border border-line bg-white px-3 text-[13px] font-semibold text-ink sm:w-40 sm:flex-none">
            {monthTitle(month)}
          </span>
          <button
            type="button"
            disabled={isCurrent}
            onClick={() => changeMonth(1)}
            className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg border border-line bg-white text-mute hover:text-brand-600 disabled:opacity-40"
            aria-label="Keyingi oy"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
        </div>
      }
    >
      {error && (
        <div className="mb-3 flex items-center gap-2 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-[13px] text-red-700">
          <AlertCircle className="h-4 w-4 shrink-0" /> {error}
        </div>
      )}

      <div className="mb-3 grid grid-cols-2 gap-2.5 lg:grid-cols-4 lg:gap-4">
        <StatTile icon={Users} label="O'quvchilar" value={rows ? unique : '—'} hint="Takrorsiz" />
        <StatTile icon={GraduationCap} tone="green" label="Faol" value={rows ? cnt('active') : '—'} hint={`Sinovda: ${cnt('trial')}`} />
        <StatTile
          icon={Percent}
          tone={avg === null ? 'brand' : avg >= 85 ? 'green' : avg >= LOW_PCT ? 'amber' : 'red'}
          label="O'rtacha davomat"
          value={avg === null ? '—' : `${avg}%`}
          hint={monthTitle(month)}
        />
        <StatTile icon={Snowflake} tone="sky" label="Muzlatilgan" value={rows ? cnt('frozen') : '—'} hint={`Past davomat: ${lowCount}`} />
      </div>

      <TCard tight>
        {/* ── Filtrlar ── */}
        <div className="flex flex-col gap-2 border-b border-line-soft p-3 lg:flex-row lg:items-center">
          <label className="flex h-9 min-w-0 items-center gap-2 rounded-lg border border-line bg-white px-3 lg:w-64">
            <Search className="h-4 w-4 shrink-0 text-faint" />
            <input
              value={q}
              onChange={(e) => setQ(e.target.value)}
              placeholder="Ism bo'yicha qidirish…"
              className="min-w-0 flex-1 bg-transparent text-[13.5px] text-ink outline-none placeholder:text-faint"
            />
          </label>
          <select
            value={group}
            onChange={(e) => setGroup(e.target.value)}
            className="h-9 rounded-lg border border-line bg-white px-2.5 text-[13.5px] font-semibold text-ink outline-none focus:border-brand-600 lg:w-52"
          >
            <option value="">Barcha guruhlar</option>
            {groups.map(([id, name]) => (
              <option key={id} value={id}>
                {name}
              </option>
            ))}
          </select>
          <Segmented<StatusFilter>
            value={status}
            onChange={setStatus}
            className="lg:ml-auto"
            options={[
              { value: 'all', label: 'Hammasi' },
              { value: 'active', label: 'Faol' },
              { value: 'trial', label: 'Sinov' },
              { value: 'frozen', label: 'Muzlatilgan' },
              { value: 'low', label: `Davomat <${LOW_PCT}%`, count: lowCount },
            ]}
          />
        </div>

        {!rows && !error ? (
          <div className="p-3">
            <SkeletonRows rows={5} />
          </div>
        ) : filtered.length === 0 ? (
          <EmptyState
            icon={UserPlus}
            title={rows && rows.length === 0 ? "O'quvchi yo'q" : 'Topilmadi'}
            text={rows && rows.length === 0 ? "Guruhlaringizda hozircha o'quvchi yo'q." : "Filtr yoki qidiruvni o'zgartiring."}
          />
        ) : (
          <>
            {/* Kompyuter: jadval */}
            <div className="hidden overflow-x-auto md:block">
              <table className="w-full min-w-[760px] text-left text-[13.5px]">
                <thead>
                  <tr className="border-b border-line-soft text-[11.5px] font-bold uppercase tracking-wide text-faint">
                    <th className="w-10 px-3 py-2.5">№</th>
                    <th className="px-3 py-2.5">O'quvchi</th>
                    <th className="px-3 py-2.5">Guruh</th>
                    <th className="px-3 py-2.5">Holat</th>
                    <th className="px-3 py-2.5">Telegram</th>
                    <th className="px-3 py-2.5">Davomat</th>
                    <th className="px-3 py-2.5 text-center">Keldi</th>
                    <th className="px-3 py-2.5 text-center">Kelmadi</th>
                    <th className="px-3 py-2.5 text-center">Kech</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-line-soft">
                  {filtered.map((r, i) => (
                    <tr key={r.studentId + r.groupId} className="transition-colors hover:bg-panel2">
                      <td className="px-3 py-2.5 text-faint">{i + 1}</td>
                      <td className="px-3 py-2.5">
                        <div className="flex items-center gap-2.5">
                          <Avatar name={r.fullName} photoUrl={r.photoUrl} size={32} />
                          <div className="min-w-0">
                            <p className="truncate font-bold text-ink">{r.fullName}</p>
                            {r.debtMonths > 0 && <p className="text-[11.5px] font-semibold text-red-600">To'lov kutilmoqda ({r.debtMonths} oy)</p>}
                          </div>
                        </div>
                      </td>
                      <td className="px-3 py-2.5">
                        <Link to={`/teacher/groups/${r.groupId}`} className="font-semibold text-brand-600 hover:underline">
                          {r.groupName}
                        </Link>
                        <p className="truncate text-[11.5px] text-mute">{r.courseName}</p>
                      </td>
                      <td className="px-3 py-2.5">
                        <StatusBadge status={r.status} />
                      </td>
                      <td className="px-3 py-2.5">
                        <TelegramLinkCell linked={r.telegramLinked} relations={r.telegramRelations} />
                      </td>
                      <td className="px-3 py-2.5">
                        <PctBar pct={r.attendancePct} held={r.held} />
                      </td>
                      <td className="px-3 py-2.5 text-center font-semibold text-emerald-700">{r.attended}</td>
                      <td className="px-3 py-2.5 text-center font-semibold text-red-600">{r.absent || '—'}</td>
                      <td className="px-3 py-2.5 text-center font-semibold text-amber-700">{r.late || '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>

            {/* Telefon: kartalar */}
            <ul className="divide-y divide-line-soft md:hidden">
              {filtered.map((r) => (
                <li key={r.studentId + r.groupId}>
                  <StudentCard r={r} />
                </li>
              ))}
            </ul>
          </>
        )}
      </TCard>
      <p className="mt-2 px-1 text-[12px] text-faint">
        Davomat — tanlangan oyda o'tilgan darslar bo'yicha; kech kelgan «keldi» hisoblanadi. O'quvchi ikki guruhda bo'lsa — har guruh alohida qator.
      </p>
    </TPage>
  )
}

function StudentCard({ r }: { r: TeacherStudentRow }) {
  return (
    <Link to={`/teacher/groups/${r.groupId}`} className="flex items-center gap-3 px-3 py-3 transition-colors active:bg-panel2">
      <Avatar name={r.fullName} photoUrl={r.photoUrl} size={40} />
      <div className="min-w-0 flex-1">
        <div className="flex items-center gap-1.5">
          <p className="truncate text-[14px] font-bold text-ink">{r.fullName}</p>
          <StatusBadge status={r.status} />
        </div>
        <p className="truncate text-[12px] text-mute">
          {r.groupName}
          {r.held > 0 && ` · ${r.attended}/${r.held} dars`}
          {r.absent > 0 && ` · ${r.absent} ta kelmagan`}
        </p>
        {r.debtMonths > 0 && <p className="text-[11.5px] font-semibold text-red-600">To'lov kutilmoqda ({r.debtMonths} oy)</p>}
        <TelegramLinkCell linked={r.telegramLinked} relations={r.telegramRelations} className="mt-0.5 text-[12px]" />
      </div>
      <div className="w-[62px] shrink-0 text-right">
        <p className={cn('text-[16px] font-bold', pctTone(r.attendancePct).text)}>
          {r.attendancePct === null ? '—' : `${r.attendancePct}%`}
        </p>
        <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-panel3">
          <div className={cn('h-full rounded-full', pctTone(r.attendancePct).bar)} style={{ width: `${r.attendancePct ?? 0}%` }} />
        </div>
      </div>
    </Link>
  )
}

function StatusBadge({ status }: { status: string }) {
  const s = STATUS[status] ?? { label: status, cls: 'bg-slate-100 text-slate-600' }
  return <span className={cn('shrink-0 rounded-md px-1.5 py-0.5 text-[11px] font-bold', s.cls)}>{s.label}</span>
}

function PctBar({ pct, held }: { pct: number | null; held: number }) {
  const t = pctTone(pct)
  return (
    <div className="flex items-center gap-2">
      <div className="h-2 w-24 overflow-hidden rounded-full bg-panel3">
        <div className={cn('h-full rounded-full', t.bar)} style={{ width: `${pct ?? 0}%` }} />
      </div>
      <span className={cn('w-10 text-[13px] font-bold', t.text)}>{pct === null ? '—' : `${pct}%`}</span>
      <span className="text-[11.5px] text-faint">{held} dars</span>
    </div>
  )
}
