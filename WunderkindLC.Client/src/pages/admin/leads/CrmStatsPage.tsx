import { useCallback, useState } from 'react'
import {
  Bar,
  BarChart,
  CartesianGrid,
  Cell,
  Legend,
  Line,
  LineChart,
  Pie,
  PieChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from 'recharts'
import { AlertTriangle, Users, UserCheck, Percent, Wallet, HandCoins } from 'lucide-react'
import { getCrmStats, getLeadAnalytics } from '@/api/services/leads'
import { useAsync } from '@/hooks/useAsync'
import { Card } from '@/components/ui/Card'
import { Input } from '@/components/ui/Input'
import { PageHeader } from '@/components/ui/PageHeader'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { monthShortNames } from '@/config/constants'
import { todayIso } from '@/lib/month'
import { ConversionFunnel } from './stats/ConversionFunnel'
import { SourcesDonut } from './stats/SourcesDonut'
import { ManagerPerformance } from './stats/ManagerPerformance'
import { ManagerStageMatrix } from './stats/ManagerStageMatrix'
import { OriginTable } from '@/components/leads/OriginTable'
import { axisTick, barCursor, CATEGORICAL, gridStroke, stageRamp, tooltipStyle } from './stats/palette'


/**
 * Analitika xatosini foydalanuvchi tilida beradi. Backend endpointi hali tayyor bo'lmasligi
 * mumkin (404) — bu sahifani BUZMAYDI, faqat shu bo'lim o'rniga izoh ko'rsatiladi.
 */
function analyticsErrorMessage(err: unknown): string {
  const status = (err as { response?: { status?: number } } | null)?.response?.status
  if (status === 404) {
    return "Analitika hali serverda mavjud emas (endpoint topilmadi). Backend tayyor bo'lgach bu bo'lim o'zi ishlay boshlaydi."
  }
  return apiErrorMessage(err, "Analitikani yuklab bo'lmadi")
}

export function CrmStatsPage() {
  const { data, loading, error } = useAsync(getCrmStats, [])

  // Davr filtri — voronka / manbalar / menejerlar bloklarini qamraydi.
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')

  const fetchAnalytics = useCallback(
    () =>
      getLeadAnalytics(from || undefined, to || undefined).catch((err: unknown) => {
        throw new Error(analyticsErrorMessage(err))
      }),
    [from, to],
  )
  const { data: analytics, loading: aLoading, error: aError } = useAsync(fetchAnalytics, [fetchAnalytics])

  /**
   * Tez oraliq: BUGUNNI QO'SHIB, orqaga `days` kun. Ya'ni `quick(1)` = faqat bugun.
   *
   * ⚠️ Sana LOKAL (`todayIso`), `toISOString()` EMAS: u UTC beradi va Toshkent vaqtida
   * (UTC+5) yarim tundan keyin «Bugun» tugmasi KECHAGI kunni tanlab qo'yardi.
   */
  const quick = (days: number) => {
    const start = new Date()
    start.setDate(start.getDate() - days + 1)
    const p = (n: number) => String(n).padStart(2, '0')
    setFrom(`${start.getFullYear()}-${p(start.getMonth() + 1)}-${p(start.getDate())}`)
    setTo(todayIso())
  }

  /** Shu tez oraliq HOZIR tanlanganmi — segmentda qaysi biri faol ekani ko'rinib tursin. */
  const isQuick = (days: number) => {
    if (!from || to !== todayIso()) return false
    const start = new Date()
    start.setDate(start.getDate() - days + 1)
    const p = (n: number) => String(n).padStart(2, '0')
    return from === `${start.getFullYear()}-${p(start.getMonth() + 1)}-${p(start.getDate())}`
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (error) return <p className="text-red-600">Xatolik: {error}</p>
  if (!data) return null

  const sourceData = data.bySource.map((s) => ({ name: s.label, count: s.count }))
  const stageData = data.byStage.map((s) => ({ name: s.label, value: s.count }))
  // Bosqichlar ORDINAL — bitta tusning ochiqdan quyuqqa rampi (kategorial ranglar emas).
  const stageColors = stageRamp(stageData.length)
  // Qiziqish fanlari: jadval BARCHA fanlarni, diagramma esa eng ko'p 10 tasini ko'rsatadi.
  const interestRows = data.byInterest ?? []
  const topInterest = interestRows[0]
  const interestChart = interestRows.slice(0, 10).map((r) => ({
    name: r.label,
    Lidlar: r.count,
    Aylantirilgan: r.converted,
  }))
  const monthlyData = data.monthly.map((m) => ({
    name: `${monthShortNames[Number(m.month.slice(5, 7)) - 1] ?? m.month} '${m.month.slice(2, 4)}`,
    Yangi: m.created,
    Aylantirilgan: m.converted,
  }))

  // KPI: davr filtri ishlaganda analitikadan, aks holda butun davr statistikasidan.
  const kpi = analytics ?? { total: data.totalLeads, converted: data.converted, conversionRate: data.conversionRate }
  const kpiHint = analytics ? undefined : 'butun davr'

  return (
    <div className="space-y-6">
      <PageHeader
        title="CRM statistika"
        sub="Lidlar va konversiya bo'yicha umumiy ko'rsatkichlar"
      />

      {/* ---- Davr filtri: quyidagi voronka / manbalar / menejerlar bloklarini qamraydi ---- */}
      <Card tight>
        <div className="flex flex-wrap items-end gap-3 p-4">
          <Input
            label="Sanadan"
            type="date"
            className="w-auto"
            value={from}
            onChange={(e) => setFrom(e.target.value)}
          />
          <Input
            label="Sanagacha"
            type="date"
            className="w-auto"
            value={to}
            onChange={(e) => setTo(e.target.value)}
          />
          <div className="tabs">
            {/* «Bugun» — `quick(1)`: bugun ham QO'SHIB sanaladi, ya'ni from = to = bugun. */}
            {[
              { label: 'Bugun', days: 1 },
              { label: '7 kun', days: 7 },
              { label: '30 kun', days: 30 },
              { label: '90 kun', days: 90 },
            ].map((q) => (
              <button
                key={q.days}
                type="button"
                className={cn('tab', isQuick(q.days) && 'active')}
                onClick={() => quick(q.days)}
              >
                {q.label}
              </button>
            ))}
            <button
              type="button"
              className={cn('tab', !from && !to && 'active')}
              onClick={() => {
                setFrom('')
                setTo('')
              }}
            >
              Butun davr
            </button>
          </div>
        </div>
      </Card>

      {/*
        KPI: konversiya — sahifaning ASOSIY raqami, shuning uchun u yagona "hero" figura
        (katta, proporsional raqamlar bilan — tabular-nums FAQAT ustundagi raqamlarga).
        Qolgan ikkitasi unga bo'ysunadigan kichik plitkalar.
      */}
      <div className="grid gap-4 lg:grid-cols-3">
        <div className="flex flex-col justify-between gap-4 rounded-xl border border-slate-200 bg-white p-5 shadow-[var(--shadow-1)]">
          <div className="flex items-start justify-between">
            <span className="text-xs font-semibold uppercase tracking-wide text-slate-400">
              Konversiya
            </span>
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-amber-50 text-amber-600">
              <Percent className="h-[18px] w-[18px]" />
            </div>
          </div>
          <div>
            <div className="text-[52px] font-semibold leading-none tracking-tight text-slate-900">
              {kpi.conversionRate.toFixed(1)}
              <span className="ml-1 text-2xl font-medium text-slate-400">%</span>
            </div>
            <p className="mt-2 text-xs text-slate-500">
              {kpi.total.toLocaleString()} ta liddan {kpi.converted.toLocaleString()} tasi
              o'quvchiga aylandi{kpiHint ? ` · ${kpiHint}` : ''}
            </p>
          </div>
        </div>

        <div className="grid gap-4 sm:grid-cols-2 lg:col-span-2">
          <StatCard label="Jami lidlar" value={kpi.total.toLocaleString()} icon={Users} hint={kpiHint} />
          <StatCard
            label="Aylantirilgan"
            value={kpi.converted.toLocaleString()}
            icon={UserCheck}
            iconBg="bg-emerald-50"
            iconColor="text-emerald-600"
            hint={kpiHint}
          />
          {/* SOTUV: "o'quvchi bo'ldi" hali PUL degani emas — sotuv bo'limining haqiqiy o'lchovi
              aynan shu ikkitasi. Ular faqat davr analitikasi yuklanganda to'ladi. */}
          <StatCard
            label="To'lov qildi"
            value={(analytics?.paid ?? 0).toLocaleString()}
            icon={HandCoins}
            iconBg="bg-teal-50"
            iconColor="text-teal-600"
            hint={analytics ? `${analytics.payRate}% sotuv konversiyasi` : kpiHint}
          />
          <StatCard
            label="Tushum"
            value={analytics && analytics.revenue > 0 ? formatMoney(analytics.revenue) : '—'}
            icon={Wallet}
            iconBg="bg-teal-50"
            iconColor="text-teal-600"
            hint={
              analytics && analytics.paid > 0
                ? `O'rtacha chek ${formatMoney(analytics.revenue / analytics.paid)} so'm`
                : "Aylantirilganlardan hali to'lov yo'q"
            }
          />
        </div>
      </div>

      {/* ---- Davr bo'yicha analitika ---- */}
      {aError ? (
        <div className="flex items-start gap-2 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700">
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>
            {aError}
            <span className="mt-0.5 block text-xs text-red-600/80">
              Voronka, manbalar va menejerlar bloklari vaqtincha ko'rsatilmayapti. Quyidagi butun
              davr bo'yicha grafiklar ishlashda davom etadi.
            </span>
          </span>
        </div>
      ) : aLoading && !analytics ? (
        <Card>
          <Loader label="Analitika yuklanmoqda..." />
        </Card>
      ) : (
        analytics && (
          // Qayta yuklashda oldingi ko'rinish saqlanadi — skelet ham, sakrash ham yo'q.
          <div className={cn('space-y-6', aLoading && 'opacity-60 transition-opacity')}>
            <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
              <ConversionFunnel funnel={analytics.funnel} />
              <SourcesDonut sources={analytics.sources} />
            </div>
            <ManagerPerformance managers={analytics.managers} />
            {/* «Kim qaysi bosqichgacha olib bordi» — menejerlar jadvalidan KEYIN: avval
                natija (nechta aylantirdi), keyin "qayerda tiqilib qolyapti" tafsiloti. */}
            <ManagerStageMatrix managers={analytics.managers} funnel={analytics.funnel} />
            <Card
              title="Lidlar qayerdan keladi"
              sub="Kanal kesimi — qaysi yo'l ko'p lid beradi va qaysi biri haqiqatan SOTADI"
            >
              <OriginTable origins={analytics.origins} />
            </Card>
          </div>
        )
      )}

      {/* ---- Butun davr bo'yicha (davr filtriga bog'liq emas) ---- */}
      <div className="flex items-center gap-3 pt-2">
        <h2 className="text-sm font-semibold uppercase tracking-wide text-slate-400">
          Butun davr bo'yicha
        </h2>
        <div className="h-px flex-1 bg-slate-200" />
      </div>

      <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
        {/* Manba bo'yicha (bar) — bitta seriya, shuning uchun legend kerak emas */}
        <Card>
          <h2 className="mb-4 font-semibold text-slate-800">Manba bo'yicha lidlar</h2>
          {sourceData.length === 0 ? (
            <p className="py-12 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
          ) : (
            <ResponsiveContainer width="100%" height={300}>
              <BarChart data={sourceData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                <CartesianGrid vertical={false} stroke={gridStroke} />
                <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
                <Tooltip cursor={barCursor} contentStyle={tooltipStyle} />
                <Bar dataKey="count" name="Lidlar" fill={CATEGORICAL[0]} radius={[4, 4, 0, 0]} maxBarSize={24} />
              </BarChart>
            </ResponsiveContainer>
          )}
        </Card>

        {/* Bosqich bo'yicha (pie) */}
        <Card>
          <h2 className="mb-4 font-semibold text-slate-800">Bosqich bo'yicha lidlar</h2>
          {stageData.length === 0 ? (
            <p className="py-12 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
          ) : (
            <ResponsiveContainer width="100%" height={300}>
              <PieChart>
                <Pie
                  data={stageData}
                  dataKey="value"
                  nameKey="name"
                  cx="50%"
                  cy="50%"
                  outerRadius={100}
                  stroke="#ffffff"
                  strokeWidth={2}
                  label={(entry: { name?: string; value?: number }) =>
                    `${entry.name ?? ''}: ${entry.value ?? 0}`
                  }
                >
                  {stageData.map((s, i) => (
                    <Cell key={s.name} fill={stageColors[i]} />
                  ))}
                </Pie>
                <Tooltip contentStyle={tooltipStyle} />
                <Legend wrapperStyle={{ fontSize: 13 }} />
              </PieChart>
            </ResponsiveContainer>
          )}
        </Card>
      </div>

      {/* Qiziqish fanlari (kurslar) bo'yicha — gorizontal bar + to'liq jadval */}
      <Card>
        <div className="mb-4 flex flex-wrap items-baseline justify-between gap-2">
          <h2 className="font-semibold text-slate-800">Qiziqish fanlari bo'yicha lidlar</h2>
          {topInterest && (
            <p className="text-sm text-slate-400">
              Eng ko'p qiziqish:{' '}
              <span className="font-medium text-slate-600">{topInterest.label}</span> — {topInterest.count} ta lid
            </p>
          )}
        </div>

        {interestRows.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
        ) : (
          <>
            <ResponsiveContainer width="100%" height={Math.max(200, interestChart.length * 46 + 40)}>
              <BarChart
                data={interestChart}
                layout="vertical"
                margin={{ top: 4, right: 24, left: 0, bottom: 0 }}
                barGap={2}
              >
                <CartesianGrid horizontal={false} stroke={gridStroke} />
                <XAxis type="number" tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
                <YAxis
                  type="category"
                  dataKey="name"
                  width={150}
                  tickLine={false}
                  axisLine={false}
                  tick={axisTick}
                />
                <Tooltip cursor={barCursor} contentStyle={tooltipStyle} />
                <Legend wrapperStyle={{ fontSize: 13 }} />
                <Bar dataKey="Lidlar" fill={CATEGORICAL[0]} radius={[0, 4, 4, 0]} maxBarSize={14} />
                <Bar dataKey="Aylantirilgan" fill={CATEGORICAL[1]} radius={[0, 4, 4, 0]} maxBarSize={14} />
              </BarChart>
            </ResponsiveContainer>

            {interestRows.length > interestChart.length && (
              <p className="mt-1 text-xs text-slate-400">
                Diagrammada eng ko'p {interestChart.length} ta fan — qolgani jadvalda.
              </p>
            )}

            <div className="mt-4 overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="border-b border-slate-100 text-xs uppercase tracking-wide text-slate-400">
                  <tr>
                    <th className="py-2 pr-3 font-medium">Fan (kurs)</th>
                    <th className="py-2 pr-3 text-right font-medium">Lidlar</th>
                    <th className="py-2 pr-3 text-right font-medium">Aylantirilgan</th>
                    <th className="py-2 text-right font-medium">Konversiya</th>
                  </tr>
                </thead>
                <tbody>
                  {interestRows.map((r) => (
                    <tr key={r.label} className="border-b border-slate-50 last:border-0">
                      <td className="py-2 pr-3 text-slate-700">{r.label}</td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-slate-700">{r.count}</td>
                      <td className="py-2 pr-3 text-right font-mono tabular-nums text-emerald-600">{r.converted}</td>
                      <td className="py-2 text-right font-mono tabular-nums text-slate-500">
                        {r.conversionRate.toFixed(1)}%
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </>
        )}
      </Card>

      {/* Oylik dinamika (line) */}
      <Card>
        <h2 className="mb-4 font-semibold text-slate-800">Oylik dinamika</h2>
        {monthlyData.length === 0 ? (
          <p className="py-12 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
        ) : (
          <ResponsiveContainer width="100%" height={320}>
            <LineChart data={monthlyData} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
              <CartesianGrid vertical={false} stroke={gridStroke} />
              <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
              <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} />
              <Tooltip contentStyle={tooltipStyle} />
              <Legend wrapperStyle={{ fontSize: 13 }} />
              <Line type="monotone" dataKey="Yangi" stroke={CATEGORICAL[0]} strokeWidth={2} dot={{ r: 4 }} />
              <Line
                type="monotone"
                dataKey="Aylantirilgan"
                stroke={CATEGORICAL[1]}
                strokeWidth={2}
                dot={{ r: 4 }}
              />
            </LineChart>
          </ResponsiveContainer>
        )}
      </Card>
    </div>
  )
}
