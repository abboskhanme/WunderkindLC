import { ChevronRight } from 'lucide-react'
import type { CareerFunnelStep } from '@/api/services/career'
import { Card } from '@/components/ui/Card'
import { cn } from '@/lib/utils'

interface Props {
  steps: CareerFunnelStep[]
}

/**
 * ISHGA QABUL VORONKASI — CV → Test → Demo dars → HR suhbati → Rahbar suhbati → Ishga qabul.
 * Har qadamda: shu bosqichga YETIB BORGANLAR soni, oldingi bosqichdan o'tish ulushi va shu
 * bosqichda to'xtab rad etilganlar ("qayerda yo'qotyapmiz"). Hisob serverda
 * (`CareerService.ComputeFunnel`) — bu yerda faqat chiziladi.
 */
export function CareerFunnel({ steps }: Props) {
  if (steps.length === 0) return null
  const total = steps[0]?.reached ?? 0

  return (
    <Card className="mb-4">
      <div className="mb-3 flex flex-wrap items-baseline justify-between gap-2">
        <h3 className="text-sm font-semibold text-slate-800">Ishga qabul voronkasi</h3>
        <span className="text-xs text-slate-400">
          Jami {total} ta ariza · son — shu bosqichga yetib borganlar
        </span>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 xl:grid-cols-6">
        {steps.map((s, i) => (
          <div key={s.key} className="relative rounded-xl border border-slate-200 bg-white p-3">
            {i > 0 && (
              <ChevronRight className="absolute -left-2.5 top-1/2 hidden h-4 w-4 -translate-y-1/2 text-slate-300 xl:block" />
            )}
            <p className="truncate text-xs font-semibold text-slate-500">
              {s.icon} {s.label}
            </p>
            <p className="mt-1 text-xl font-bold text-slate-800">{s.reached}</p>
            <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-slate-100">
              <div
                className="h-full rounded-full bg-brand-500"
                style={{ width: `${Math.min(100, Math.max(0, s.fromStartPct))}%` }}
              />
            </div>
            <p className="mt-1.5 text-[11px] text-slate-500">
              {i === 0 ? (
                <>jami arizalar</>
              ) : (
                <>
                  o'tish: <b className={cn(s.fromPrevPct < 50 ? 'text-amber-600' : 'text-slate-700')}>{s.fromPrevPct}%</b>
                </>
              )}
            </p>
            <p className="text-[11px] text-slate-400">
              hozir: {s.current}
              {s.rejectedHere > 0 && <> · rad: <span className="text-red-500">{s.rejectedHere}</span></>}
            </p>
          </div>
        ))}
      </div>
    </Card>
  )
}
