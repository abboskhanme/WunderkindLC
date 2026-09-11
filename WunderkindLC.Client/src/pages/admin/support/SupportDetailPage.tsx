import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowLeft, Calendar, CheckCircle2, CalendarClock, Clock } from 'lucide-react'
import { getSupportTeacher, type SupportTeacherDetail, type SupportSlot } from '@/api/services/support'
import { Card } from '@/components/ui/Card'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { Badge, type BadgeTone } from '@/components/ui/Badge'
import { cn, formatDate } from '@/lib/utils'

type Filter = 'all' | 'done' | 'booked' | 'open'

const STATUS_META: Record<SupportSlot['status'], { label: string; tone: BadgeTone }> = {
  open: { label: "Bo'sh", tone: 'default' },
  booked: { label: 'Bron', tone: 'amber' },
  done: { label: "O'tilgan", tone: 'green' },
}

const FILTERS: { key: Filter; label: string }[] = [
  { key: 'all', label: 'Hammasi' },
  { key: 'done', label: "O'tilgan" },
  { key: 'booked', label: 'Bron' },
  { key: 'open', label: "Bo'sh" },
]

export function SupportDetailPage() {
  const { id = '' } = useParams<{ id: string }>()
  const navigate = useNavigate()

  const [loading, setLoading] = useState(true)
  const [detail, setDetail] = useState<SupportTeacherDetail | null>(null)
  const [filter, setFilter] = useState<Filter>('all')

  useEffect(() => {
    getSupportTeacher(id)
      .then(setDetail)
      .catch(() => setDetail(null))
      .finally(() => setLoading(false))
  }, [id])

  const slots = detail?.slots ?? []

  const counts = useMemo(
    () => ({
      total: slots.length,
      booked: slots.filter((s) => s.status === 'booked').length,
      done: slots.filter((s) => s.status === 'done').length,
    }),
    [slots],
  )

  const visible = useMemo(
    () => (filter === 'all' ? slots : slots.filter((s) => s.status === filter)),
    [slots, filter],
  )

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!detail)
    return (
      <Card>
        <p className="py-10 text-center text-slate-400">Support o'qituvchi topilmadi.</p>
      </Card>
    )

  return (
    <div>
      <PageHeader
        title={
          <span className="flex items-center gap-2">
            <button
              onClick={() => navigate('/admin/support')}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-400 transition-colors hover:bg-slate-50 hover:text-slate-600"
              title="Orqaga"
            >
              <ArrowLeft className="h-5 w-5" />
            </button>
            {detail.fullName}
          </span>
        }
        sub={detail.phone ? `Telefon: ${detail.phone}` : 'Telefon kiritilmagan'}
      />

      {/* KPI plitkalar */}
      <div className="mb-4 grid grid-cols-3 gap-3">
        <Card className="text-center">
          <div className="mx-auto mb-2 flex h-9 w-9 items-center justify-center rounded-lg bg-slate-100 text-slate-600">
            <Calendar className="h-5 w-5" />
          </div>
          <div className="font-mono text-2xl font-bold text-slate-800">{counts.total}</div>
          <div className="text-xs text-slate-400">Jami slot</div>
        </Card>
        <Card className="text-center">
          <div className="mx-auto mb-2 flex h-9 w-9 items-center justify-center rounded-lg bg-amber-50 text-amber-600">
            <CalendarClock className="h-5 w-5" />
          </div>
          <div className="font-mono text-2xl font-bold text-amber-600">{counts.booked}</div>
          <div className="text-xs text-slate-400">Bron</div>
        </Card>
        <Card className="text-center">
          <div className="mx-auto mb-2 flex h-9 w-9 items-center justify-center rounded-lg bg-emerald-50 text-emerald-600">
            <CheckCircle2 className="h-5 w-5" />
          </div>
          <div className="font-mono text-2xl font-bold text-emerald-600">{counts.done}</div>
          <div className="text-xs text-slate-400">O'tilgan dars</div>
        </Card>
      </div>

      {/* Filtr */}
      <div className="mb-4 flex gap-1 rounded-lg bg-slate-100 p-1 text-sm">
        {FILTERS.map((f) => (
          <button
            key={f.key}
            onClick={() => setFilter(f.key)}
            className={cn(
              'flex flex-1 items-center justify-center gap-1.5 rounded-md py-1.5 font-medium transition-colors',
              filter === f.key ? 'bg-white text-slate-800 shadow-sm' : 'text-slate-500 hover:text-slate-700',
            )}
          >
            {f.label}
          </button>
        ))}
      </div>

      <Card
        title="Darslar"
        sub="O'tilgan darslar — qaysi o'quvchi, qachon va qaysi mavzu bo'yicha"
      >
        {visible.length === 0 ? (
          <p className="py-8 text-center text-sm text-slate-400">Mos slot yo'q.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-3 py-2">Sana</th>
                  <th className="px-3 py-2">Vaqt</th>
                  <th className="px-3 py-2">Holat</th>
                  <th className="px-3 py-2">O'quvchi</th>
                  <th className="px-3 py-2">Mavzu</th>
                  <th className="px-3 py-2">Izoh</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {visible.map((s) => {
                  const meta = STATUS_META[s.status]
                  return (
                    <tr key={s.id} className="hover:bg-slate-50/60">
                      <td className="px-3 py-2 whitespace-nowrap text-slate-600">{formatDate(s.date)}</td>
                      <td className="px-3 py-2 whitespace-nowrap font-mono text-slate-500">
                        <span className="inline-flex items-center gap-1.5">
                          <Clock className="h-3.5 w-3.5 text-slate-300" />
                          {s.startTime}–{s.endTime}
                        </span>
                      </td>
                      <td className="px-3 py-2">
                        <Badge tone={meta.tone} dot>{meta.label}</Badge>
                      </td>
                      <td className="px-3 py-2 font-medium text-slate-700">
                        {s.studentName || <span className="font-normal text-slate-300">—</span>}
                      </td>
                      <td className="px-3 py-2 text-slate-600">
                        {s.topic || <span className="text-slate-300">—</span>}
                      </td>
                      <td className="px-3 py-2 text-slate-500">
                        {s.notes || <span className="text-slate-300">—</span>}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </Card>
    </div>
  )
}
