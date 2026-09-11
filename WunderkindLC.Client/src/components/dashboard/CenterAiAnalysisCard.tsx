import { useEffect, useState, useCallback } from 'react'
import {
  Sparkles,
  RefreshCw,
  TrendingUp,
  TrendingDown,
  Minus,
  AlertTriangle,
  Lightbulb,
  UserPlus,
  UserMinus,
  Users,
  Star,
  Banknote,
  Target,
} from 'lucide-react'
import {
  Bar,
  BarChart,
  Area,
  AreaChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  PieChart,
  Pie,
  Cell,
  RadialBarChart,
  RadialBar,
  PolarAngleAxis,
} from 'recharts'
import {
  getCenterAiAnalysis,
  runCenterAiAnalysis,
} from '@/api/services/aiAnalysis'
import type { CenterAiRecord, CenterPoint } from '@/types'
import { useAuth } from '@/context/auth-context'
import { cn, formatMoney, formatDate } from '@/lib/utils'

const PIE_COLORS = ['#6d5ef8', '#10b981', '#f59e0b', '#ef4444', '#3b82f6', '#14b8a6', '#ec4899']

/** Qisqa pul formati: 1.2 mln / 340 ming / 500 (diagramma va katta raqamlar uchun). */
function shortSom(n: number): string {
  const a = Math.abs(n)
  if (a >= 1_000_000) return `${(n / 1_000_000).toFixed(a >= 10_000_000 ? 0 : 1)} mln`
  if (a >= 1_000) return `${Math.round(n / 1_000)} ming`
  return `${Math.round(n)}`
}

export function CenterAiAnalysisCard() {
  const { user } = useAuth()
  const isSuper = user?.role === 'superadmin'
  const [rec, setRec] = useState<CenterAiRecord | null>(null)
  const [loading, setLoading] = useState(true)
  const [running, setRunning] = useState(false)
  const [msg, setMsg] = useState<string | null>(null)

  const load = useCallback(async () => {
    setLoading(true)
    try {
      setRec(await getCenterAiAnalysis())
    } catch {
      /* ignore */
    } finally {
      setLoading(false)
    }
  }, [])

  useEffect(() => {
    void load()
  }, [load])

  async function run(force: boolean) {
    setRunning(true)
    setMsg(null)
    try {
      const res = await runCenterAiAnalysis(force)
      if (!res.ok) setMsg(res.error || 'Tahlil yaratilmadi')
      else {
        setRec(res.record)
        if (res.alreadyToday)
          setMsg('Bugungi tahlil allaqachon tayyor. Qayta tahlil uchun "Qayta" tugmasi (superadmin).')
      }
    } catch (e) {
      setMsg(e instanceof Error ? e.message : 'Xatolik yuz berdi')
    } finally {
      setRunning(false)
    }
  }

  const today = new Date().toISOString().slice(0, 10)
  const isToday = rec?.date === today

  return (
    <div className="card overflow-hidden p-0">
      {/* Sarlavha */}
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 bg-gradient-to-r from-violet-50 to-indigo-50 px-4 py-3">
        <div className="flex items-center gap-2.5">
          <div className="grid h-9 w-9 place-items-center rounded-xl bg-gradient-to-br from-violet-500 to-indigo-500 text-white shadow-sm">
            <Sparkles className="h-5 w-5" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-slate-800">AI Tahlil</h3>
            <p className="text-[11px] text-slate-500">
              {rec
                ? `${formatDate(rec.date)}${isToday ? ' · bugun' : ''} · ${rec.model}`
                : 'Markaz bo’yicha kunlik sun’iy intellekt tahlili'}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          {rec && <TrendChip trend={rec.ai.trend} />}
          <button
            type="button"
            onClick={() => run(false)}
            disabled={running}
            className="btn btn-secondary btn-sm inline-flex items-center gap-1.5"
          >
            <RefreshCw className={cn('h-3.5 w-3.5', running && 'animate-spin')} />
            {rec ? 'Yangilash' : 'Tahlil qilish'}
          </button>
          {isSuper && rec && isToday && (
            <button
              type="button"
              onClick={() => run(true)}
              disabled={running}
              className="btn btn-ghost btn-sm"
              title="Bugungi tahlilni qayta yaratish"
            >
              Qayta
            </button>
          )}
        </div>
      </div>

      <div className="p-4">
        {msg && (
          <div className="mb-3 rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-700">
            {msg}
          </div>
        )}

        {loading ? (
          <p className="py-8 text-center text-sm text-slate-400">Yuklanmoqda...</p>
        ) : !rec ? (
          <div className="py-8 text-center">
            <p className="text-sm text-slate-500">AI tahlil hali tayyorlanmagan.</p>
            <p className="mt-1 text-xs text-slate-400">
              Har kuni ertalab soat 8:00 da avtomatik yaratiladi yoki hozir qo'lda
              tahlil qilishingiz mumkin (Gemini kaliti sozlangan bo'lsa).
            </p>
          </div>
        ) : (
          <Body rec={rec} />
        )}
      </div>
    </div>
  )
}

function Body({ rec }: { rec: CenterAiRecord }) {
  const { ai, revenue, metrics } = rec
  const collectPct =
    revenue.expectedThisMonth > 0
      ? Math.min(100, (revenue.collectedThisMonth / revenue.expectedThisMonth) * 100)
      : 0
  const gradeDelta = metrics.avgGradeThisMonth - metrics.avgGradePrevMonth

  return (
    <div className="space-y-5">
      {/* ====== Yuqori KPI band: salomatlik halqasi + 4 ko'rsatkich ====== */}
      <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-3 lg:grid-cols-5">
        <HealthRing value={rec.health} />
        <Kpi icon={UserPlus} label="Yangi lidlar" value={metrics.newLeadsThisMonth}
          sub={`kecha +${metrics.newLeadsYesterday}`} color="violet" />
        <Kpi icon={Target} label="Konversiya" value={metrics.convertedThisMonth} color="emerald" />
        <Kpi icon={Users} label="Aktiv o'quvchi" value={metrics.activeStudents} color="blue" />
        <Kpi icon={UserMinus} label="Ketganlar" value={metrics.departedThisMonth} color="red" />
      </div>

      {/* Umumiy xulosa */}
      {ai.umumiy && (
        <p className="rounded-xl bg-gradient-to-br from-violet-50/60 to-slate-50 p-3.5 text-sm leading-relaxed text-slate-700">
          {ai.umumiy}
        </p>
      )}

      {/* ====== Moliya: pul kartalari + yig'ilish + 14 kunlik diagramma ====== */}
      <section>
        <SecTitle icon={Banknote} text="Tushum prognozi (joriy oy)" />
        <div className="grid gap-3 lg:grid-cols-2">
          <div>
            <div className="grid grid-cols-2 gap-2.5">
              <Money label="Yig'ilgan" value={revenue.collectedThisMonth} accent="emerald" />
              <Money label="Kutilayotgan" value={revenue.expectedThisMonth} />
              <Money label="Oy oxiri prognoz" value={revenue.predictedMonthEnd} accent="violet" />
              <Money label="Qarzdorlik" value={revenue.outstandingDebt} accent="red" />
            </div>
            <div className="mt-3">
              <div className="mb-1 flex items-center justify-between text-[11px] text-slate-500">
                <span>Yig'ilish darajasi</span>
                <span className="font-mono font-medium text-slate-700">{Math.round(collectPct)}%</span>
              </div>
              <div className="h-2.5 overflow-hidden rounded-full bg-slate-100">
                <div className="h-full rounded-full bg-gradient-to-r from-emerald-400 to-emerald-500"
                  style={{ width: `${collectPct}%` }} />
              </div>
              <p className="mt-1.5 text-[11px] text-slate-400">
                Kechagi tushum: <span className="font-mono text-slate-600">{formatMoney(revenue.yesterdayIncome)}</span>
              </p>
            </div>
          </div>
          <div>
            <p className="mb-1 text-[11px] text-slate-400">Oxirgi 14 kun tushumi</p>
            {metrics.incomeLast14Days.some((p) => p.value > 0) ? (
              <div className="h-32">
                <ResponsiveContainer width="100%" height="100%">
                  <AreaChart data={metrics.incomeLast14Days} margin={{ top: 6, right: 4, left: 4, bottom: 0 }}>
                    <defs>
                      <linearGradient id="incGrad" x1="0" y1="0" x2="0" y2="1">
                        <stop offset="0%" stopColor="#10b981" stopOpacity={0.35} />
                        <stop offset="100%" stopColor="#10b981" stopOpacity={0} />
                      </linearGradient>
                    </defs>
                    <XAxis dataKey="label" tick={{ fontSize: 9, fill: '#94a3b8' }}
                      tickLine={false} axisLine={false} interval={2} />
                    <Tooltip cursor={{ stroke: '#cbd5e1' }}
                      contentStyle={{ borderRadius: 10, border: '1px solid #e2e8f0', fontSize: 12 }}
                      formatter={(v) => [formatMoney(Number(v)), 'Tushum']} />
                    <Area type="monotone" dataKey="value" stroke="#10b981" strokeWidth={2}
                      fill="url(#incGrad)" />
                  </AreaChart>
                </ResponsiveContainer>
              </div>
            ) : (
              <div className="grid h-32 place-items-center text-xs text-slate-300">
                Oxirgi 14 kunda tushum yo'q
              </div>
            )}
          </div>
        </div>
        {ai.tushumTahlili && <Note text={ai.tushumTahlili} />}
      </section>

      {/* ====== Baholar dinamikasi: ustun diagramma + delta ====== */}
      <section>
        <SecTitle icon={Star} text="O'quvchilar baholari dinamikasi" />
        <div className="grid items-center gap-3 lg:grid-cols-2">
          <div className="h-28">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={[
                  { name: "O'tgan oy", v: metrics.avgGradePrevMonth },
                  { name: 'Shu oy', v: metrics.avgGradeThisMonth },
                ]}
                margin={{ top: 14, right: 8, left: 8, bottom: 0 }}
              >
                <XAxis dataKey="name" tick={{ fontSize: 11, fill: '#64748b' }} tickLine={false} axisLine={false} />
                <Tooltip cursor={{ fill: 'rgba(0,0,0,0.03)' }}
                  contentStyle={{ borderRadius: 10, border: '1px solid #e2e8f0', fontSize: 12 }}
                  formatter={(v) => [Number(v).toFixed(2), "O'rtacha baho"]} />
                <Bar dataKey="v" radius={[6, 6, 0, 0]} maxBarSize={56}>
                  <Cell fill="#cbd5e1" />
                  <Cell fill={gradeDelta >= 0 ? '#10b981' : '#ef4444'} />
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>
          <div className="flex flex-wrap items-center gap-3">
            <Stat label="Shu oy" value={metrics.avgGradeThisMonth.toFixed(2)} />
            <Stat label="O'tgan oy" value={metrics.avgGradePrevMonth.toFixed(2)} muted />
            <DeltaChip delta={gradeDelta} />
          </div>
        </div>
        {ai.baholarTahlili && <Note text={ai.baholarTahlili} />}
      </section>

      {/* ====== Lidlar manbasi (pie) + Ketish sabablari (bar) yonma-yon ====== */}
      <div className="grid gap-3 md:grid-cols-2">
        <div className="rounded-xl border border-slate-100 p-3">
          <SecTitle icon={UserPlus} text="Lidlar manbasi" />
          {metrics.leadsBySource.length > 0 ? (
            <PieBlock points={metrics.leadsBySource} />
          ) : (
            <p className="py-6 text-center text-xs text-slate-300">Shu oy lid yo'q</p>
          )}
          {ai.lidlar && <Note text={ai.lidlar} />}
        </div>
        <div className="rounded-xl border border-slate-100 p-3">
          <SecTitle icon={UserMinus} text="Ketish sabablari" />
          {metrics.departureReasons.length > 0 ? (
            <MiniBars points={metrics.departureReasons} color="bg-red-400" />
          ) : (
            <p className="py-6 text-center text-xs text-slate-300">Sabablar bo'yicha ma'lumot yo'q</p>
          )}
          {ai.ketganlar && <Note text={ai.ketganlar} />}
        </div>
      </div>

      {/* ====== Xavflar + Tavsiyalar ====== */}
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        {ai.xavflar.length > 0 && (
          <ListCard icon={AlertTriangle} title="E'tibor talab qiladi" items={ai.xavflar} tone="amber" />
        )}
        {ai.tavsiyalar.length > 0 && (
          <ListCard icon={Lightbulb} title="Tavsiyalar" items={ai.tavsiyalar} tone="emerald" />
        )}
      </div>
    </div>
  )
}

/* ---------- yordamchi komponentlar ---------- */

function HealthRing({ value }: { value: number }) {
  const color = value >= 70 ? '#10b981' : value >= 40 ? '#f59e0b' : '#ef4444'
  return (
    <div className="relative flex flex-col items-center justify-center rounded-xl border border-slate-100 bg-white p-2">
      <div className="relative h-[72px] w-[72px]">
        <ResponsiveContainer width="100%" height="100%">
          <RadialBarChart
            innerRadius="72%"
            outerRadius="100%"
            data={[{ value }]}
            startAngle={90}
            endAngle={-270}
          >
            <PolarAngleAxis type="number" domain={[0, 100]} tick={false} />
            <RadialBar background dataKey="value" cornerRadius={20} fill={color} />
          </RadialBarChart>
        </ResponsiveContainer>
        <div className="absolute inset-0 flex flex-col items-center justify-center">
          <span className="font-mono text-lg font-bold" style={{ color }}>{value}</span>
        </div>
      </div>
      <p className="mt-1 text-[11px] font-medium text-slate-500">Salomatlik</p>
    </div>
  )
}

const KPI_TONE: Record<string, { box: string; ic: string }> = {
  violet: { box: 'bg-violet-50', ic: 'text-violet-600' },
  emerald: { box: 'bg-emerald-50', ic: 'text-emerald-600' },
  blue: { box: 'bg-blue-50', ic: 'text-blue-600' },
  red: { box: 'bg-red-50', ic: 'text-red-600' },
}

function Kpi({
  icon: Icon,
  label,
  value,
  sub,
  color,
}: {
  icon: typeof Star
  label: string
  value: number
  sub?: string
  color: keyof typeof KPI_TONE
}) {
  const t = KPI_TONE[color]
  return (
    <div className="rounded-xl border border-slate-100 bg-white p-3">
      <div className={cn('mb-1.5 grid h-7 w-7 place-items-center rounded-lg', t.box)}>
        <Icon className={cn('h-4 w-4', t.ic)} />
      </div>
      <p className="font-mono text-xl font-bold text-slate-800">{value}</p>
      <p className="text-[11px] text-slate-500">{label}</p>
      {sub && <p className="text-[10px] text-slate-400">{sub}</p>}
    </div>
  )
}

function SecTitle({ icon: Icon, text }: { icon: typeof Star; text: string }) {
  return (
    <div className="mb-2.5 flex items-center gap-2">
      <Icon className="h-4 w-4 text-slate-400" />
      <h4 className="text-xs font-semibold uppercase tracking-wide text-slate-500">{text}</h4>
    </div>
  )
}

function Note({ text }: { text: string }) {
  return <p className="mt-2.5 text-sm leading-relaxed text-slate-600">{text}</p>
}

const ACCENT: Record<string, string> = {
  slate: 'text-slate-800',
  emerald: 'text-emerald-600',
  red: 'text-red-600',
  violet: 'text-violet-600',
}

function Money({ label, value, accent = 'slate' }: { label: string; value: number; accent?: string }) {
  return (
    <div className="rounded-xl border border-slate-100 bg-white p-2.5">
      <p className="text-[11px] text-slate-400">{label}</p>
      <p className={cn('mt-0.5 font-mono text-base font-bold', ACCENT[accent])} title={formatMoney(value)}>
        {shortSom(value)} <span className="text-[10px] font-normal text-slate-400">so'm</span>
      </p>
    </div>
  )
}

function Stat({ label, value, accent = 'slate', muted }: { label: string; value: number | string; accent?: string; muted?: boolean }) {
  return (
    <div className="rounded-lg bg-slate-50 px-3 py-1.5">
      <span className="text-[11px] text-slate-400">{label}: </span>
      <span className={cn('font-mono text-sm font-semibold', muted ? 'text-slate-400' : ACCENT[accent])}>
        {value}
      </span>
    </div>
  )
}

function PieBlock({ points }: { points: CenterPoint[] }) {
  const data = points.slice(0, 7)
  const total = data.reduce((s, p) => s + p.value, 0) || 1
  return (
    <div className="flex items-center gap-3">
      <div className="h-28 w-28 shrink-0">
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie data={data} dataKey="value" nameKey="label" cx="50%" cy="50%"
              innerRadius={28} outerRadius={52} paddingAngle={2}>
              {data.map((_, i) => (
                <Cell key={i} fill={PIE_COLORS[i % PIE_COLORS.length]} />
              ))}
            </Pie>
            <Tooltip contentStyle={{ borderRadius: 10, border: '1px solid #e2e8f0', fontSize: 12 }}
              formatter={(v, n) => [`${v} ta`, n as string]} />
          </PieChart>
        </ResponsiveContainer>
      </div>
      <ul className="min-w-0 flex-1 space-y-1">
        {data.map((p, i) => (
          <li key={p.label} className="flex items-center gap-1.5 text-xs">
            <span className="h-2.5 w-2.5 shrink-0 rounded-sm" style={{ background: PIE_COLORS[i % PIE_COLORS.length] }} />
            <span className="min-w-0 flex-1 truncate text-slate-600">{p.label}</span>
            <span className="font-mono font-medium text-slate-700">{p.value}</span>
            <span className="w-9 text-right text-[10px] text-slate-400">
              {Math.round((p.value / total) * 100)}%
            </span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function MiniBars({ points, color }: { points: CenterPoint[]; color: string }) {
  const max = Math.max(1, ...points.map((p) => p.value))
  return (
    <div className="space-y-1.5">
      {points.slice(0, 6).map((p) => (
        <div key={p.label} className="flex items-center gap-2">
          <span className="w-28 shrink-0 truncate text-[11px] text-slate-500">{p.label}</span>
          <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-slate-100">
            <div className={cn('h-full rounded-full', color)} style={{ width: `${(p.value / max) * 100}%` }} />
          </div>
          <span className="w-6 shrink-0 text-right font-mono text-[11px] font-medium text-slate-600">{p.value}</span>
        </div>
      ))}
    </div>
  )
}

function ListCard({
  icon: Icon,
  title,
  items,
  tone,
}: {
  icon: typeof Star
  title: string
  items: string[]
  tone: 'amber' | 'emerald'
}) {
  const ring = tone === 'amber' ? 'border-amber-100 bg-amber-50' : 'border-emerald-100 bg-emerald-50'
  const ic = tone === 'amber' ? 'text-amber-500' : 'text-emerald-500'
  return (
    <div className={cn('rounded-xl border p-3', ring)}>
      <div className="mb-2 flex items-center gap-1.5">
        <Icon className={cn('h-4 w-4', ic)} />
        <h4 className="text-xs font-semibold text-slate-700">{title}</h4>
      </div>
      <ul className="space-y-1.5">
        {items.map((t, i) => (
          <li key={i} className="flex gap-1.5 text-xs leading-relaxed text-slate-600">
            <span className={ic}>•</span>
            <span>{t}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}

function TrendChip({ trend }: { trend: string }) {
  const t = trend.toLowerCase()
  const up = t.includes('yaxshi')
  const down = t.includes('yomon')
  const Icon = up ? TrendingUp : down ? TrendingDown : Minus
  const cls = up
    ? 'bg-emerald-50 text-emerald-700'
    : down
      ? 'bg-red-50 text-red-700'
      : 'bg-slate-100 text-slate-600'
  const label = up ? 'Yaxshilanmoqda' : down ? 'Yomonlashmoqda' : 'Barqaror'
  return (
    <span className={cn('inline-flex items-center gap-1.5 rounded-lg px-2.5 py-1 text-xs font-medium', cls)}>
      <Icon className="h-3.5 w-3.5" />
      {label}
    </span>
  )
}

function DeltaChip({ delta }: { delta: number }) {
  if (Math.abs(delta) < 0.01)
    return (
      <span className="inline-flex items-center gap-1 rounded-lg bg-slate-100 px-2 py-1 text-xs text-slate-500">
        <Minus className="h-3 w-3" /> o'zgarmadi
      </span>
    )
  const up = delta > 0
  return (
    <span className={cn('inline-flex items-center gap-1 rounded-lg px-2 py-1 text-xs font-medium',
      up ? 'bg-emerald-50 text-emerald-700' : 'bg-red-50 text-red-700')}>
      {up ? <TrendingUp className="h-3 w-3" /> : <TrendingDown className="h-3 w-3" />}
      {up ? '+' : ''}{delta.toFixed(2)}
    </span>
  )
}
