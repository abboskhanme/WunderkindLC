import { useMemo, useState } from 'react'
import { IconCalendar, IconChartBar } from '@tabler/icons-react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { getTransactions } from '@/api/services/finance'
import { Loader } from '@/components/ui/Loader'
import { TintedIconButton } from '@/components/ui/list/TintedIconButton'
import { useAsync } from '@/hooks/useAsync'
import { cn, formatMoney } from '@/lib/utils'

/** Dushanbadan boshlanadigan hafta — edutizim kalendari shunday. */
const WEEK = ['Dush', 'Sesh', 'Chor', 'Pay', 'Jum', 'Shan', 'Yak']

function monthDays(year: number, month: number): (string | null)[] {
  const first = new Date(year, month - 1, 1)
  const days = new Date(year, month, 0).getDate()
  // getDay(): 0 = yakshanba → dushanbadan boshlanadigan indeksga o'giramiz.
  const lead = (first.getDay() + 6) % 7
  const cells: (string | null)[] = Array(lead).fill(null)
  for (let d = 1; d <= days; d++) {
    cells.push(`${year}-${String(month).padStart(2, '0')}-${String(d).padStart(2, '0')}`)
  }
  while (cells.length % 7 !== 0) cells.push(null)
  return cells
}

/**
 * Moliya → «Moliya analitikasi» (edutizim `/analytics/financial-analytics`): chapda markazning
 * umumiy qoldig'i, o'ngda KALENDAR — har kunning kirim/chiqim farqi va o'sha kun oxiridagi
 * yugurib boruvchi qoldiq.
 *
 * ⚠️ Filiallar ro'yxati edutizimdagidek EMAS: bizda bitta markaz (docs/ASSUMPTIONS.md) — chapda
 * markazning o'zi ko'rsatiladi.
 */
export function FinanceAnalyticsPage() {
  const now = new Date()
  const [year, setYear] = useState(now.getFullYear())
  const [month, setMonth] = useState(now.getMonth() + 1)
  const [view, setView] = useState<'calendar' | 'chart'>('calendar')

  // Butun tarix — yugurib boruvchi qoldiq uchun (oy boshigacha bo'lgan sof ham kerak).
  const { data, loading, error } = useAsync(() => getTransactions({}), [])

  const { byDay, opening, total } = useMemo(() => {
    const map = new Map<string, number>()
    let before = 0
    let all = 0
    const start = `${year}-${String(month).padStart(2, '0')}-01`
    const end = `${year}-${String(month).padStart(2, '0')}-31`
    for (const t of data ?? []) {
      const day = (t.date ?? '').slice(0, 10)
      if (!day) continue
      const signed = t.direction === 'expense' ? -t.amount : t.amount
      all += signed
      if (day < start) before += signed
      else if (day <= end) map.set(day, (map.get(day) ?? 0) + signed)
    }
    return { byDay: map, opening: before, total: all }
  }, [data, year, month])

  const cells = monthDays(year, month)
  // Har kun oxiridagi qoldiq — oy boshidagi qoldiqdan boshlab yig'iladi.
  const running = new Map<string, number>()
  let acc = opening
  for (const c of cells) {
    if (!c) continue
    acc += byDay.get(c) ?? 0
    running.set(c, acc)
  }

  const chart = cells
    .filter((c): c is string => !!c)
    .map((c) => ({ day: c.slice(8), delta: byDay.get(c) ?? 0, balance: running.get(c) ?? 0 }))

  if (loading && !data) return <Loader className="min-h-[240px]" />
  if (error) return <p className="text-[13px] text-red-600">{error}</p>

  const todayIso = new Date().toISOString().slice(0, 10)

  return (
    <div className="grid gap-2.5 lg:grid-cols-[300px_1fr]">
      <div className="space-y-2.5">
        <div className="rounded-xl bg-gradient-to-br from-[#5b6cff] to-[#8b5cf6] p-4 text-white shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
          <p className="text-[13px] opacity-90">Umumiy qoldiq</p>
          <p className="mt-1 text-[22px] font-bold">{formatMoney(total)}</p>
        </div>
        <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
          <p className="mb-2 text-[13px] font-semibold text-[#6b7280]">Tanlangan oy</p>
          <p className="text-[13px] text-[#333]">
            Oy boshidagi qoldiq: <b>{formatMoney(opening)}</b>
          </p>
          <p className="mt-1 text-[13px] text-[#333]">
            Oy oxiridagi qoldiq: <b>{formatMoney(acc)}</b>
          </p>
        </div>
      </div>

      <div className="rounded-xl border border-[#dbe0e6] bg-white p-4 shadow-[0_1px_2px_rgba(0,0,0,0.05)]">
        <div className="mb-3 flex flex-wrap items-center justify-between gap-2">
          <p className="text-[15px] font-semibold text-black">Kalendar</p>
          <div className="flex items-center gap-2">
            <select
              value={year}
              onChange={(e) => setYear(Number(e.target.value))}
              className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] outline-none focus:border-brand-600"
              aria-label="Yil"
            >
              {Array.from({ length: 5 }, (_, i) => now.getFullYear() - i).map((y) => (
                <option key={y} value={y}>
                  {y}
                </option>
              ))}
            </select>
            <select
              value={month}
              onChange={(e) => setMonth(Number(e.target.value))}
              className="h-[34px] rounded-lg border border-black/25 bg-white px-3 text-[13px] outline-none focus:border-brand-600"
              aria-label="Oy"
            >
              {Array.from({ length: 12 }, (_, i) => i + 1).map((m) => (
                <option key={m} value={m}>
                  {String(m).padStart(2, '0')}
                </option>
              ))}
            </select>
            <TintedIconButton label="Kalendar" pressed={view === 'calendar'} onClick={() => setView('calendar')}>
              <IconCalendar className="h-5 w-5" />
            </TintedIconButton>
            <TintedIconButton label="Grafik" pressed={view === 'chart'} onClick={() => setView('chart')}>
              <IconChartBar className="h-5 w-5" />
            </TintedIconButton>
          </div>
        </div>

        {view === 'calendar' ? (
          <div className="grid grid-cols-7 gap-px overflow-hidden rounded-lg bg-[#e5e7eb]">
            {WEEK.map((w) => (
              <div key={w} className="bg-[#f9fafb] py-2 text-center text-[12px] font-semibold text-[#6b7280]">
                {w}
              </div>
            ))}
            {cells.map((c, i) => {
              const delta = c ? (byDay.get(c) ?? 0) : 0
              return (
                <div key={c ?? `e-${i}`} className={cn('min-h-[92px] bg-white p-2', !c && 'bg-[#fafafa]')}>
                  {c && (
                    <>
                      <div className="flex items-center justify-between">
                        <span
                          className={cn(
                            'text-[13px] font-semibold text-[#333]',
                            c === todayIso && 'flex h-6 w-6 items-center justify-center rounded-full bg-brand-600 text-white',
                          )}
                        >
                          {Number(c.slice(8))}
                        </span>
                        {delta !== 0 && (
                          <span className={cn('text-[12px] font-semibold', delta > 0 ? 'text-[#2e7d32]' : 'text-[#de4141]')}>
                            {delta > 0 ? '+' : ''}
                            {formatMoney(delta)}
                          </span>
                        )}
                      </div>
                      <p className="mt-6 text-right text-[11px] text-[#9ca3af]">{formatMoney(running.get(c) ?? 0)}</p>
                    </>
                  )}
                </div>
              )
            })}
          </div>
        ) : (
          <ResponsiveContainer width="100%" height={340}>
            <BarChart data={chart} margin={{ top: 5, right: 10, left: 0, bottom: 0 }}>
              <CartesianGrid stroke="#eef0f3" vertical={false} />
              <XAxis dataKey="day" tick={{ fontSize: 11, fill: '#6b7280' }} />
              <YAxis tick={{ fontSize: 11, fill: '#6b7280' }} width={70} tickFormatter={(v: number) => (v / 1000).toFixed(0) + 'k'} />
              <Tooltip formatter={(v) => formatMoney(Number(v ?? 0))} />
              <Bar dataKey="delta" name="Kunlik farq" fill="#3D68FF" radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        )}
      </div>
    </div>
  )
}
