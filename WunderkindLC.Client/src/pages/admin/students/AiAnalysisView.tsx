import {
  PolarAngleAxis, PolarGrid, PolarRadiusAxis, Radar, RadarChart, ResponsiveContainer,
} from 'recharts'
import {
  TrendingUp, TrendingDown, Minus, CheckCircle2, AlertTriangle, Lightbulb, History, GitCompare,
} from 'lucide-react'
import type { StudentAiAnalysisRecord } from '@/api/services/students'
import { formatDate } from '@/lib/utils'

const dimLabels: { key: keyof StudentAiAnalysisRecord['result']['baholar']; label: string }[] = [
  { key: 'akademik', label: 'Akademik' },
  { key: 'davomat', label: 'Davomat' },
  { key: 'intizom', label: 'Intizom' },
  { key: 'uyVazifa', label: 'Uy vazifa' },
  { key: 'faollik', label: 'Faollik' },
]

function scoreColor(v: number): string {
  if (v >= 80) return '#16a34a'
  if (v >= 60) return '#2563eb'
  if (v >= 40) return '#f59e0b'
  return '#dc2626'
}

function trendInfo(trend: string): { label: string; cls: string; Icon: typeof TrendingUp } {
  const t = (trend || '').toLowerCase()
  if (t.includes('yaxshi')) return { label: 'Yaxshilanmoqda', cls: 'bg-emerald-50 text-emerald-700', Icon: TrendingUp }
  if (t.includes('yomon')) return { label: 'Yomonlashmoqda', cls: 'bg-red-50 text-red-700', Icon: TrendingDown }
  return { label: 'Barqaror', cls: 'bg-slate-100 text-slate-600', Icon: Minus }
}

/** Umumiy ball halqasi (SVG ring). */
function ScoreRing({ value }: { value: number }) {
  const r = 46
  const c = 2 * Math.PI * r
  const pct = Math.max(0, Math.min(100, value))
  const dash = (pct / 100) * c
  const color = scoreColor(pct)
  return (
    <div className="relative h-32 w-32 shrink-0">
      <svg viewBox="0 0 110 110" className="h-full w-full -rotate-90">
        <circle cx="55" cy="55" r={r} fill="none" stroke="#eef0f4" strokeWidth="9" />
        <circle
          cx="55" cy="55" r={r} fill="none" stroke={color} strokeWidth="9" strokeLinecap="round"
          strokeDasharray={`${dash} ${c}`}
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="font-mono text-3xl font-bold" style={{ color }}>{pct}</span>
        <span className="text-[11px] text-slate-400">/ 100</span>
      </div>
    </div>
  )
}

/**
 * Bitta AI tahlil natijasini interaktiv ko'rsatadi: umumiy ball halqasi + sohaviy radar diagramma +
 * matn bo'limlari (umumiy holat, dinamika, o'zgarishlar, kuchli/zaif tomonlar, tavsiyalar).
 */
export function AiAnalysisView({ record }: { record: StudentAiAnalysisRecord }) {
  const { result } = record
  const b = result.baholar
  const radarData = dimLabels.map((d) => ({ subject: d.label, value: b[d.key] ?? 0 }))
  const tr = trendInfo(result.trend)

  return (
    <div className="space-y-5">
      {/* Meta */}
      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-slate-400">
        <span className="inline-flex items-center gap-1">
          <History className="h-3.5 w-3.5" /> {formatDate(record.date)}
        </span>
        <span>·</span>
        <span className="font-mono">{record.model}</span>
      </div>

      {/* Ball + radar */}
      <div className="grid items-center gap-4 rounded-2xl border border-slate-100 bg-slate-50/60 p-4 sm:grid-cols-2">
        <div className="flex items-center gap-4">
          <ScoreRing value={b.umumiy} />
          <div className="space-y-2">
            <p className="text-sm font-medium text-slate-500">Umumiy baho</p>
            <span className={`inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm font-semibold ${tr.cls}`}>
              <tr.Icon className="h-4 w-4" /> {tr.label}
            </span>
          </div>
        </div>
        <div className="h-52 w-full">
          <ResponsiveContainer width="100%" height="100%">
            <RadarChart data={radarData} outerRadius="72%">
              <PolarGrid stroke="#e2e8f0" />
              <PolarAngleAxis dataKey="subject" tick={{ fontSize: 11, fill: '#64748b' }} />
              <PolarRadiusAxis domain={[0, 100]} tick={{ fontSize: 9, fill: '#cbd5e1' }} angle={90} />
              <Radar dataKey="value" stroke="#6d28d9" fill="#7c3aed" fillOpacity={0.35} />
            </RadarChart>
          </ResponsiveContainer>
        </div>
      </div>

      {/* Sohaviy ballar — mini barlar */}
      <div className="grid grid-cols-2 gap-2.5 sm:grid-cols-5">
        {dimLabels.map((d) => {
          const v = b[d.key] ?? 0
          return (
            <div key={d.key} className="rounded-xl border border-slate-100 p-2.5 text-center">
              <p className="font-mono text-lg font-bold" style={{ color: scoreColor(v) }}>{v}</p>
              <p className="text-[11px] text-slate-500">{d.label}</p>
              <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-slate-100">
                <div className="h-full rounded-full" style={{ width: `${v}%`, background: scoreColor(v) }} />
              </div>
            </div>
          )
        })}
      </div>

      {/* Umumiy holat */}
      {result.umumiy && (
        <Block title="Umumiy holat">
          <p className="text-sm leading-relaxed text-slate-700">{result.umumiy}</p>
        </Block>
      )}

      {/* O'zgarishlar (oldingi tahlilga nisbatan) */}
      {result.ozgarishlar && (
        <div className="rounded-xl border border-brand-100 bg-brand-50/60 p-4">
          <p className="mb-1.5 flex items-center gap-1.5 text-sm font-semibold text-brand-800">
            <GitCompare className="h-4 w-4" /> Oldingi tahlilga nisbatan o'zgarishlar
          </p>
          <p className="text-sm leading-relaxed text-slate-700">{result.ozgarishlar}</p>
        </div>
      )}

      {/* Dinamika */}
      {result.dinamika && (
        <Block title="O'qishdagi dinamika">
          <p className="text-sm leading-relaxed text-slate-700">{result.dinamika}</p>
        </Block>
      )}

      {/* Kuchli / Zaif */}
      <div className="grid gap-4 md:grid-cols-2">
        {result.kuchli.length > 0 && (
          <CardList
            title="Kuchli tomonlari" Icon={CheckCircle2} tone="green" items={result.kuchli}
          />
        )}
        {result.zaif.length > 0 && (
          <CardList
            title="Zaif tomonlari" Icon={AlertTriangle} tone="amber" items={result.zaif}
          />
        )}
      </div>

      {/* Tavsiyalar */}
      {result.tavsiyalar.length > 0 && (
        <CardList title="Tavsiyalar" Icon={Lightbulb} tone="blue" items={result.tavsiyalar} />
      )}
    </div>
  )
}

function Block({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div>
      <p className="mb-1.5 text-sm font-semibold text-slate-800">{title}</p>
      {children}
    </div>
  )
}

const tones: Record<string, { box: string; chip: string }> = {
  green: { box: 'border-emerald-100 bg-emerald-50/50', chip: 'text-emerald-600' },
  amber: { box: 'border-amber-100 bg-amber-50/50', chip: 'text-amber-600' },
  blue: { box: 'border-blue-100 bg-blue-50/50', chip: 'text-blue-600' },
}

function CardList({
  title, Icon, tone, items,
}: {
  title: string
  Icon: typeof CheckCircle2
  tone: 'green' | 'amber' | 'blue'
  items: string[]
}) {
  const t = tones[tone]
  return (
    <div className={`rounded-xl border p-4 ${t.box}`}>
      <p className="mb-2 flex items-center gap-1.5 text-sm font-semibold text-slate-800">
        <Icon className={`h-4 w-4 ${t.chip}`} /> {title}
      </p>
      <ul className="space-y-1.5">
        {items.map((it, i) => (
          <li key={i} className="flex gap-2 text-sm leading-relaxed text-slate-700">
            <span className={`mt-1.5 h-1.5 w-1.5 shrink-0 rounded-full ${t.chip.replace('text-', 'bg-')}`} />
            <span>{it}</span>
          </li>
        ))}
      </ul>
    </div>
  )
}
