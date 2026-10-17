import type { CSSProperties, ReactElement, ReactNode } from 'react'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { Award, CalendarCheck, Info, Trophy, TrendingDown, TrendingUp, Users } from 'lucide-react'
import type { StudentRatingBlock as BlockData } from '@/api/services/rating'
import {
  fmtScore,
  fmtSolved,
  rankDelta,
  scoreFormulaText,
  shortMonth,
  monthLabel,
  testKindLabel,
  type RatingPeriodValue,
} from '@/lib/ratingPeriods'
import { RatingPeriodSwitch } from './RatingPeriodSwitch'

/**
 * O'QUVCHI REYTING BLOKI — "boy, lekin tartibli" (docs/SPEC-crm-upgrade.md P4.4):
 *   1) davr tanlovi; 2) uchta plitka (markazdagi o'rin + o'zgarish, guruhdagi o'rin, reyting bali);
 *   3) 12 oylik IKKI kichik grafik (o'rin — teskari o'q, ball) — hech qachon ikki y-o'qli bitta grafik
 *   emas (`course-analytics.md` §6), yakka rang #6366f1; 4) «Testlarim»; 5) davomat qatori.
 *
 * Faqat CHIZADI — hamma raqam serverda hisoblanadi. Admin profilida ham, o'quvchi/ota-ona kabinetida ham
 * shu komponent (`variant`); ranglar umumiy CSS o'zgaruvchilaridan — kabinetning tungi rejimi ishlaydi.
 */

const RATING_LINE = '#6366f1'
const UP = '#0284c7'   // ko'tarildi — sky (yashil/qizil emas: deuteranopiyada ajralmaydi)
const DOWN = '#e11d48' // tushdi — rose

const C = {
  text: 'var(--text, var(--fg, #1f2937))',
  muted: 'var(--muted, #6b7488)',
  surface: 'var(--surface, #fff)',
  surface2: 'var(--surface2, var(--surface-2, #fafbfd))',
  border: 'var(--border, #dbe0e6)',
}

const card: CSSProperties = {
  background: C.surface,
  border: `1px solid ${C.border}`,
  borderRadius: 14,
}

interface Props {
  data: BlockData | null
  loading: boolean
  error: string | null
  period: RatingPeriodValue
  onPeriodChange: (p: RatingPeriodValue) => void
  /** `cabinet` — o'quvchi ilovasi (tor ekran, kabinet ranglari). */
  variant?: 'admin' | 'cabinet'
  /** Blok ostidagi qo'shimcha (kabinetdagi TOP ro'yxatlar). */
  children?: ReactNode
}

export function StudentRatingBlock({ data, loading, error, period, onPeriodChange, variant = 'admin', children }: Props) {
  return (
    <div className="space-y-4" style={{ color: C.text }}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        {variant === 'admin' && (
          <div className="flex items-center gap-2 text-[15px] font-bold">
            <Trophy className="h-5 w-5" style={{ color: RATING_LINE }} /> Reyting
            {data && <span className="text-[13px] font-semibold" style={{ color: C.muted }}>· {data.period.label}</span>}
          </div>
        )}
        <RatingPeriodSwitch value={period} onChange={onPeriodChange} />
      </div>

      {error && (
        <div className="rounded-lg border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>
      )}
      {loading && !data && (
        <div className="py-10 text-center text-sm" style={{ color: C.muted }}>
          Reyting yuklanmoqda...
        </div>
      )}

      {data && (
        <div className={`space-y-4 transition-opacity ${loading ? 'opacity-60' : ''}`}>
          <Tiles data={data} variant={variant} />
          <HistoryCharts data={data} />
          {variant === 'cabinet' ? <TestsList data={data} /> : <TestsTable data={data} />}
          <AttendanceLine data={data} />
          {children}
        </div>
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ plitkalar */

function Tile({ icon, title, children, hint, className }: { icon: ReactNode; title: string; children: ReactNode; hint?: string; className?: string }) {
  return (
    <div className={`min-w-0 p-4 ${className ?? ''}`} style={card} title={hint}>
      <div className="mb-1.5 flex items-center gap-1.5 text-[12px] font-semibold uppercase tracking-wide" style={{ color: C.muted }}>
        {icon}
        {title}
      </div>
      {children}
    </div>
  )
}

function Tiles({ data, variant }: { data: BlockData; variant: 'admin' | 'cabinet' }) {
  const { centre, groups, components } = data
  const delta = rankDelta(centre.rank, centre.prevRank)
  const formula =
    groups.length === 1
      ? scoreFormulaText(groups[0].testAvg, groups[0].ballNorm, components.testWeight, groups[0].homeworkAvg ?? null, components.homeworkWeight ?? 0)
      : groups
          .filter((g) => g.score != null)
          .map((g) => `${g.groupName}: ${fmtScore(g.score)} (${scoreFormulaText(g.testAvg, g.ballNorm, components.testWeight, g.homeworkAvg ?? null, components.homeworkWeight ?? 0)})`)
          .join('\n') || scoreFormulaText(null, null, components.testWeight)

  // Cabinet (phone): two compact tiles side by side, the score tile full width under them.
  const cabinet = variant === 'cabinet'
  return (
    <div className={cabinet ? 'grid grid-cols-2 gap-3 sm:grid-cols-3' : 'grid grid-cols-1 gap-3 sm:grid-cols-3'}>
      <Tile icon={<Award className="h-3.5 w-3.5" />} title="Markazda">
        {centre.rank != null ? (
          <div className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
            <span className="text-[26px] font-extrabold leading-none" style={{ color: RATING_LINE }}>
              {centre.rank}-o'rin
            </span>
            <span className="text-[13px] font-semibold" style={{ color: C.muted }}>
              / {centre.total}
            </span>
            {delta && delta.dir !== 'same' && (
              <span
                className="inline-flex items-center gap-0.5 rounded-full px-1.5 py-0.5 text-[12px] font-bold"
                style={{ color: delta.dir === 'up' ? UP : DOWN, background: delta.dir === 'up' ? '#0284c71a' : '#e11d481a' }}
                title={`Oldingi davrda: ${centre.prevRank}-o'rin`}
              >
                {delta.dir === 'up' ? '▲' : '▼'}
                {delta.n}
              </span>
            )}
            {delta?.dir === 'same' && (
              <span className="text-[12px] font-semibold" style={{ color: C.muted }} title="Oldingi davrda ham shu o'rin">
                =
              </span>
            )}
          </div>
        ) : (
          <EmptyValue text="Bu davrda o'rin yo'q" />
        )}
      </Tile>

      <Tile icon={<Users className="h-3.5 w-3.5" />} title="Guruhda">
        {groups.length === 0 ? (
          <EmptyValue text="Guruh yo'q" />
        ) : groups.length === 1 ? (
          <>
            <div className="flex items-baseline gap-2">
              <span className="text-[26px] font-extrabold leading-none" style={{ color: RATING_LINE }}>
                {groups[0].rank != null ? `${groups[0].rank}-o'rin` : '—'}
              </span>
              {groups[0].total > 0 && (
                <span className="text-[13px] font-semibold" style={{ color: C.muted }}>
                  / {groups[0].total}
                </span>
              )}
            </div>
            <div className="mt-1 line-clamp-2 text-[12.5px]" style={{ color: C.muted, overflowWrap: 'anywhere' }} title={groups[0].groupName}>
              {groups[0].groupName}
            </div>
          </>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {groups.map((g) => (
              <span
                key={g.groupId}
                className="inline-flex max-w-full items-center gap-1 rounded-full px-2 py-1 text-[12.5px] font-semibold"
                style={{ background: C.surface2, border: `1px solid ${C.border}` }}
                title={g.courseName}
              >
                <span className="truncate">{g.groupName}</span>
                <span style={{ color: RATING_LINE }}>{g.rank != null ? `${g.rank}/${g.total}` : '—'}</span>
              </span>
            ))}
          </div>
        )}
      </Tile>

      <Tile icon={<Trophy className="h-3.5 w-3.5" />} title="Reyting bali" hint={formula} className={cabinet ? 'col-span-2 sm:col-span-1' : undefined}>
        {centre.score != null ? (
          <>
            <div className="flex items-baseline gap-1.5">
              <span className="text-[26px] font-extrabold leading-none" style={{ color: RATING_LINE }}>
                {fmtScore(centre.score)}
              </span>
              <Info className="h-3.5 w-3.5 cursor-help" style={{ color: C.muted }} aria-label={formula} />
            </div>
            <div className="mt-1 text-[12.5px]" style={{ color: C.muted }}>
              Testlar {components.testAvg != null ? `${fmtScore(components.testAvg)}%` : '—'} · Ball {components.ball}
            </div>
          </>
        ) : (
          <EmptyValue text="Test ham, ball ham yo'q" />
        )}
      </Tile>
    </div>
  )
}

function EmptyValue({ text }: { text: string }) {
  return (
    <div className="text-[14px] font-semibold" style={{ color: C.muted }}>
      {text}
    </div>
  )
}

/* ------------------------------------------------------------------ 12 oylik grafiklar */

const axisTick = { fontSize: 11, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 10, border: '1px solid #e2e8f0', fontSize: 12 }

function HistoryCharts({ data }: { data: BlockData }) {
  const points = data.history.map((h) => ({
    month: h.month,
    label: shortMonth(h.month),
    rank: h.centreRank,
    total: h.centreTotal,
    score: h.score,
  }))
  const hasAny = points.some((p) => p.rank != null)
  if (!hasAny) {
    return (
      <div className="p-4 text-center text-[13px]" style={{ ...card, color: C.muted }}>
        12 oylik tarix hali yo'q — birinchi test yoki baho qo'yilgach chiziladi.
      </div>
    )
  }
  return (
    <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
      <ChartCard title="Markazdagi o'rin (12 oy)" sub="Yuqori — yaxshiroq">
        <LineChart data={points} margin={{ top: 8, right: 12, bottom: 0, left: -18 }}>
          <CartesianGrid stroke="#eef0f4" vertical={false} />
          <XAxis dataKey="label" tick={axisTick} tickLine={false} axisLine={false} interval="preserveStartEnd" />
          <YAxis reversed allowDecimals={false} domain={[1, 'dataMax']} tick={axisTick} tickLine={false} axisLine={false} width={44} />
          <Tooltip
            contentStyle={tooltipStyle}
            labelFormatter={(_, p) => (p?.[0] ? monthLabel(String(p[0].payload.month)) : '')}
            formatter={(v, _n, p) => [`${v}-o'rin / ${p.payload.total}`, 'Markazda']}
          />
          <Line type="monotone" dataKey="rank" stroke={RATING_LINE} strokeWidth={2.5} dot={{ r: 3 }} connectNulls={false} />
        </LineChart>
      </ChartCard>
      <ChartCard title="Reyting bali (12 oy)" sub="0 – 100">
        <LineChart data={points} margin={{ top: 8, right: 12, bottom: 0, left: -18 }}>
          <CartesianGrid stroke="#eef0f4" vertical={false} />
          <XAxis dataKey="label" tick={axisTick} tickLine={false} axisLine={false} interval="preserveStartEnd" />
          <YAxis domain={[0, 100]} tick={axisTick} tickLine={false} axisLine={false} width={44} />
          <Tooltip
            contentStyle={tooltipStyle}
            labelFormatter={(_, p) => (p?.[0] ? monthLabel(String(p[0].payload.month)) : '')}
            formatter={(v) => [fmtScore(Number(v)), 'Reyting bali']}
          />
          <Line type="monotone" dataKey="score" stroke={RATING_LINE} strokeWidth={2.5} dot={{ r: 3 }} connectNulls={false} />
        </LineChart>
      </ChartCard>
    </div>
  )
}

function ChartCard({ title, sub, children }: { title: string; sub: string; children: ReactElement }) {
  return (
    <div className="min-w-0 p-3" style={card}>
      <div className="mb-1 flex items-baseline justify-between gap-2 px-1">
        <span className="text-[13px] font-bold">{title}</span>
        <span className="text-[11px]" style={{ color: C.muted }}>
          {sub}
        </span>
      </div>
      <div style={{ width: '100%', height: 150 }}>
        <ResponsiveContainer width="100%" height="100%">
          {children}
        </ResponsiveContainer>
      </div>
    </div>
  )
}

/* ------------------------------------------------------------------ «Testlarim» */

function TestsTable({ data }: { data: BlockData }) {
  return (
    <div className="min-w-0 overflow-hidden" style={card}>
      <div className="flex items-center justify-between px-4 py-3" style={{ borderBottom: `1px solid ${C.border}` }}>
        <span className="text-[14px] font-bold">Testlarim</span>
        <span className="text-[12px]" style={{ color: C.muted }}>
          {data.tests.length > 0 ? `${data.tests.length} ta test` : ''}
        </span>
      </div>
      {data.tests.length === 0 ? (
        <div className="px-4 py-6 text-center text-[13px]" style={{ color: C.muted }}>
          Bu davrda test yo'q.
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[520px] text-[13px]">
            <thead>
              <tr className="text-left text-[11.5px] uppercase tracking-wide" style={{ color: C.muted }}>
                <th className="px-4 py-2 font-semibold">Sana</th>
                <th className="px-2 py-2 font-semibold">Test</th>
                <th className="px-2 py-2 text-right font-semibold">Natija</th>
                <th className="px-2 py-2 text-right font-semibold">Foiz</th>
                <th className="px-4 py-2 text-right font-semibold">O'rin</th>
              </tr>
            </thead>
            <tbody>
              {data.tests.map((t) => {
                const kind = testKindLabel(t.kind)
                return (
                  <tr key={t.testId} style={{ borderTop: `1px solid ${C.border}` }}>
                    <td className="whitespace-nowrap px-4 py-2" style={{ color: C.muted }}>
                      {t.date.slice(8, 10)}.{t.date.slice(5, 7)}
                    </td>
                    <td className="px-2 py-2">
                      <div className="flex flex-wrap items-center gap-1.5">
                        <span className="font-semibold">{t.name}</span>
                        {kind && (
                          <span
                            className="rounded-full px-1.5 py-0.5 text-[10.5px] font-bold"
                            style={{ background: '#6366f11a', color: RATING_LINE }}
                          >
                            {kind}
                          </span>
                        )}
                      </div>
                      <div className="text-[11.5px]" style={{ color: C.muted }}>
                        {t.groupName}
                      </div>
                    </td>
                    <td className="whitespace-nowrap px-2 py-2 text-right font-semibold">{fmtSolved(t.solved, t.questions)}</td>
                    <td className="whitespace-nowrap px-2 py-2 text-right font-bold" style={{ color: RATING_LINE }}>
                      {t.percent != null ? `${fmtScore(t.percent)}%` : '—'}
                    </td>
                    <td className="whitespace-nowrap px-4 py-2 text-right">
                      {t.rankInTest}/{t.takers}
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

/** «Testlarim» for the phone cabinet — one card row per test instead of a wide table. */
function TestsList({ data }: { data: BlockData }) {
  return (
    <div className="min-w-0 overflow-hidden" style={card}>
      <div className="flex items-center justify-between px-4 py-3" style={{ borderBottom: `1px solid ${C.border}` }}>
        <span className="text-[15px] font-bold">Testlarim</span>
        <span className="text-[12.5px]" style={{ color: C.muted }}>
          {data.tests.length > 0 ? `${data.tests.length} ta test` : ''}
        </span>
      </div>
      {data.tests.length === 0 ? (
        <div className="px-4 py-6 text-center text-[14px]" style={{ color: C.muted }}>
          Bu davrda test yo'q.
        </div>
      ) : (
        data.tests.map((t, i) => {
          const kind = testKindLabel(t.kind)
          return (
            <div key={t.testId} className="flex items-center gap-3 px-4 py-3" style={i > 0 ? { borderTop: `1px solid ${C.border}` } : undefined}>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-1.5">
                  <span className="text-[14.5px] font-bold" style={{ overflowWrap: 'anywhere' }}>{t.name}</span>
                  {kind && (
                    <span className="rounded-full px-2 py-0.5 text-[11px] font-bold" style={{ background: '#6366f11a', color: RATING_LINE }}>
                      {kind}
                    </span>
                  )}
                </div>
                <div className="mt-0.5 text-[12.5px]" style={{ color: C.muted }}>
                  {t.date.slice(8, 10)}.{t.date.slice(5, 7)} · {t.groupName} · {t.rankInTest}/{t.takers}-o'rin
                </div>
              </div>
              <div className="flex-none text-right">
                <div className="text-[17px] font-extrabold" style={{ color: RATING_LINE, fontVariantNumeric: 'tabular-nums' }}>
                  {t.percent != null ? `${fmtScore(t.percent)}%` : '—'}
                </div>
                <div className="text-[12px] font-semibold" style={{ color: C.muted }}>{fmtSolved(t.solved, t.questions)}</div>
              </div>
            </div>
          )
        })
      )}
    </div>
  )
}

/* ------------------------------------------------------------------ davomat */

function AttendanceLine({ data }: { data: BlockData }) {
  const a = data.attendance
  return (
    <div className="flex items-center gap-2 px-4 py-3 text-[13.5px]" style={card}>
      <CalendarCheck className="h-4 w-4" style={{ color: RATING_LINE }} />
      <span className="font-semibold">Davomat:</span>
      {a.percent != null ? (
        <span>
          <b>{a.percent}%</b>{' '}
          <span style={{ color: C.muted }}>
            ({a.attended}/{a.held})
          </span>
        </span>
      ) : (
        <span style={{ color: C.muted }}>bu davrda o'tilgan dars yo'q</span>
      )}
      {data.centre.prevRank != null && data.centre.rank != null && (
        <span className="ml-auto hidden items-center gap-1 text-[12px] sm:inline-flex" style={{ color: C.muted }}>
          {data.centre.rank <= data.centre.prevRank ? (
            <TrendingUp className="h-3.5 w-3.5" />
          ) : (
            <TrendingDown className="h-3.5 w-3.5" />
          )}
          Oldingi davr: {data.centre.prevRank}-o'rin
        </span>
      )}
    </div>
  )
}
