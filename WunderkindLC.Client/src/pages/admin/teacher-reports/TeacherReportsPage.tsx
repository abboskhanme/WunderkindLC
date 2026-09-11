import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { Archive, Activity, TrendingDown, HelpCircle, Users, UserCheck, UserMinus, ArrowUpRight } from 'lucide-react'
import type { TeacherReportRow, TeacherReportDetail } from '@/types'
import { getTeacherReport, getTeacherReportDetail } from '@/api/services/teacherReports'
import { formatDate, cn } from '@/lib/utils'
import { formatMonth } from '@/config/constants'
import { Card } from '@/components/ui/Card'
import { PageHeader } from '@/components/ui/PageHeader'
import { CardTabs } from '@/components/ui/CardTabs'
import { teacherTabs } from '@/config/sectionTabs'
import { StatCard } from '@/components/ui/StatCard'
import { Badge } from '@/components/ui/Badge'
import type { BadgeTone } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { Modal } from '@/components/ui/Modal'

const statusMeta: Record<TeacherReportRow['status'], { label: string; tone: BadgeTone }> = {
  active: { label: 'Faol', tone: 'green' },
  low: { label: 'Sust', tone: 'amber' },
  none: { label: "Ma'lumot yo'q", tone: 'default' },
}

export function TeacherReportsPage() {
  const [rows, setRows] = useState<TeacherReportRow[]>([])
  const [months, setMonths] = useState<string[]>([])
  // "" = Umumiy (barcha oylar yig'indisi) — standart
  const [selectedMonth, setSelectedMonth] = useState('')
  const [loading, setLoading] = useState(true)

  const [detail, setDetail] = useState<TeacherReportDetail | null>(null)
  const [detailOpen, setDetailOpen] = useState(false)
  const [detailLoading, setDetailLoading] = useState(false)

  useEffect(() => {
    setLoading(true)
    getTeacherReport(selectedMonth || undefined)
      .then((o) => {
        setRows(o.rows)
        setMonths(o.months)
      })
      .finally(() => setLoading(false))
  }, [selectedMonth])

  const openDetail = (id: string) => {
    setDetailOpen(true)
    setDetail(null)
    setDetailLoading(true)
    getTeacherReportDetail(id, selectedMonth || undefined)
      .then(setDetail)
      .finally(() => setDetailLoading(false))
  }

  const monthLabel = selectedMonth ? formatMonth(selectedMonth) : 'Umumiy'

  const counts = {
    active: rows.filter((r) => r.status === 'active').length,
    low: rows.filter((r) => r.status === 'low').length,
    none: rows.filter((r) => r.status === 'none').length,
  }

  const totals = {
    came: rows.reduce((s, r) => s + r.came, 0),
    active: rows.reduce((s, r) => s + r.active, 0),
    left: rows.reduce((s, r) => s + r.left, 0),
    remaining: rows.reduce((s, r) => s + r.remaining, 0),
  }

  return (
    <div>
      {/* Bu sahifaga faqat `teacherReports` ruxsati bilan kiriladi — card har doim ko'rinadi */}
      <CardTabs items={teacherTabs(() => true)} className="mb-5" />
      <PageHeader
        title="O'qituvchilar hisoboti"
        sub={`Dars o'tilishi, baho, mavzu, uy vazifa faolligi va o'quvchi konversiyasi — ${monthLabel}`}
      />

      <div className="mb-4 flex flex-wrap gap-2">
        <button
          type="button"
          onClick={() => setSelectedMonth('')}
          className={cn(
            'rounded-lg px-3 py-1.5 text-sm font-medium transition',
            selectedMonth === ''
              ? 'bg-brand-600 text-white shadow-sm'
              : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
          )}
        >
          Umumiy
        </button>
        {months.map((m) => (
          <button
            key={m}
            type="button"
            onClick={() => setSelectedMonth(m)}
            className={cn(
              'rounded-lg px-3 py-1.5 text-sm font-medium transition',
              selectedMonth === m
                ? 'bg-brand-600 text-white shadow-sm'
                : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
            )}
          >
            {formatMonth(m)}
          </button>
        ))}
      </div>

      <div className="mb-4 grid grid-cols-1 gap-4 sm:grid-cols-3">
        <StatCard
          label="Faol o'qituvchilar"
          value={counts.active}
          icon={Activity}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
        <StatCard
          label="Sust"
          value={counts.low}
          icon={TrendingDown}
          iconBg="bg-amber-50"
          iconColor="text-amber-600"
        />
        <StatCard
          label="Ma'lumot yo'q"
          value={counts.none}
          icon={HelpCircle}
          iconBg="bg-slate-100"
          iconColor="text-slate-500"
        />
      </div>

      <div className="mb-6 grid grid-cols-2 gap-4 sm:grid-cols-4">
        <StatCard
          label="Jami kelgan o'quvchilar"
          value={totals.came}
          icon={Users}
          iconBg="bg-blue-50"
          iconColor="text-blue-600"
        />
        <StatCard
          label="Faol o'quvchilar"
          value={totals.active}
          icon={UserCheck}
          iconBg="bg-emerald-50"
          iconColor="text-emerald-600"
        />
        <StatCard
          label="Qolgan (hozir faol)"
          value={totals.remaining}
          icon={Users}
          iconBg="bg-brand-50"
          iconColor="text-brand-600"
        />
        <StatCard
          label="Ketgan o'quvchilar"
          value={totals.left}
          icon={UserMinus}
          iconBg="bg-red-50"
          iconColor="text-red-500"
        />
      </div>

      <Card tight>
        {loading ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>O'qituvchi</th>
                  <th className="num">Reja</th>
                  <th className="num">O'tdi</th>
                  <th className="num">Bajarildi</th>
                  <th className="num">Baho</th>
                  <th className="num">Mavzu</th>
                  <th className="num">Uy vaz.</th>
                  <th>Oxirgi faollik</th>
                  <th className="num">Kelgan</th>
                  <th className="num">Faol</th>
                  <th className="num">Sinov</th>
                  <th className="num">Muzl.</th>
                  <th className="num">Ketgan</th>
                  <th className="num">Qolgan</th>
                  <th className="num">Konv.%</th>
                  <th>Holat</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr
                    key={r.teacherId}
                    onClick={() => openDetail(r.teacherId)}
                    className="cursor-pointer"
                  >
                    <td className="font-medium text-slate-800">
                      <span className="flex items-center gap-2">
                        {r.fullName}
                        {r.isArchived && (
                          <Badge tone="default">
                            <Archive className="h-3 w-3" /> arxiv
                          </Badge>
                        )}
                      </span>
                    </td>
                    <td className="num text-slate-600">{r.expected}</td>
                    <td className="num text-slate-600">{r.conducted}</td>
                    <td className="num">
                      <PctCell v={r.donePct} />
                    </td>
                    <td className="num text-slate-600">{r.grades}</td>
                    <td className="num">
                      <PctCell v={r.topicPct} />
                    </td>
                    <td className="num">
                      <PctCell v={r.homeworkPct} />
                    </td>
                    <td className="font-mono text-slate-500">
                      {r.lastActivity ? formatDate(r.lastActivity) : '—'}
                    </td>
                    <td className="num font-mono text-slate-600">{r.came}</td>
                    <td className="num font-mono text-emerald-600 font-semibold">{r.active}</td>
                    <td className="num font-mono text-blue-500">{r.trial}</td>
                    <td className="num font-mono text-amber-500">{r.frozen}</td>
                    <td className="num font-mono text-red-400">{r.left}</td>
                    <td className="num font-mono font-semibold text-brand-600">{r.remaining}</td>
                    <td className="num">
                      <PctCell v={r.conversionPct} />
                    </td>
                    <td>
                      <StatusBadge status={r.status} />
                    </td>
                  </tr>
                ))}
                {rows.length === 0 && (
                  <tr>
                    <td colSpan={15} className="px-4 py-12 text-center text-slate-400">
                      O'qituvchi yo'q
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        )}
      </Card>

      <Modal
        open={detailOpen}
        onClose={() => setDetailOpen(false)}
        size="xl"
        title={detail ? `${detail.fullName} — hisobot (${monthLabel})` : 'Hisobot'}
      >
        {detailLoading || !detail ? (
          <Loader label="Yuklanmoqda..." />
        ) : (
          <div className="space-y-4">
            <Link
              to={`/admin/teachers/${detail.teacherId}`}
              className="inline-flex items-center gap-1 text-sm font-medium text-brand-600 hover:underline"
            >
              To'liq profil
              <ArrowUpRight className="h-3.5 w-3.5" />
            </Link>
            <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
              <Metric label="Reja" value={String(detail.expected)} />
              <Metric label="O'tildi" value={`${detail.conducted}`} />
              <Metric label="Bajarildi" value={detail.donePct == null ? '—' : `${detail.donePct}%`} />
              <Metric label="Baholar" value={String(detail.grades)} />
            </div>

            <div>
              <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">
                O'quvchilar lifecycle
              </p>
              <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 lg:grid-cols-7">
                <LifecycleMetric label="Kelgan" value={detail.came} color="text-slate-700" />
                <LifecycleMetric label="Faol" value={detail.active} color="text-emerald-600" />
                <LifecycleMetric label="Sinov" value={detail.trial} color="text-blue-500" />
                <LifecycleMetric label="Muzlatilgan" value={detail.frozen} color="text-amber-500" />
                <LifecycleMetric label="Ketgan" value={detail.left} color="text-red-500" />
                <LifecycleMetric label="Qolgan" value={detail.remaining} color="text-brand-600" />
                <LifecycleMetric
                  label="Konversiya"
                  value={detail.conversionPct == null ? '—' : `${detail.conversionPct}%`}
                  color={
                    detail.conversionPct == null
                      ? 'text-slate-400'
                      : detail.conversionPct >= 70
                        ? 'text-emerald-600'
                        : detail.conversionPct >= 40
                          ? 'text-amber-600'
                          : 'text-red-600'
                  }
                />
              </div>
            </div>

            <div className="table-wrap rounded-xl border border-slate-100">
              <table className="table">
                <thead>
                  <tr>
                    <th>Guruh</th>
                    <th>Fan</th>
                    <th className="num">Reja</th>
                    <th className="num">O'tdi</th>
                    <th className="num">Bajarildi</th>
                    <th className="num">Baho</th>
                    <th className="num">Mavzu</th>
                    <th className="num">Uy vaz.</th>
                  </tr>
                </thead>
                <tbody>
                  {detail.rows.map((b, i) => (
                    <tr key={i}>
                      <td className="font-medium text-slate-700">{b.className}</td>
                      <td className="text-slate-600">{b.subjectName}</td>
                      <td className="num text-slate-600">{b.expected}</td>
                      <td className="num text-slate-600">{b.conducted}</td>
                      <td className="num">
                        <PctCell v={b.donePct} />
                      </td>
                      <td className="num text-slate-600">{b.grades}</td>
                      <td className="num">
                        <PctCell v={b.topicPct} />
                      </td>
                      <td className="num">
                        <PctCell v={b.homeworkPct} />
                      </td>
                    </tr>
                  ))}
                  {detail.rows.length === 0 && (
                    <tr>
                      <td colSpan={8} className="px-3 py-8 text-center text-slate-400">
                        Bu davr uchun ma'lumot yo'q
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>
        )}
      </Modal>
    </div>
  )
}

function PctCell({ v }: { v: number | null }) {
  if (v == null) return <span className="font-mono text-slate-300">—</span>
  const color = v >= 70 ? 'text-emerald-600' : v >= 40 ? 'text-amber-600' : 'text-red-600'
  return <span className={cn('font-mono font-semibold', color)}>{v}%</span>
}

function StatusBadge({ status }: { status: TeacherReportRow['status'] }) {
  const m = statusMeta[status]
  return (
    <Badge tone={m.tone} dot>
      {m.label}
    </Badge>
  )
}

function Metric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-xl bg-slate-50 px-3 py-2">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-0.5 font-mono text-lg font-semibold text-slate-800">{value}</p>
    </div>
  )
}

function LifecycleMetric({
  label,
  value,
  color,
}: {
  label: string
  value: number | string
  color: string
}) {
  return (
    <div className="rounded-xl border border-slate-100 bg-slate-50 px-3 py-2 text-center">
      <p className="text-xs font-semibold uppercase tracking-wide text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-mono text-xl font-semibold', color)}>{value}</p>
    </div>
  )
}
