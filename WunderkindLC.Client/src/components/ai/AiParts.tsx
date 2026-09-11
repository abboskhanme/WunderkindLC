import type { ReactNode } from 'react'
import { AlertCircle, CheckCircle2 } from 'lucide-react'
import {
  PolarAngleAxis,
  PolarGrid,
  PolarRadiusAxis,
  Radar,
  RadarChart,
  ResponsiveContainer,
} from 'recharts'
import { cn } from '@/lib/utils'
import { scoreColor } from '@/lib/ai'

/**
 * AI tahlil bo'limlarining UMUMIY qismlari — o'quvchi/o'qituvchi/guruh tahlillari bir xil
 * ko'rinishda bo'lishi uchun (ball halqasi, radar, foiz qatorlari, ro'yxat kartalari).
 */

/** Umumiy ball halqasi (SVG ring). */
export function ScoreRing({ value }: { value: number }) {
  const r = 46
  const c = 2 * Math.PI * r
  const pct = Math.max(0, Math.min(100, value))
  const color = scoreColor(pct)
  return (
    <div className="relative h-32 w-32 shrink-0">
      <svg viewBox="0 0 110 110" className="h-full w-full -rotate-90">
        <circle cx="55" cy="55" r={r} fill="none" stroke="#eef0f4" strokeWidth="9" />
        <circle
          cx="55" cy="55" r={r} fill="none" stroke={color} strokeWidth="9" strokeLinecap="round"
          strokeDasharray={`${(pct / 100) * c} ${c}`}
        />
      </svg>
      <div className="absolute inset-0 flex flex-col items-center justify-center">
        <span className="font-mono text-3xl font-bold" style={{ color }}>{pct}</span>
        <span className="text-[11px] text-slate-400">/ 100</span>
      </div>
    </div>
  )
}

/** Sohaviy baholar radar diagrammasi. */
export function AiRadar({ data }: { data: { subject: string; value: number }[] }) {
  return (
    <div className="h-52 w-full">
      <ResponsiveContainer width="100%" height="100%">
        <RadarChart data={data} outerRadius="70%">
          <PolarGrid stroke="#e2e8f0" />
          <PolarAngleAxis dataKey="subject" tick={{ fontSize: 10, fill: '#64748b' }} />
          <PolarRadiusAxis domain={[0, 100]} tick={{ fontSize: 9, fill: '#cbd5e1' }} angle={90} />
          <Radar dataKey="value" stroke="#6d28d9" fill="#7c3aed" fillOpacity={0.35} />
        </RadarChart>
      </ResponsiveContainer>
    </div>
  )
}

/** Sohaviy ballar — mini kartalar qatori. */
export function ScoreGrid({ items }: { items: { label: string; value: number }[] }) {
  return (
    <div className={cn('grid grid-cols-2 gap-2.5', items.length > 5 ? 'sm:grid-cols-6' : 'sm:grid-cols-5')}>
      {items.map((it) => (
        <div key={it.label} className="rounded-xl border border-slate-100 p-2.5 text-center">
          <p className="font-mono text-lg font-bold" style={{ color: scoreColor(it.value) }}>{it.value}</p>
          <p className="text-[11px] text-slate-500">{it.label}</p>
          <div className="mt-1.5 h-1.5 overflow-hidden rounded-full bg-slate-100">
            <div className="h-full rounded-full" style={{ width: `${it.value}%`, background: scoreColor(it.value) }} />
          </div>
        </div>
      ))}
    </div>
  )
}

/** Foizli qator (ko'rsatkich nomi + progress + qiymat). */
export function PctRow({ label, value, hint }: { label: string; value: number; hint?: string }) {
  const color = value >= 80 ? 'bg-emerald-500' : value >= 50 ? 'bg-amber-400' : 'bg-red-500'
  return (
    <div className="flex items-center gap-3 py-2">
      <div className="w-44 shrink-0">
        <p className="text-sm text-slate-600">{label}</p>
        {hint && <p className="text-[11px] text-slate-400">{hint}</p>}
      </div>
      <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-slate-100">
        <div className={cn('h-full rounded-full transition-all', color)} style={{ width: `${Math.min(100, Math.max(0, value))}%` }} />
      </div>
      <span className="w-12 text-right font-mono text-sm font-semibold text-slate-700">{value}%</span>
    </div>
  )
}

/** Reytingli ro'yxat (sabablar taqsimoti kabi) — eng kattasiga nisbatan bar. */
export function RankedBars({
  items, empty, barClass = 'bg-red-400',
}: {
  items: { label: string; value: number }[]
  empty: string
  barClass?: string
}) {
  if (items.length === 0) return <div className="py-10 text-center text-sm text-slate-400">{empty}</div>
  const max = Math.max(1, ...items.map((i) => i.value))
  return (
    <ul className="space-y-2.5">
      {items.map((r) => (
        <li key={r.label} className="flex items-center gap-3">
          <span className="w-44 shrink-0 truncate text-sm text-slate-600" title={r.label}>{r.label}</span>
          <div className="h-2.5 flex-1 overflow-hidden rounded-full bg-slate-100">
            <div className={cn('h-full rounded-full', barClass)} style={{ width: `${(r.value / max) * 100}%` }} />
          </div>
          <span className="w-8 text-right font-mono text-sm font-semibold text-slate-700">{r.value}</span>
        </li>
      ))}
    </ul>
  )
}

const tones: Record<string, { box: string; chip: string }> = {
  green: { box: 'border-emerald-100 bg-emerald-50/50', chip: 'text-emerald-600' },
  amber: { box: 'border-amber-100 bg-amber-50/50', chip: 'text-amber-600' },
  red: { box: 'border-red-100 bg-red-50/50', chip: 'text-red-600' },
  blue: { box: 'border-blue-100 bg-blue-50/50', chip: 'text-blue-600' },
}

/** AI ro'yxati (kuchli/zaif/xavflar/tavsiyalar). */
export function CardList({
  title, Icon, tone, items,
}: {
  title: string
  Icon: typeof CheckCircle2
  tone: 'green' | 'amber' | 'red' | 'blue'
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

/** Sarlavhali matn bloki (bo'sh matn — umuman ko'rinmaydi). */
export function TextBlock({ title, text }: { title: string; text: string }) {
  if (!text) return null
  return (
    <div>
      <p className="mb-1.5 text-sm font-semibold text-slate-800">{title}</p>
      <p className="text-sm leading-relaxed text-slate-700">{text}</p>
    </div>
  )
}

/** Kichik ko'rsatkich katagi. */
export function MiniStat({ label, value, hint }: { label: string; value: ReactNode; hint?: string }) {
  return (
    <div className="rounded-xl border border-slate-100 p-3">
      <p className="text-[11px] uppercase tracking-wide text-slate-400">{label}</p>
      <p className="mt-1 font-mono text-xl font-semibold text-slate-800">{value}</p>
      {hint && <p className="mt-0.5 text-[11px] text-slate-400">{hint}</p>}
    </div>
  )
}

/** AI xatosi banneri. */
export function AiErrorBox({ message }: { message: string }) {
  return (
    <div className="flex items-start gap-2 rounded-lg border border-red-100 bg-red-50 px-3 py-2.5 text-sm text-red-700">
      <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
      <div>
        <p className="font-semibold">Tahlil amalga oshmadi</p>
        <p className="mt-0.5 text-red-600">{message}</p>
      </div>
    </div>
  )
}
