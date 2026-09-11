import { useCallback, useEffect, useState } from 'react'
import {
  Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { CircleCheckBig, Clock, ListChecks, Target, TriangleAlert, Users } from 'lucide-react'
import { getDashboard, type WorkTaskDashboard } from '@/api/services/workTasks'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { Select } from '@/components/ui/Input'
import { StatCard } from '@/components/ui/StatCard'
import { usePerm } from '@/lib/permissions'
import { cn, apiErrorMessage } from '@/lib/utils'
import { TaskModal } from './TaskModal'
import { dayIso, priorityOf, shortDay, todayIso, useBoardParam, useTasksMeta } from './model'
import { AssigneeChip, DueChip, TasksShell } from './shared'

const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }

/** Oraliq tanlovlari — nazorat odatda "shu hafta / shu oy" kesimida qilinadi. */
const RANGES: { value: number; label: string }[] = [
  { value: 7, label: 'Oxirgi 7 kun' },
  { value: 30, label: 'Oxirgi 30 kun' },
  { value: 90, label: 'Oxirgi 90 kun' },
]

/**
 * NAZORAT PANELI — "adminlar qilinadigan ishlarini bajaryaptimi?" degan savolning javobi.
 *
 * <p>Yuqorida umumiy raqamlar (HOZIRGI holat), o'rtada kunlik dinamika (oraliq bo'yicha:
 * qancha topshiriq berildi va qanchasi yopildi), pastda esa XODIMLAR KESIMI — kimda qancha
 * ochiq/kechikkan ish borligi va muddatida bajarish ulushi. Oxirida "e'tibor talab qiladi" —
 * darhol aralashish kerak bo'lgan topshiriqlar.</p>
 */
export function TasksDashboardPage() {
  const { can } = usePerm()
  const canEdit = can('tasks.board', 'edit')
  const { boards, assignees, loading: metaLoading, reload } = useTasksMeta()
  const [boardId, setBoardId] = useBoardParam(boards)
  const [days, setDays] = useState(30)
  const [allBoards, setAllBoards] = useState(true)

  const [data, setData] = useState<WorkTaskDashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const [modal, setModal] = useState<{ open: boolean; taskId: string | null }>({
    open: false,
    taskId: null,
  })

  const today = todayIso()

  // ⚠️ So'rov AYNAN effektda bajariladi va setState faqat javob kelgach chaqiriladi: effekt
  // tanasida to'g'ridan-to'g'ri setState (masalan `setLoading(true)`) kaskad renderga olib
  // keladi (React qoidasi). Qayta yuklash esa `tick` hisoblagichi orqali so'raladi.
  useEffect(() => {
    let alive = true
    const to = new Date()
    const from = new Date()
    from.setDate(to.getDate() - (days - 1))
    getDashboard({
      boardId: allBoards ? undefined : boardId,
      from: dayIso(from),
      to: dayIso(to),
    })
      .then((d) => {
        if (!alive) return
        setData(d)
        setLoading(false)
      })
      .catch((err) => {
        if (!alive) return
        setLoading(false)
        alert(apiErrorMessage(err, "Statistikani yuklab bo'lmadi"))
      })
    return () => {
      alive = false
    }
  }, [boardId, days, allBoards, tick])

  const load = useCallback(() => setTick((t) => t + 1), [])

  const board = boards.find((b) => b.id === boardId) ?? null

  if (metaLoading || (loading && !data)) return <Loader label="Yuklanmoqda..." />

  const trend = (data?.trend ?? []).map((p) => ({
    name: shortDay(p.date),
    Berildi: p.created,
    Bajarildi: p.done,
  }))

  return (
    <TasksShell
      sub="Xodimlar kesimida topshiriqlarning bajarilishi, kechikishlar va dinamika"
      actions={
        <>
          <Select
            value={allBoards ? '' : boardId}
            onChange={(e) => {
              const v = e.target.value
              setAllBoards(v === '')
              if (v) setBoardId(v)
            }}
            className="w-auto"
          >
            <option value="">Barcha doskalar</option>
            {boards.map((b) => (
              <option key={b.id} value={b.id}>
                {b.title}
              </option>
            ))}
          </Select>
          <Select
            value={String(days)}
            onChange={(e) => setDays(Number(e.target.value))}
            className="w-auto"
          >
            {RANGES.map((r) => (
              <option key={r.value} value={String(r.value)}>
                {r.label}
              </option>
            ))}
          </Select>
        </>
      }
    >
      {/* ---------- Umumiy raqamlar ---------- */}
      <div className="mb-5 grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
        <StatCard label="Jami topshiriq" value={data?.total ?? 0} icon={ListChecks} />
        <StatCard
          label="Bajarilgan"
          value={data?.done ?? 0}
          icon={CircleCheckBig}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
          hint={`${data?.rate ?? 0}% bajarilish`}
        />
        <StatCard
          label="Ochiq"
          value={data?.open ?? 0}
          icon={Clock}
          iconBg="bg-sky-50"
          iconColor="text-sky-600"
        />
        <StatCard
          label="Muddati o'tgan"
          value={data?.overdue ?? 0}
          icon={TriangleAlert}
          iconBg="bg-rose-50"
          iconColor="text-rose-600"
          hint={data && data.overdue > 0 ? 'darhol ko’rib chiqing' : undefined}
        />
        <StatCard
          label="Bugun muddati"
          value={data?.dueToday ?? 0}
          icon={Target}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
        />
      </div>

      {/* ---------- Dinamika ---------- */}
      <Card
        className="mb-5"
        title="Kunlik dinamika"
        sub="Qancha topshiriq berildi va qanchasi yopildi"
      >
        {trend.every((p) => p.Berildi === 0 && p.Bajarildi === 0) ? (
          <p className="py-10 text-center text-sm text-slate-400">
            Bu oraliqda harakat bo'lmagan.
          </p>
        ) : (
          <ResponsiveContainer width="100%" height={240}>
            <BarChart data={trend} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
              <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
              <XAxis
                dataKey="name"
                tickLine={false}
                axisLine={false}
                tick={axisTick}
                interval="preserveStartEnd"
              />
              <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} width={36} />
              <Tooltip contentStyle={tooltipStyle} />
              <Legend wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="Berildi" fill="#7c3aed" radius={[4, 4, 0, 0]} maxBarSize={18} />
              <Bar dataKey="Bajarildi" fill="#10b981" radius={[4, 4, 0, 0]} maxBarSize={18} />
            </BarChart>
          </ResponsiveContainer>
        )}
      </Card>

      {/* ---------- Xodimlar kesimi ---------- */}
      <Card
        className="mb-5"
        tight
        title="Xodimlar kesimi"
        sub="Kechikkanlari ko'p bo'lgan xodim tepada turadi"
      >
        {(data?.rows.length ?? 0) === 0 ? (
          <div className="flex flex-col items-center gap-3 py-12 text-center">
            <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400">
              <Users className="h-6 w-6" />
            </div>
            <p className="text-sm text-slate-500">Hali birorta xodimga topshiriq biriktirilmagan.</p>
          </div>
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>Xodim</th>
                  <th className="num">Jami</th>
                  <th className="num">Bajarildi</th>
                  <th className="num">Ochiq</th>
                  <th className="num">Kechikkan</th>
                  <th className="num">Bugungi</th>
                  <th className="num">Muddatida</th>
                  <th>Bajarilish</th>
                </tr>
              </thead>
              <tbody>
                {data?.rows.map((r) => (
                  <tr key={r.userId}>
                    <td>
                      <div className="min-w-0">
                        <p className="truncate font-semibold text-slate-800">{r.fullName}</p>
                        {r.position && (
                          <p className="truncate text-[11px] text-slate-400">{r.position}</p>
                        )}
                      </div>
                    </td>
                    <td className="num">{r.total}</td>
                    <td className="num text-emerald-600">{r.done}</td>
                    <td className="num">{r.open}</td>
                    <td className={cn('num', r.overdue > 0 && 'font-bold text-rose-600')}>
                      {r.overdue}
                    </td>
                    <td className="num">{r.dueToday}</td>
                    <td className="num" title="Muddatida yopilgan / kechikib yopilgan">
                      {r.doneOnTime}
                      <span className="text-slate-300"> / </span>
                      <span className={cn(r.doneLate > 0 && 'text-amber-600')}>{r.doneLate}</span>
                    </td>
                    <td className="min-w-[9rem]">
                      <div className="flex items-center gap-2">
                        <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                          <div
                            className={cn(
                              'h-full rounded-full',
                              r.rate >= 80
                                ? 'bg-emerald-500'
                                : r.rate >= 50
                                  ? 'bg-amber-500'
                                  : 'bg-rose-500',
                            )}
                            style={{ width: `${Math.min(100, Math.max(0, r.rate))}%` }}
                          />
                        </div>
                        <span className="w-9 shrink-0 text-right font-mono text-[11.5px] font-semibold text-slate-500">
                          {r.rate}%
                        </span>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      {/* ---------- E'tibor talab qiladi ---------- */}
      <Card
        tight
        title="E'tibor talab qiladi"
        sub="Muddati o'tgan va muhimligi yuqori ochiq topshiriqlar"
      >
        {(data?.attention.length ?? 0) === 0 ? (
          <p className="py-10 text-center text-sm text-slate-400">
            Kechikkan yoki shoshilinch topshiriq yo'q — hammasi joyida.
          </p>
        ) : (
          <div className="divide-y divide-slate-100">
            {data?.attention.map((t) => {
              const p = priorityOf(t.priority)
              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setModal({ open: true, taskId: t.id })}
                  className="flex w-full items-center gap-3 px-[18px] py-3 text-left transition-colors hover:bg-slate-50"
                >
                  <span
                    className="h-8 w-1 shrink-0 rounded-full"
                    style={{ background: p.accent }}
                  />
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-semibold text-slate-800">{t.title}</p>
                    <div className="mt-0.5 flex items-center gap-2">
                      <AssigneeChip task={t} />
                    </div>
                  </div>
                  <DueChip task={t} today={today} />
                </button>
              )
            })}
          </div>
        )}
      </Card>

      <TaskModal
        open={modal.open}
        onClose={() => setModal({ open: false, taskId: null })}
        board={board}
        assignees={assignees}
        taskId={modal.taskId}
        canWrite={canEdit}
        onSaved={() => {
          load()
          reload()
        }}
      />
    </TasksShell>
  )
}
