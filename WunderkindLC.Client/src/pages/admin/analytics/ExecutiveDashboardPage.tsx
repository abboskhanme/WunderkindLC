import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ArrowRight, ChevronLeft, ChevronRight, HandCoins, Landmark, TrendingUp, UserPlus, Users, Wallet } from 'lucide-react'
import { Bar, BarChart, CartesianGrid, Legend, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { getExecutiveDashboard, type ExecutiveDashboard } from '@/api/services/executive'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import { formatMonth } from '@/config/constants'
import { conversionPct, deltaPct } from '@/lib/executive'
import { currentMonth, shiftMonth } from '@/lib/month'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'

/** Ranglar — `course-analytics.md` §6: kelgan `#0284c7`, ketgan `#e11d48`; yakka seriya `#6366f1`. */
const IN_COLOR = '#0284c7'
const OUT_COLOR = '#e11d48'
const ONE_COLOR = '#6366f1'

const axisTick = { fontSize: 12, fill: '#64748b' }
const short = (m: string) => formatMonth(m).split(' ')[0].slice(0, 3)

/**
 * RAHBARIYAT ANALITIKASI (SPEC-backlog-2026-10-17 F5 D1, TZ 3.4) — `/admin/analitika`, faqat superadmin (yuqori
 * paneldagi «Analitika»). Har ko'rsatkich o'z hisobotiga olib boradi; raqamlar mavjud yagona ta'riflardan (server
 * `ExecutiveDashboard`). Ikki o'lchov bitta grafikda ikki y-o'q bilan ko'rsatilmaydi.
 */
export function ExecutiveDashboardPage() {
  const cur = currentMonth()
  const [month, setMonth] = useState(cur)
  const [res, setRes] = useState<{ month: string; data: ExecutiveDashboard | null; error: string | null } | null>(null)

  useEffect(() => {
    let alive = true
    getExecutiveDashboard(month)
      .then((data) => alive && setRes({ month, data, error: null }))
      .catch((e) => alive && setRes({ month, data: null, error: apiErrorMessage(e, "Analitikani yuklab bo'lmadi") }))
    return () => {
      alive = false
    }
  }, [month])

  const data = res?.month === month ? res.data : null
  const error = res?.month === month ? res.error : null
  const last = data?.trend[data.trend.length - 1]
  const conv = last ? conversionPct(last.leads, last.leadsConverted) : null

  return (
    <div className="space-y-4">
      <PageHeader title="Analitika" sub="Rahbariyat uchun tezkor ko'rsatkichlar — har biri o'z hisobotiga olib boradi" />

      <div className="toolbar flex-wrap">
        <span className="text-sm font-medium text-slate-600">Oy:</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, -1))} aria-label="Oldingi oy">
          <ChevronLeft className="h-4 w-4" />
        </Button>
        <span className="min-w-28 text-center text-sm font-semibold text-slate-700">{formatMonth(month)}</span>
        <Button variant="secondary" onClick={() => setMonth((m) => shiftMonth(m, 1))} disabled={month >= cur} aria-label="Keyingi oy">
          <ChevronRight className="h-4 w-4" />
        </Button>
      </div>

      {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
      {!data ? (error ? null : <Loader />) : (
        <>
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Tile to="/admin/moliya/kirim-chiqim">
              <StatCard label="O'quv tushumi (oy)" value={formatMoney(data.thisMonth.tuition)} icon={HandCoins}
                delta={deltaPct(data.thisMonth.tuition, data.prevMonth.tuition)} hint="vozvrat ayrilgan" />
            </Tile>
            <Tile to="/admin/moliya/pnl">
              <StatCard label="Jami kirim − chiqim" value={formatMoney(data.thisMonth.net)} icon={TrendingUp}
                iconBg="bg-indigo-50" iconColor="text-indigo-600"
                hint={`Kirim ${formatMoney(data.thisMonth.income)} · chiqim ${formatMoney(data.thisMonth.expense)}`} />
            </Tile>
            <Tile to="/admin/hisobotlar/qarzdorlik">
              <StatCard label="Faol qarz (hozir)" value={formatMoney(data.debtTotal)} icon={Wallet}
                iconBg="bg-rose-50" iconColor="text-rose-600" hint={`${data.debtors} ta qarzdor o'quvchi`} />
            </Tile>
            <Tile to="/admin/crm-stats">
              <StatCard label="Lid konversiyasi (oy)" value={conv == null ? '—' : `${conv}%`} icon={UserPlus}
                iconBg="bg-sky-50" iconColor="text-sky-600"
                hint={last ? `${last.leads} ta lid → ${last.leadsConverted} ta o'quvchi` : undefined} />
            </Tile>
          </div>

          <div className="grid gap-4 lg:grid-cols-3">
            <Panel title="Kassalar qoldig'i (hozir)" to="/admin/finance?tab=cashiers" className="lg:col-span-1">
              <div className="mb-3 flex items-center gap-2">
                <Landmark className="h-5 w-5 text-brand-600" />
                <span className="text-xl font-bold tabular-nums text-slate-800">{formatMoney(data.cashTotal)}</span>
              </div>
              <div className="divide-y divide-slate-100">
                {data.cashboxes.map((b) => (
                  <div key={b.id} className="flex items-center justify-between gap-2 py-1.5 text-sm">
                    <span className="truncate text-slate-600">
                      {b.name}
                      {b.kind === 'bank' && <span className="ml-1 text-[10px] font-bold text-sky-600">BANK</span>}
                      {b.isArchived && <span className="ml-1 text-xs text-slate-400">(arxiv)</span>}
                    </span>
                    <span className={cn('font-mono tabular-nums', b.balance < 0 ? 'text-rose-600' : 'text-slate-800')}>{formatMoney(b.balance)}</span>
                  </div>
                ))}
              </div>
            </Panel>

            <Panel title="O'quvchilar oqimi (6 oy)" to="/admin/hisobotlar/ketish-sabablari" className="lg:col-span-2"
              sub={`Faol o'quvchilar (hozir): ${data.activeStudents}. Yangi — birinchi to'lovni qilganlar; ketgan — aktivdan ketganlar.`}>
              <ResponsiveContainer width="100%" height={240}>
                <BarChart data={data.trend.map((t) => ({ name: short(t.month), new: t.newPaying, left: t.leftActive }))} barGap={2}>
                  <CartesianGrid vertical={false} stroke="#eef2f7" />
                  <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                  <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={axisTick} width={32} />
                  <Tooltip />
                  <Legend />
                  <Bar dataKey="new" name="Yangi" fill={IN_COLOR} radius={[4, 4, 0, 0]} maxBarSize={18} />
                  <Bar dataKey="left" name="Ketgan" fill={OUT_COLOR} radius={[4, 4, 0, 0]} maxBarSize={18} />
                </BarChart>
              </ResponsiveContainer>
            </Panel>
          </div>

          <div className="grid gap-4 lg:grid-cols-2">
            <Panel title="O'quv tushumi (6 oy)" to="/admin/moliya/kirim-chiqim">
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={data.trend.map((t) => ({ name: short(t.month), v: t.tuition }))}>
                  <CartesianGrid vertical={false} stroke="#eef2f7" />
                  <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                  <YAxis tickLine={false} axisLine={false} tick={axisTick} width={64} tickFormatter={(v: number) => `${Math.round(v / 1_000_000)} mln`} />
                  <Tooltip formatter={(v) => formatMoney(Number(v))} />
                  <Bar dataKey="v" name="Tushum" fill={ONE_COLOR} radius={[4, 4, 0, 0]} maxBarSize={28} />
                </BarChart>
              </ResponsiveContainer>
            </Panel>
            <Panel title="Lidlar (6 oy)" to="/admin/crm-stats" sub="Shu oyda kelgan lidlar va ulardan hozir o'quvchiga aylanganlar">
              <ResponsiveContainer width="100%" height={220}>
                <BarChart data={data.trend.map((t) => ({ name: short(t.month), leads: t.leads, conv: t.leadsConverted }))} barGap={2}>
                  <CartesianGrid vertical={false} stroke="#eef2f7" />
                  <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                  <YAxis allowDecimals={false} tickLine={false} axisLine={false} tick={axisTick} width={32} />
                  <Tooltip />
                  <Legend />
                  <Bar dataKey="leads" name="Kelgan" fill={IN_COLOR} radius={[4, 4, 0, 0]} maxBarSize={18} />
                  <Bar dataKey="conv" name="O'quvchi bo'ldi" fill={ONE_COLOR} radius={[4, 4, 0, 0]} maxBarSize={18} />
                </BarChart>
              </ResponsiveContainer>
            </Panel>
          </div>
          <p className="flex items-center gap-1.5 text-xs text-slate-400">
            <Users className="h-3.5 w-3.5" /> Raqamlar 5 daqiqagacha keshlanadi; qarz — arxivlanmagan o'quvchilarning umumiy balansi.
          </p>
        </>
      )}
    </div>
  )
}

function Tile({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link to={to} className="block rounded-2xl transition-shadow hover:shadow-md">
      {children}
    </Link>
  )
}

function Panel({ title, to, sub, className, children }: { title: string; to: string; sub?: string; className?: string; children: React.ReactNode }) {
  return (
    <div className={cn('rounded-2xl border border-slate-200/80 bg-white p-4 shadow-sm', className)}>
      <div className="mb-3 flex items-start justify-between gap-2">
        <div>
          <h3 className="text-sm font-bold text-slate-700">{title}</h3>
          {sub && <p className="mt-0.5 text-xs text-slate-400">{sub}</p>}
        </div>
        <Link to={to} className="inline-flex shrink-0 items-center gap-1 text-xs font-semibold text-brand-600 hover:underline">
          Hisobot <ArrowRight className="h-3.5 w-3.5" />
        </Link>
      </div>
      {children}
    </div>
  )
}
