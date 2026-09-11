import { useEffect, useMemo, useState } from 'react'
import {
  Bar, BarChart, CartesianGrid, Legend, Line, LineChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import { BookOpen, Users, School, Wallet, Layers } from 'lucide-react'
import {
  getCourseAnalytics, type CourseAnalytics, type CourseAnalyticsRow,
} from '@/api/services/courseAnalytics'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { PageHeader } from '@/components/ui/PageHeader'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'
import { monthShortNames } from '@/config/constants'

/**
 * KURSLAR ANALITIKASI — qaysi kursga oyma-oy nechta o'quvchi keldi/ketdi, hozir qaysi kursda
 * nechta o'quvchi bor va nechtasi birdan ortiq kursga qatnaydi.
 *
 * <p>Butun hisob serverda (`CourseAnalytics`) — bu sahifa faqat chizadi.</p>
 */

/** Grafik ranglari — CVD (rang ko'rmaslik) uchun TEKSHIRILGAN juftlik.
 *  Yashil/qizil ATAYIN olinmadi: deuteranopiya'da ular deyarli ajralmaydi (ΔE 2.7). */
const C_JOINED = '#0284c7'   // sky-600 — kelgan
const C_LEFT = '#e11d48'     // rose-600 — ketgan
const C_ACTIVE = '#6366f1'   // indigo-500 — faol (yakka seriya)

const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }

type Tab = 'umumiy' | 'oqim' | 'kesishuv'

/** "2026-03" → "Mar 26" (o'q yorlig'i qisqa bo'lsin). */
const shortMonth = (m: string) =>
  m.length >= 7 ? `${monthShortNames[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(2, 4)}` : m

export function CourseAnalyticsPage() {
  const [months, setMonths] = useState(12)
  const [data, setData] = useState<CourseAnalytics | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  const [tab, setTab] = useState<Tab>('umumiy')
  /** '' — barcha kurslar jamlanmasi; aks holda bitta kurs. */
  const [courseId, setCourseId] = useState('')

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getCourseAnalytics(months)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Analitikani yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [months])

  /** Tanlangan kurs (yoki barcha kurslar yig'indisi) bo'yicha oylik qatorlar. */
  const flow = useMemo(() => {
    if (!data) return []
    const selected = courseId ? data.courses.filter((c) => c.courseId === courseId) : data.courses
    return data.months.map((m, i) => {
      const row = { month: m, name: shortMonth(m), joined: 0, left: 0, completed: 0, activeEnd: 0 }
      for (const c of selected) {
        const f = c.monthly[i]
        if (!f) continue
        row.joined += f.joined
        row.left += f.left
        row.completed += f.completed
        row.activeEnd += f.activeEnd
      }
      return row
    })
  }, [data, courseId])

  /** Joriy (oxirgi) oy — "bu oy kelgan/ketgan" ustunlari uchun. */
  const lastIdx = (data?.months.length ?? 0) - 1
  const thisMonth = useMemo(() => {
    if (!data || lastIdx < 0) return { joined: 0, left: 0 }
    return data.courses.reduce(
      (acc, c) => {
        const f = c.monthly[lastIdx]
        return f ? { joined: acc.joined + f.joined, left: acc.left + f.left } : acc
      },
      { joined: 0, left: 0 },
    )
  }, [data, lastIdx])

  return (
    <div>
      <PageHeader
        title="Kurslar analitikasi"
        sub="Qaysi kursga oyma-oy nechta o'quvchi keldi va ketdi, kurslar kesishuvi"
        actions={
          <div className="flex gap-1">
            {[6, 12, 24].map((n) => (
              <button
                key={n}
                type="button"
                onClick={() => setMonths(n)}
                className={cn(
                  'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                  months === n
                    ? 'border-brand-500 bg-brand-50 text-brand-700'
                    : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                )}
              >
                {n} oy
              </button>
            ))}
          </div>
        }
      />

      <div className="mb-4 flex gap-1 border-b border-slate-200">
        {([
          ['umumiy', 'Umumiy'],
          ['oqim', 'Oylik oqim'],
          ['kesishuv', 'Kurslar kesishuvi'],
        ] as const).map(([key, label]) => (
          <button
            key={key}
            type="button"
            className={cn('tab', tab === key && 'active')}
            onClick={() => setTab(key)}
          >
            {label}
          </button>
        ))}
      </div>

      {error && <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}

      {loading || !data ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {/* ==================== UMUMIY ==================== */}
          {tab === 'umumiy' && (
            <>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
                <StatCard label="Kurslar" value={data.courses.length} icon={BookOpen} />
                <StatCard
                  label="Faol o'quvchilar"
                  value={data.activeStudents}
                  icon={Users}
                  iconBg="bg-emerald-50"
                  iconColor="text-emerald-600"
                  hint="Takrorsiz — bir nechta kursda o'qisa ham bitta"
                />
                <StatCard label="Guruhlar" value={data.totalGroups} icon={School} />
                <StatCard
                  label="Oylik tushum"
                  value={formatMoney(data.monthlyRevenue)}
                  icon={Wallet}
                  hint="Faol a'zoliklar bo'yicha kutilayotgan"
                />
                <StatCard
                  label="Bu oy"
                  value={`+${thisMonth.joined} / −${thisMonth.left}`}
                  icon={Layers}
                  hint="Kelgan / ketgan"
                  delta={
                    thisMonth.joined !== thisMonth.left
                      ? {
                          value: `${thisMonth.joined - thisMonth.left > 0 ? '+' : ''}${thisMonth.joined - thisMonth.left}`,
                          dir: thisMonth.joined >= thisMonth.left ? 'up' : 'down',
                        }
                      : undefined
                  }
                />
              </div>

              <Card
                title="Kurslar kesimi"
                sub="Hozirgi holat. «Jami (hozir)» — takrorsiz o'quvchilar: bir kursning ikki guruhida o'qisa ham bitta sanaladi."
              >
                {data.courses.length === 0 ? (
                  <p className="py-8 text-center text-sm text-slate-400">Kurs qo'shilmagan</p>
                ) : (
                  <div className="overflow-x-auto">
                    <table className="table">
                      <thead>
                        <tr>
                          <th>Kurs</th>
                          <th className="num">Guruh</th>
                          <th className="num">O'qituvchi</th>
                          <th className="num">Faol</th>
                          <th className="num">Sinov</th>
                          <th className="num">Muzlatilgan</th>
                          <th className="num">Jami (hozir)</th>
                          <th className="num">Bu oy</th>
                          <th className="num">Oylik tushum</th>
                          <th className="num">Jami (tarixda)</th>
                        </tr>
                      </thead>
                      <tbody>
                        {[...data.courses]
                          .sort((a, b) => b.active - a.active)
                          .map((c) => {
                            const f = c.monthly[lastIdx]
                            return (
                              <tr key={c.courseId}>
                                <td className="font-medium text-slate-700">{c.courseName}</td>
                                <td className="num">{c.groups || '—'}</td>
                                <td className="num">{c.teachers || '—'}</td>
                                <td className="num font-semibold text-emerald-600">{c.active || '—'}</td>
                                <td className="num">{c.trial || '—'}</td>
                                <td className="num">{c.frozen || '—'}</td>
                                <td className="num font-semibold">{c.students || '—'}</td>
                                <td className="num">
                                  {f && (f.joined || f.left) ? (
                                    <span>
                                      <span className="text-sky-600">+{f.joined}</span>
                                      {' / '}
                                      <span className="text-rose-600">−{f.left}</span>
                                    </span>
                                  ) : '—'}
                                </td>
                                <td className="num">{c.monthlyRevenue ? formatMoney(c.monthlyRevenue) : '—'}</td>
                                <td className="num text-slate-400">{c.totalEver || '—'}</td>
                              </tr>
                            )
                          })}
                      </tbody>
                    </table>
                  </div>
                )}
              </Card>
            </>
          )}

          {/* ==================== OYLIK OQIM ==================== */}
          {tab === 'oqim' && (
            <>
              <Card
                title="Kelgan va ketgan"
                sub="«Ketgan» — kursdan haqiqatan chiqib ketganlar. Guruh almashtirish va kursni tugatish bunga KIRMAYDI."
                actions={
                  <select
                    value={courseId}
                    onChange={(e) => setCourseId(e.target.value)}
                    className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                  >
                    <option value="">Barcha kurslar</option>
                    {data.courses.map((c) => (
                      <option key={c.courseId} value={c.courseId}>
                        {c.courseName}
                      </option>
                    ))}
                  </select>
                }
              >
                <ResponsiveContainer width="100%" height={300}>
                  <BarChart data={flow} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
                    <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                    <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} width={36} />
                    <Tooltip contentStyle={tooltipStyle} />
                    <Legend iconType="circle" wrapperStyle={{ fontSize: 12 }} />
                    <Bar dataKey="joined" name="Kelgan" fill={C_JOINED} radius={[4, 4, 0, 0]} maxBarSize={22} />
                    <Bar dataKey="left" name="Ketgan" fill={C_LEFT} radius={[4, 4, 0, 0]} maxBarSize={22} />
                  </BarChart>
                </ResponsiveContainer>
              </Card>

              <Card
                title="Faol o'quvchilar"
                sub="Har oy oxirida faol (aktivlashgan, ketmagan, muzlatilmagan) o'quvchilar soni."
              >
                <ResponsiveContainer width="100%" height={260}>
                  <LineChart data={flow} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke="#eef0f4" />
                    <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                    <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} width={36} />
                    <Tooltip contentStyle={tooltipStyle} />
                    <Line
                      type="monotone" dataKey="activeEnd" name="Faol o'quvchilar"
                      stroke={C_ACTIVE} strokeWidth={2} dot={{ r: 3 }}
                    />
                  </LineChart>
                </ResponsiveContainer>
              </Card>

              {/* MATRITSA — asosiy talab: har kurs, har oy, kelgan/ketgan */}
              <Card
                title="Kurs × oy"
                sub="Har katakda «kelgan / ketgan». Bo'sh katak — o'sha oyda harakat bo'lmagan."
              >
                <div className="overflow-x-auto">
                  <table className="table">
                    <thead>
                      <tr>
                        <th className="sticky left-0 bg-white">Kurs</th>
                        {data.months.map((m) => (
                          <th key={m} className="num whitespace-nowrap">{shortMonth(m)}</th>
                        ))}
                      </tr>
                    </thead>
                    <tbody>
                      {data.courses.map((c) => (
                        <tr key={c.courseId}>
                          <td className="sticky left-0 bg-white font-medium text-slate-700">
                            {c.courseName}
                          </td>
                          {c.monthly.map((f) => (
                            <td key={f.month} className="num whitespace-nowrap">
                              {f.joined || f.left ? (
                                <>
                                  <span className="text-sky-600">{f.joined ? `+${f.joined}` : ''}</span>
                                  {f.joined && f.left ? ' ' : ''}
                                  <span className="text-rose-600">{f.left ? `−${f.left}` : ''}</span>
                                </>
                              ) : (
                                <span className="text-slate-300">·</span>
                              )}
                            </td>
                          ))}
                        </tr>
                      ))}
                      <tr className="border-t-2 border-slate-200 font-semibold">
                        <td className="sticky left-0 bg-white">Jami</td>
                        {flowTotals(data).map((t, i) => (
                          <td key={i} className="num whitespace-nowrap">
                            <span className="text-sky-600">+{t.joined}</span>
                            {' '}
                            <span className="text-rose-600">−{t.left}</span>
                          </td>
                        ))}
                      </tr>
                    </tbody>
                  </table>
                </div>
              </Card>
            </>
          )}

          {/* ==================== KESISHUV ==================== */}
          {tab === 'kesishuv' && (
            <>
              <div className="grid gap-3 sm:grid-cols-3">
                <StatCard
                  label="Faol o'quvchilar"
                  value={data.overlap.totalStudents}
                  icon={Users}
                  hint="Kamida bitta kursda faol"
                />
                <StatCard
                  label="Bitta kursda"
                  value={data.overlap.oneCourse}
                  icon={BookOpen}
                  hint={pct(data.overlap.oneCourse, data.overlap.totalStudents)}
                />
                <StatCard
                  label="Birdan ortiq kursda"
                  value={data.overlap.multiCourse}
                  icon={Layers}
                  iconBg="bg-indigo-50"
                  iconColor="text-indigo-600"
                  hint={pct(data.overlap.multiCourse, data.overlap.totalStudents)}
                />
              </div>

              <Card
                title="Nechta kursga qatnaydi"
                sub="Faqat FAOL a'zoliklar bo'yicha (sinovdagi va muzlatilganlar hisobga olinmaydi)."
              >
                {data.overlap.buckets.length === 0 ? (
                  <p className="py-8 text-center text-sm text-slate-400">Faol o'quvchi yo'q</p>
                ) : (
                  <ul className="space-y-2">
                    {data.overlap.buckets.map((b) => {
                      const p = data.overlap.totalStudents > 0
                        ? Math.round((b.students / data.overlap.totalStudents) * 100) : 0
                      return (
                        <li key={b.courses}>
                          <div className="flex items-center justify-between text-sm">
                            <span className="text-slate-600">{b.courses} ta kurs</span>
                            <span className="font-semibold text-slate-700">
                              {b.students} <span className="text-xs font-normal text-slate-400">({p}%)</span>
                            </span>
                          </div>
                          <div className="mt-1 h-1.5 w-full overflow-hidden rounded-full bg-slate-100">
                            <div
                              className="h-full rounded-full"
                              style={{ width: `${p}%`, background: b.courses > 1 ? C_ACTIVE : '#94a3b8' }}
                            />
                          </div>
                        </li>
                      )
                    })}
                  </ul>
                )}
              </Card>

              <Card
                title="Birga o'qiladigan kurslar"
                sub="Qaysi ikki kurs ko'pincha birga olinadi — qo'shimcha kurs taklif qilish uchun."
              >
                {data.overlap.pairs.length === 0 ? (
                  <p className="py-8 text-center text-sm text-slate-400">
                    Hech kim birdan ortiq kursga qatnamaydi
                  </p>
                ) : (
                  <table className="table">
                    <thead>
                      <tr>
                        <th>Kurs</th>
                        <th>Kurs</th>
                        <th className="num">O'quvchilar</th>
                      </tr>
                    </thead>
                    <tbody>
                      {data.overlap.pairs.map((p) => (
                        <tr key={`${p.aId}-${p.bId}`}>
                          <td className="text-slate-700">{p.aName}</td>
                          <td className="text-slate-700">{p.bName}</td>
                          <td className="num font-semibold">{p.students}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                )}
              </Card>
            </>
          )}
        </div>
      )}
    </div>
  )
}

/** Har oy uchun barcha kurslar bo'yicha jami kelgan/ketgan. */
function flowTotals(data: CourseAnalytics): { joined: number; left: number }[] {
  return data.months.map((_, i) =>
    data.courses.reduce(
      (acc: { joined: number; left: number }, c: CourseAnalyticsRow) => {
        const f = c.monthly[i]
        return f ? { joined: acc.joined + f.joined, left: acc.left + f.left } : acc
      },
      { joined: 0, left: 0 },
    ),
  )
}

const pct = (part: number, total: number) =>
  total > 0 ? `${Math.round((part / total) * 100)}% — jami ${total} tadan` : undefined
