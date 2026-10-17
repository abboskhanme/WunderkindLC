import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { CalendarRange, PhoneCall, RefreshCw, TrendingDown, UserX, Clock } from 'lucide-react'
import { getAttendancePeriod, type AttendancePeriod } from '@/api/services/attendancePeriod'
import { getTeachers } from '@/api/services/teachers'
import { getClasses } from '@/api/services/classes'
import type { Group, Teacher } from '@/types'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { PageHeader } from '@/components/ui/PageHeader'
import { DateInput } from '@/components/ui/DateInput'
import { usePerm } from '@/lib/permissions'
import { formatPhone } from '@/lib/numberFormat'
import { apiErrorMessage, cn } from '@/lib/utils'
import { todayIso } from '@/lib/month'
import { shiftLocalDay } from '@/lib/localDate'
import { NeedContactModal, type ContactTarget } from './contacts/NeedContactModal'

/**
 * «O'quvchilar davomati» → «Davr tahlili» (TZ 2.1, SPEC-backlog-2026-10-17 A1): davr (≤ 92 kun) bo'yicha davomat,
 * oldingi teng davrga nisbatan pasaygan guruhlar, sabablar statistikasi, sinflar kesimi va davrda ko'p dars
 * qoldirganlar (Service Manager tezkor bog'lanishi uchun — «Bog'lanish kerak» navbatiga yuboriladi).
 */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400'
const chip = (on: boolean) =>
  cn(
    'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
    on ? 'border-brand-500 bg-brand-50 text-brand-700' : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
  )

const RANGES = [7, 30, 92]

const pct = (n: number | null) => (n == null ? '—' : `${n}%`)

/** Davomat foizi rangi: 90%+ → sky, 75%+ → amber, past → rose (yashil/qizil ATAYIN emas — CVD). */
const pctTone = (n: number | null) =>
  n == null ? 'text-slate-400' : n >= 90 ? 'text-sky-600' : n >= 75 ? 'text-amber-600' : 'text-rose-600'

/** "yyyy-MM-dd" → "dd.MM". */
const dm = (d: string) => (d.length >= 10 ? `${d.slice(8, 10)}.${d.slice(5, 7)}` : d)

export function AttendancePeriodView() {
  const { can } = usePerm()
  const today = todayIso()
  const [from, setFrom] = useState(() => shiftLocalDay(today, -29))
  const [to, setTo] = useState(today)
  const [teacherId, setTeacherId] = useState('')
  const [groupId, setGroupId] = useState('')
  const [onlyDropped, setOnlyDropped] = useState(false)
  const [riskMin, setRiskMin] = useState<3 | 5>(3)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [contactOpen, setContactOpen] = useState(false)
  const [tick, setTick] = useState(0)
  const [data, setData] = useState<AttendancePeriod | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [groups, setGroups] = useState<Group[]>([])

  useEffect(() => {
    // Filtr ro'yxatlari — ikkilamchi o'qish: xato bo'lsa filtr bo'sh qoladi, hisobot ishlayveradi.
    getTeachers().then(setTeachers).catch(() => {})
    getClasses(false).then(setGroups).catch(() => {})
  }, [])

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr/filtr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getAttendancePeriod({ from, to, groupId, teacherId })
      .then((d) => { if (active) { setData(d); setError(''); setSelected(new Set()) } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Davomat tahlilini yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [from, to, groupId, teacherId, tick])

  const setRange = (n: number) => { setFrom(shiftLocalDay(today, -(n - 1))); setTo(today) }
  const rangeIs = (n: number) => to === today && from === shiftLocalDay(today, -(n - 1))

  const groupRows = useMemo(() => (data?.groups ?? []).filter((g) => !onlyDropped || g.dropped), [data, onlyDropped])
  const risk = useMemo(() => (data?.atRisk ?? []).filter((r) => r.absent >= riskMin), [data, riskMin])
  const maxReason = Math.max(1, ...(data?.reasons ?? []).map((r) => r.count))
  const droppedCount = data?.groups.filter((g) => g.dropped).length ?? 0

  const targets: ContactTarget[] = risk.filter((r) => selected.has(r.studentId)).map((r) => ({ id: r.studentId, fullName: r.studentName }))
  const toggle = (id: string) => setSelected((s) => {
    const n = new Set(s)
    if (n.has(id)) n.delete(id)
    else n.add(id)
    return n
  })

  const t = data?.total
  const delta = data && t?.percent != null && data.previousTotal.percent != null ? t.percent - data.previousTotal.percent : null

  return (
    <div>
      <PageHeader title="Davr tahlili" sub="Keldi, kelmadi, sababli, sababsiz va kechikish — tanlangan davr bo'yicha (92 kungacha)" />

      <div className="toolbar mb-4 flex-wrap">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <DateInput value={from} max={to} onChange={(e) => e.target.value && setFrom(e.target.value)} className="w-36" />
        <span className="text-slate-400">—</span>
        <DateInput value={to} min={from} max={today} onChange={(e) => e.target.value && setTo(e.target.value)} className="w-36" />
        <div className="flex gap-1">
          {RANGES.map((n) => (
            <button key={n} type="button" onClick={() => setRange(n)} className={chip(rangeIs(n))}>
              {n === 92 ? '3 oy' : `${n} kun`}
            </button>
          ))}
        </div>
        <select value={teacherId} onChange={(e) => setTeacherId(e.target.value)} className={control} aria-label="O'qituvchi">
          <option value="">Barcha o'qituvchilar</option>
          {teachers.map((x) => <option key={x.id} value={x.id}>{x.fullName}</option>)}
        </select>
        <select value={groupId} onChange={(e) => setGroupId(e.target.value)} className={control} aria-label="Guruh">
          <option value="">Barcha guruhlar</option>
          {groups.map((g) => <option key={g.id} value={g.id}>{g.name}</option>)}
        </select>
        <Button variant="secondary" onClick={() => setTick((n) => n + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {loading || !data || !t ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <StatCard label="Davomat" value={pct(t.percent)} icon={CalendarRange}
              hint={`Oldingi ${data.days} kun: ${pct(data.previousTotal.percent)}`}
              delta={delta == null || delta === 0 ? undefined : { value: `${delta > 0 ? '+' : ''}${delta} pp`, dir: delta > 0 ? 'up' : 'down' }} />
            <StatCard label="Darslar (o'quvchi × dars)" value={t.held} icon={CalendarRange}
              iconBg="bg-sky-50" iconColor="text-sky-600" hint={`Keldi ${t.present} · kech ${t.late}`} />
            <StatCard label="Sababsiz" value={t.unexcused} icon={UserX}
              iconBg="bg-rose-50" iconColor="text-rose-600" hint="Kelmagan, sababi yo'q" />
            <StatCard label="Sababli" value={t.excused} icon={UserX}
              iconBg="bg-indigo-50" iconColor="text-indigo-600" hint="Kasal va boshqa sabablar" />
            <StatCard label="Kechikdi" value={t.late} icon={Clock}
              iconBg="bg-amber-50" iconColor="text-amber-600" hint="Kech kelgan — davomatda «keldi»" />
          </div>

          <Card
            title="Guruhlar"
            sub={`Oldingi teng davr (${dm(data.prevFrom)}–${dm(data.prevTo)}) bilan solishtiriladi. ${data.dropPp} foiz punkt va undan ko'p tushgan — «Pasaydi».`}
            actions={
              <label className="inline-flex cursor-pointer items-center gap-2 text-sm text-slate-600">
                <input type="checkbox" checked={onlyDropped} onChange={(e) => setOnlyDropped(e.target.checked)} className="h-4 w-4 accent-brand-600" />
                Faqat pasayganlar ({droppedCount})
              </label>
            }
            tight
          >
            {groupRows.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">{onlyDropped ? 'Davomati pasaygan guruh yo\'q' : "Bu davrda dars o'tilmagan"}</p>
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Guruh</th>
                      <th className="num">O'quvchi</th>
                      <th className="num">Davomat</th>
                      <th className="num">Oldingi</th>
                      <th className="num">O'zgarish</th>
                      <th className="num">Sababsiz</th>
                      <th className="num">Sababli</th>
                      <th className="num">Kech</th>
                    </tr>
                  </thead>
                  <tbody>
                    {groupRows.map((g) => (
                      <tr key={g.groupId} className={cn(g.dropped && 'bg-rose-50/40')}>
                        <td>
                          <Link to={`/admin/classes/${g.groupId}`} className="font-medium text-slate-700 hover:text-brand-600">{g.groupName}</Link>
                          <div className="mt-0.5 flex items-center gap-1.5 text-xs text-slate-400">
                            <span>{[g.courseName, g.teacherName].filter(Boolean).join(' · ')}</span>
                            {g.dropped && <Badge tone="red">Pasaydi</Badge>}
                          </div>
                        </td>
                        <td className="num">{g.students}</td>
                        <td className={cn('num font-semibold tabular-nums', pctTone(g.current.percent))}>{pct(g.current.percent)}</td>
                        <td className="num tabular-nums text-slate-500">{pct(g.previous.percent)}</td>
                        <td className={cn('num font-semibold tabular-nums', g.deltaPp == null ? 'text-slate-400' : g.deltaPp < 0 ? 'text-rose-600' : 'text-sky-600')}>
                          {g.deltaPp == null ? '—' : (
                            <span className="inline-flex items-center gap-1">
                              {g.deltaPp < 0 && <TrendingDown className="h-3.5 w-3.5" />}
                              {g.deltaPp > 0 ? '+' : ''}{g.deltaPp} pp
                            </span>
                          )}
                        </td>
                        <td className="num tabular-nums">{g.current.unexcused || '—'}</td>
                        <td className="num tabular-nums">{g.current.excused || '—'}</td>
                        <td className="num tabular-nums">{g.current.late || '—'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>

          <div className="grid gap-4 lg:grid-cols-2">
            <Card title="Sabablar" sub="Nima sababdan kelmagan yoki kechikkan — belgilangan darslar soni">
              {data.reasons.length === 0 ? (
                <p className="py-6 text-center text-sm text-slate-400">Bu davrda sabab belgilanmagan</p>
              ) : (
                <ul className="space-y-2.5">
                  {data.reasons.map((r) => (
                    <li key={r.reasonId}>
                      <div className="mb-1 flex items-center justify-between gap-2 text-sm">
                        <span className="flex items-center gap-2 font-medium text-slate-700">
                          {r.name}
                          <Badge tone={r.isLate ? 'amber' : r.isExcused ? 'indigo' : 'red'}>
                            {r.isLate ? 'kechikish' : r.isExcused ? 'sababli' : 'sababsiz'}
                          </Badge>
                        </span>
                        <span className="tabular-nums text-slate-500">{r.count} dars · {r.students} o'quvchi</span>
                      </div>
                      <div className="h-2 rounded-full bg-slate-100">
                        <div
                          className={cn('h-2 rounded-full', r.isLate ? 'bg-amber-400' : r.isExcused ? 'bg-indigo-400' : 'bg-rose-400')}
                          style={{ width: `${(r.count / maxReason) * 100}%` }}
                        />
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </Card>

            <Card title="Sinflar kesimi" sub="O'quvchining maktab sinfi bo'yicha" tight>
              {data.grades.length === 0 ? (
                <p className="py-6 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
              ) : (
                <div className="table-wrap">
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Sinf</th>
                        <th className="num">O'quvchi</th>
                        <th className="num">Davomat</th>
                        <th className="num">Sababsiz</th>
                        <th className="num">Sababli</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.grades.map((g) => (
                        <tr key={g.label}>
                          <td className={cn('font-medium', g.grade == null ? 'text-slate-400' : 'text-slate-700')}>{g.label}</td>
                          <td className="num">{g.students}</td>
                          <td className={cn('num font-semibold tabular-nums', pctTone(g.counts.percent))}>{pct(g.counts.percent)}</td>
                          <td className="num tabular-nums">{g.counts.unexcused || '—'}</td>
                          <td className="num tabular-nums">{g.counts.excused || '—'}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          </div>

          <Card
            title="Ko'p dars qoldirganlar"
            sub="Davrda JAMI qoldirilgan darslar (sababli ham, sababsiz ham). Ketma-ket qoldirganlar — «Qizil ro'yxat» tabida."
            actions={
              <div className="flex items-center gap-2">
                <div className="flex gap-1">
                  {([3, 5] as const).map((k) => (
                    <button key={k} type="button" onClick={() => setRiskMin(k)} className={chip(riskMin === k)}>{k}+ dars</button>
                  ))}
                </div>
                {can('contacts', 'create') && (
                  <Button onClick={() => setContactOpen(true)} disabled={targets.length === 0}>
                    <PhoneCall className="h-4 w-4" /> Bog'lanish kerak{targets.length ? ` (${targets.length})` : ''}
                  </Button>
                )}
              </div>
            }
            tight
          >
            {risk.length === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">Davrda {riskMin} va undan ko'p dars qoldirgan o'quvchi yo'q</p>
            ) : (
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      {can('contacts', 'create') && (
                        <th className="w-8">
                          <input type="checkbox" aria-label="Hammasini tanlash" className="h-4 w-4 accent-brand-600"
                            checked={risk.every((r) => selected.has(r.studentId))}
                            onChange={(e) => setSelected(e.target.checked ? new Set(risk.map((r) => r.studentId)) : new Set())} />
                        </th>
                      )}
                      <th>O'quvchi</th>
                      <th>Guruh</th>
                      <th className="num">Qoldirgan</th>
                      <th className="num">Sababsiz</th>
                      <th className="num">Sababli</th>
                      <th className="num">Kech</th>
                      <th className="num">Davomat</th>
                    </tr>
                  </thead>
                  <tbody>
                    {risk.map((r) => (
                      <tr key={r.studentId} className={cn(r.absent >= 5 && 'bg-rose-50/40')}>
                        {can('contacts', 'create') && (
                          <td>
                            <input type="checkbox" aria-label={`${r.studentName} tanlash`} className="h-4 w-4 accent-brand-600"
                              checked={selected.has(r.studentId)} onChange={() => toggle(r.studentId)} />
                          </td>
                        )}
                        <td>
                          <Link to={`/admin/students/${r.studentId}`} className="font-medium text-slate-700 hover:text-brand-600">{r.studentName}</Link>
                          {r.phone && <div className="text-xs tabular-nums text-slate-400">{formatPhone(r.phone)}</div>}
                        </td>
                        <td className="text-sm text-slate-600">{r.groups.join(', ')}</td>
                        <td className={cn('num font-semibold tabular-nums', r.absent >= 5 ? 'text-rose-600' : 'text-amber-600')}>{r.absent}</td>
                        <td className="num tabular-nums">{r.unexcused || '—'}</td>
                        <td className="num tabular-nums">{r.excused || '—'}</td>
                        <td className="num tabular-nums">{r.late || '—'}</td>
                        <td className={cn('num font-semibold tabular-nums', pctTone(r.percent))}>{pct(r.percent)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </Card>
        </div>
      )}

      <NeedContactModal
        open={contactOpen}
        students={targets}
        onClose={() => setContactOpen(false)}
        onCreated={() => setSelected(new Set())}
      />
    </div>
  )
}
