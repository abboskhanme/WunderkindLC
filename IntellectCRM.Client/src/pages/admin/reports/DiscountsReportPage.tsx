import { useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import {
  Bar, BarChart, CartesianGrid, Line, LineChart,
  ResponsiveContainer, Tooltip, XAxis, YAxis,
} from 'recharts'
import {
  BadgePercent, CalendarDays, ChevronDown, ChevronUp, Percent,
  RefreshCw, Search, Users, Wallet,
} from 'lucide-react'
import {
  getDiscountReport,
  type DiscountReport,
  type DiscountReportStudent,
} from '@/api/services/discounts'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'
import { formatMonth, monthShortNames } from '@/config/constants'
import { apiErrorMessage, cn, formatMoney } from '@/lib/utils'

/**
 * CHEGIRMALAR HISOBOTI — "kimga qancha chegirma berilgan va u qayerda ketyapti".
 *
 * Butun hisob SERVERDA (`GET /admin/reports/discounts`) — bu sahifa faqat chizadi va
 * klient tomonida qidiruv/saralash qiladi. Chegirma MANTIG'I bu yerda ham, shartnomada ham
 * yo'q: pul hisobi avvalgidek `Student.discount*` maydonlariga tayanadi
 * (`.claude/rules/discounts.md`).
 */

/* Grafik ranglari — CVD (rang ko'rmaslik) uchun TEKSHIRILGAN, kurslar analitikasi bilan BIR XIL.
 * Yashil/qizil ATAYIN olinmadi: deuteranopiya'da ular deyarli ajralmaydi. */
const C_DISCOUNT = '#0284c7' // sky-600 — chegirma summasi
const C_STUDENTS = '#6366f1' // indigo-500 — chegirma olgan o'quvchilar (yakka seriya)

const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }
const gridStroke = '#eef0f4'

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-mono text-slate-700 outline-none focus:border-brand-400'
const searchInput =
  'w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Joriy oy ("yyyy-MM"). */
const currentMonth = new Date().toISOString().slice(0, 7)

/** "yyyy-MM" ga oy qo'shish/ayirish (oy chegarasidan o'tishni Date o'zi normallashtiradi). */
function addMonths(ym: string, delta: number): string {
  const y = Number(ym.slice(0, 4))
  const m = Number(ym.slice(5, 7))
  const d = new Date(Date.UTC(y, m - 1 + delta, 1))
  return d.toISOString().slice(0, 7)
}

/** "2026-03" → "Mar 26" (o'q yorlig'i qisqa bo'lsin). */
const shortMonth = (m: string) =>
  m.length >= 7 ? `${monthShortNames[Number(m.slice(5, 7)) - 1] ?? m} ${m.slice(2, 4)}` : m

/** Oy yorlig'i — bo'sh/buzuq qiymatda "—" (eski yozuvda davr bo'sh bo'lishi mumkin). */
const monthLabel = (ym: string) => (/^\d{4}-\d{2}$/.test(ym) ? formatMonth(ym) : '—')

/** Registr yozuvining amal qilish davri: bo'sh chegara — "cheklovsiz". */
function periodLabel(start: string, end: string): string {
  const s = /^\d{4}-\d{2}$/.test(start) ? formatMonth(start) : 'boshidan'
  const e = /^\d{4}-\d{2}$/.test(end) ? formatMonth(end) : 'cheklovsiz'
  return `${s} — ${e}`
}

/** Foiz — ortiqcha nollarsiz ("12.5%", "7%"). */
const pctLabel = (n: number) => `${Number(n.toFixed(1))}%`

/** Chegirma o'lchami: foiz va/yoki aniq summa (ikkalasi ham bo'lishi mumkin). */
function sizeLabel(pct: number, amount: number): string {
  const parts: string[] = []
  if (pct) parts.push(pctLabel(pct))
  if (amount) parts.push(formatMoney(amount))
  return parts.length ? parts.join(' + ') : '—'
}

/** Nol summa jadvalda "—" bo'lib chiqadi — nollar qatori raqamni ko'rishga xalaqit beradi. */
const money = (n: number) => (n ? formatMoney(n) : '—')

/** `charged` ichida `discount` qancha ulush egallaydi. */
const share = (discount: number, charged: number) => (charged > 0 ? (discount / charged) * 100 : 0)

type Tab = 'teachers' | 'groups' | 'students' | 'reasons' | 'active'
type StudentSort = 'name' | 'charged' | 'discount' | 'months' | 'active'

const TABS: Array<[Tab, string]> = [
  ['teachers', "O'qituvchilar kesimi"],
  ['groups', 'Guruhlar kesimi'],
  ['students', "O'quvchilar"],
  ['reasons', 'Sabablar'],
  ['active', 'Hozir amaldagi'],
]

/**
 * Saralanadigan ustun sarlavhasi (o'q bilan).
 *
 * ⚠️ Komponent ATAYIN modul darajasida: sahifa ichida e'lon qilinsa har renderda YANGI tur
 * bo'lib, React uni qayta yaratardi (`react-hooks/static-components`).
 */
function SortTh({ col, label, sort, desc, onSort }: {
  col: StudentSort
  label: string
  sort: StudentSort
  desc: boolean
  onSort: (k: StudentSort) => void
}) {
  return (
    <th className={cn(col !== 'name' && 'num')}>
      <button
        type="button"
        onClick={() => onSort(col)}
        className="inline-flex items-center gap-1 uppercase tracking-[0.06em] hover:text-slate-600"
      >
        {label}
        {sort === col && (desc ? <ChevronDown className="h-3 w-3" /> : <ChevronUp className="h-3 w-3" />)}
      </button>
    </th>
  )
}

export function DiscountsReportPage() {
  const [from, setFrom] = useState(() => addMonths(currentMonth, -11))
  const [to, setTo] = useState(currentMonth)
  const [data, setData] = useState<DiscountReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState('')
  /** Qayta yuklash so'rovi — «Yangilash» tugmasi shuni oshiradi (effekt ichida setState yo'q). */
  const [tick, setTick] = useState(0)
  const [tab, setTab] = useState<Tab>('teachers')
  const [search, setSearch] = useState('')
  const [sort, setSort] = useState<StudentSort>('discount')
  const [desc, setDesc] = useState(true)

  useEffect(() => {
    let active = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda qayta yuklash (maqsadli)
    setLoading(true)
    getDiscountReport(from, to)
      .then((d) => { if (active) { setData(d); setError('') } })
      .catch((e) => { if (active) setError(apiErrorMessage(e, "Hisobotni yuklab bo'lmadi")) })
      .finally(() => { if (active) setLoading(false) })
    return () => { active = false }
  }, [from, to, tick])

  /** Tez tugma — tanlangan davr bilan mos tushsa yoritiladi. */
  const rangeIs = (months: number) =>
    to === currentMonth && from === addMonths(currentMonth, -(months - 1))

  const setRange = (months: number) => {
    setFrom(addMonths(currentMonth, -(months - 1)))
    setTo(currentMonth)
  }

  /** Grafik qatorlari — oy yorlig'i qisqartirilgan holda. */
  const chart = useMemo(
    () => (data?.months ?? []).map((m) => ({ ...m, name: shortMonth(m.month) })),
    [data],
  )

  /** O'quvchilar jadvali: matn qidiruvi + ustun bo'yicha saralash. */
  const students = useMemo(() => {
    const q = search.trim().toLowerCase()
    const rows = (data?.byStudent ?? []).filter((s) => {
      if (!q) return true
      return (
        s.studentName.toLowerCase().includes(q) ||
        // Amaldagi chegirmalar yorlig'i — FAN nomi va o'lchami shu matnda ("Matematika 20%").
        s.activeLabel.toLowerCase().includes(q) ||
        s.groupNames.some((g) => g.toLowerCase().includes(q)) ||
        s.teacherNames.some((t) => t.toLowerCase().includes(q))
      )
    })
    const cmp = (a: DiscountReportStudent, b: DiscountReportStudent) => {
      switch (sort) {
        case 'name': return a.studentName.localeCompare(b.studentName)
        case 'charged': return a.charged - b.charged
        case 'months': return a.months - b.months
        // Chegirma o'lchami (foiz) endi o'quvchi darajasida YO'Q — har fan alohida.
        // Shuning uchun "hozirgi chegirma" ustuni amaldagi yozuvlar SONI bo'yicha saralanadi.
        case 'active': return a.activeCount - b.activeCount
        default: return a.discount - b.discount
      }
    }
    return [...rows].sort((a, b) => (desc ? -cmp(a, b) : cmp(a, b)))
  }, [data, search, sort, desc])

  const pg = usePagination(students)

  const toggleSort = (key: StudentSort) => {
    if (sort === key) { setDesc((d) => !d); return }
    setSort(key)
    // Ism — alifbo bo'yicha o'sish, sonlar — kattadan kichikka (odatdagi kutilma).
    setDesc(key !== 'name')
  }

  const s = data?.summary
  /** Davrda umuman harakat bo'lmaganmi — bo'sh jadvallar o'rniga ochiq xabar. */
  const emptyPeriod = !!data && data.months.length === 0

  return (
    <div>
      <PageHeader
        title="Chegirmalar hisoboti"
        sub="Kimga qancha chegirma berilgan — o'quvchi, guruh va o'qituvchi kesimida"
      />

      {/* ==================== DAVR ==================== */}
      <div className="toolbar mb-4">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <input
          type="month"
          value={from}
          max={to}
          onChange={(e) => setFrom(e.target.value)}
          className={control}
          aria-label="Davr boshi"
        />
        <span className="text-slate-400">—</span>
        <input
          type="month"
          value={to}
          min={from}
          onChange={(e) => setTo(e.target.value)}
          className={control}
          aria-label="Davr oxiri"
        />
        <div className="flex gap-1">
          <button
            type="button"
            onClick={() => { setFrom(currentMonth); setTo(currentMonth) }}
            className={cn(
              'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
              from === currentMonth && to === currentMonth
                ? 'border-brand-500 bg-brand-50 text-brand-700'
                : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
            )}
          >
            Joriy oy
          </button>
          {[6, 12].map((n) => (
            <button
              key={n}
              type="button"
              onClick={() => setRange(n)}
              className={cn(
                'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                rangeIs(n)
                  ? 'border-brand-500 bg-brand-50 text-brand-700'
                  : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
              )}
            >
              {n} oy
            </button>
          ))}
        </div>
        <Button variant="secondary" onClick={() => setTick((t) => t + 1)} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
      </div>

      {error && (
        <p className="mb-3 rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>
      )}

      {loading || !data || !s ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <div className="space-y-4">
          {/* ==================== KPI ==================== */}
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
            <StatCard
              label="Amaldagi chegirmalar"
              value={s.activeCount}
              icon={BadgePercent}
              hint="Har FAN uchun alohida sanaladi — o'quvchilar sonidan ko'p bo'lishi mumkin"
            />
            <StatCard
              label="Chegirmali o'quvchilar"
              value={`${s.studentCount} / ${s.totalStudents}`}
              icon={Users}
              iconBg="bg-indigo-50"
              iconColor="text-indigo-600"
              hint={`Barcha o'quvchilarning ${pctLabel(s.studentSharePct)} qismi`}
            />
            <StatCard
              label="Davr bo'yicha chegirma"
              value={formatMoney(s.periodDiscount)}
              icon={Wallet}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
              hint={`Hisoblangan: ${formatMoney(s.periodCharged)}`}
            />
            <StatCard
              label="Chegirma ulushi"
              value={pctLabel(s.sharePct)}
              icon={Percent}
              iconBg="bg-amber-50"
              iconColor="text-amber-600"
              hint="Chegirma / hisoblangan summa"
            />
            <StatCard
              label="Joriy oy chegirmasi"
              value={formatMoney(s.currentMonthDiscount)}
              icon={CalendarDays}
              hint={monthLabel(currentMonth)}
            />
          </div>

          {emptyPeriod ? (
            <Card>
              <p className="py-10 text-center text-sm text-slate-400">
                Tanlangan davrda ({monthLabel(data.from)} — {monthLabel(data.to)}) hech qanday
                hisob yozilmagan. Boshqa davrni tanlang.
              </p>
            </Card>
          ) : (
            <>
              {/* ==================== GRAFIKLAR ====================
                  ⚠️ Ikki o'lchov (summa va o'quvchilar soni) BITTA grafikda ikki y-o'q bilan
                  KO'RSATILMAYDI — alohida grafiklar (`.claude/rules/course-analytics.md` §6). */}
              <Card
                title="Oylik chegirma"
                sub="Har oyda HAQIQATAN berilgan chegirma summasi (hisob qatorlaridan)."
              >
                <ResponsiveContainer width="100%" height={280}>
                  <BarChart data={chart} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                    <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                    <YAxis
                      tickLine={false} axisLine={false} tick={axisTick} width={72}
                      tickFormatter={(v: number) => new Intl.NumberFormat('ru-RU').format(v)}
                    />
                    <Tooltip
                      contentStyle={tooltipStyle}
                      formatter={(v) => [formatMoney(Number(v)), 'Chegirma']}
                    />
                    <Bar dataKey="discount" name="Chegirma" fill={C_DISCOUNT} radius={[4, 4, 0, 0]} maxBarSize={26} />
                  </BarChart>
                </ResponsiveContainer>
              </Card>

              <Card
                title="Chegirma olgan o'quvchilar"
                sub="Har oyda kamida bitta guruhda chegirma qo'llangan o'quvchilar soni."
              >
                <ResponsiveContainer width="100%" height={240}>
                  <LineChart data={chart} margin={{ top: 10, right: 10, left: 0, bottom: 0 }}>
                    <CartesianGrid strokeDasharray="3 3" vertical={false} stroke={gridStroke} />
                    <XAxis dataKey="name" tickLine={false} axisLine={false} tick={axisTick} />
                    <YAxis tickLine={false} axisLine={false} tick={axisTick} allowDecimals={false} width={36} />
                    <Tooltip contentStyle={tooltipStyle} />
                    <Line
                      type="monotone" dataKey="students" name="O'quvchilar"
                      stroke={C_STUDENTS} strokeWidth={2} dot={{ r: 3 }}
                    />
                  </LineChart>
                </ResponsiveContainer>
              </Card>

              {/* ==================== JADVALLAR ==================== */}
              <div className="flex flex-wrap gap-1 border-b border-slate-200">
                {TABS.map(([key, label]) => (
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

              {/* ---------- O'QITUVCHILAR ---------- */}
              {tab === 'teachers' && (
                <Card
                  title="O'qituvchilar kesimi"
                  sub="Guruhning JORIY o'qituvchisi bo'yicha (pul o'sha guruhda hisoblangan). «Ulush» — chegirmaning hisoblangan summadagi qismi."
                  tight
                >
                  {data.byTeacher.length === 0 ? (
                    <p className="py-10 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
                  ) : (
                    <div className="table-wrap">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>O'qituvchi</th>
                            <th className="num">Guruhlar</th>
                            <th className="num">O'quvchilar</th>
                            <th className="num">Hisoblangan</th>
                            <th className="num">Chegirma</th>
                            <th className="num">Ulushi</th>
                          </tr>
                        </thead>
                        <tbody>
                          {[...data.byTeacher]
                            .sort((a, b) => b.discount - a.discount)
                            .map((t) => (
                              <tr key={t.teacherId || t.teacherName}>
                                <td className="font-medium text-slate-700">
                                  {t.teacherName || "— o'qituvchisiz —"}
                                </td>
                                <td className="num">{t.groups || '—'}</td>
                                <td className="num">{t.students || '—'}</td>
                                <td className="num text-slate-500">{money(t.charged)}</td>
                                <td className="num font-semibold text-sky-600">{money(t.discount)}</td>
                                <td className="num">{pctLabel(share(t.discount, t.charged))}</td>
                              </tr>
                            ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </Card>
              )}

              {/* ---------- GURUHLAR ---------- */}
              {tab === 'groups' && (
                <Card
                  title="Guruhlar kesimi"
                  sub="Qaysi guruhda qancha chegirma ketmoqda. Guruhga bog'lanmagan hisoblar alohida qatorda — jimgina yo'qolmaydi."
                  tight
                >
                  {data.byGroup.length === 0 ? (
                    <p className="py-10 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
                  ) : (
                    <div className="table-wrap">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>Guruh</th>
                            <th>Kurs</th>
                            <th>O'qituvchi</th>
                            <th className="num">O'quvchilar</th>
                            <th className="num">Hisoblangan</th>
                            <th className="num">Chegirma</th>
                          </tr>
                        </thead>
                        <tbody>
                          {[...data.byGroup]
                            .sort((a, b) => b.discount - a.discount)
                            .map((g) => (
                              <tr key={g.groupId || g.groupName}>
                                <td className="font-medium text-slate-700">
                                  {g.groupName || '— guruhsiz hisob —'}
                                </td>
                                <td className="text-slate-500">{g.courseName || '—'}</td>
                                <td className="text-slate-500">{g.teacherName || '—'}</td>
                                <td className="num">{g.students || '—'}</td>
                                <td className="num text-slate-500">{money(g.charged)}</td>
                                <td className="num font-semibold text-sky-600">{money(g.discount)}</td>
                              </tr>
                            ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </Card>
              )}

              {/* ---------- O'QUVCHILAR ---------- */}
              {tab === 'students' && (
                <Card
                  title="O'quvchilar"
                  sub="«Hozirgi chegirma» — bugun kuchda turgan yozuvlar, HAR FAN uchun alohida; davrda chegirma olgan, lekin hozir chegirmasi yo'q o'quvchida «—»."
                  tight
                >
                  <div className="border-b border-slate-100 p-[18px]">
                    <div className="relative max-w-sm">
                      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                      <input
                        value={search}
                        onChange={(e) => setSearch(e.target.value)}
                        placeholder="O'quvchi, fan, guruh yoki o'qituvchi..."
                        className={searchInput}
                      />
                    </div>
                  </div>

                  {students.length === 0 ? (
                    <p className="py-10 text-center text-sm text-slate-400">
                      {search ? 'Qidiruvga mos o\'quvchi topilmadi' : "Ma'lumot yo'q"}
                    </p>
                  ) : (
                    <>
                      <div className="table-wrap">
                        <table className="table">
                          <thead>
                            <tr>
                              <SortTh col="name" label="O'quvchi" sort={sort} desc={desc} onSort={toggleSort} />
                              <th>Guruhlar</th>
                              <th>O'qituvchilar</th>
                              <SortTh col="months" label="Oylar" sort={sort} desc={desc} onSort={toggleSort} />
                              <SortTh col="charged" label="Hisoblangan" sort={sort} desc={desc} onSort={toggleSort} />
                              <SortTh col="discount" label="Jami chegirma" sort={sort} desc={desc} onSort={toggleSort} />
                              <SortTh col="active" label="Hozirgi chegirma" sort={sort} desc={desc} onSort={toggleSort} />
                            </tr>
                          </thead>
                          <tbody>
                            {pg.paged.map((st) => (
                              <tr key={st.studentId}>
                                <td>
                                  <Link
                                    to={`/admin/students/${st.studentId}`}
                                    className="font-medium text-brand-600 hover:underline"
                                  >
                                    {st.studentName}
                                  </Link>
                                </td>
                                <td className="text-slate-500">{st.groupNames.join(', ') || '—'}</td>
                                <td className="text-slate-500">{st.teacherNames.join(', ') || '—'}</td>
                                <td className="num">{st.months || '—'}</td>
                                <td className="num text-slate-500">{money(st.charged)}</td>
                                <td className="num font-semibold text-sky-600">{money(st.discount)}</td>
                                <td className="num">
                                  {st.hasActive ? (
                                    <span className="inline-flex flex-wrap items-center justify-end gap-1.5">
                                      <span>{st.activeLabel || '—'}</span>
                                      {st.activeCount > 1 && (
                                        <span className="rounded-full bg-sky-50 px-1.5 py-0.5 text-[11px] font-medium text-sky-700">
                                          {st.activeCount} ta fan
                                        </span>
                                      )}
                                    </span>
                                  ) : (
                                    <span className="text-slate-300">—</span>
                                  )}
                                </td>
                              </tr>
                            ))}
                          </tbody>
                        </table>
                      </div>
                      <div className="px-[18px] pb-[18px]">
                        <TablePagination {...pg} />
                      </div>
                    </>
                  )}
                </Card>
              )}

              {/* ---------- SABABLAR ---------- */}
              {tab === 'reasons' && (
                <Card
                  title="Sabablar"
                  sub="Chegirma NEGA berilgan. Sabab ixtiyoriy — bo'sh yozuvlar «— sababsiz —» guruhiga tushadi."
                  tight
                >
                  {data.byReason.length === 0 ? (
                    <p className="py-10 text-center text-sm text-slate-400">Ma'lumot yo'q</p>
                  ) : (
                    <div className="table-wrap">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>Sabab</th>
                            <th className="num">Nechta</th>
                            <th className="num">Summa</th>
                          </tr>
                        </thead>
                        <tbody>
                          {[...data.byReason]
                            .sort((a, b) => b.discount - a.discount)
                            .map((r) => (
                              <tr key={r.reason || '(bosh)'}>
                                <td className={cn('font-medium', r.reason ? 'text-slate-700' : 'text-slate-400')}>
                                  {r.reason || '— sababsiz —'}
                                </td>
                                <td className="num">{r.count || '—'}</td>
                                <td className="num font-semibold text-sky-600">{money(r.discount)}</td>
                              </tr>
                            ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </Card>
              )}

              {/* ---------- HOZIR AMALDAGILAR ---------- */}
              {tab === 'active' && (
                <Card
                  title="Hozir amaldagi chegirmalar"
                  sub="Registrdagi kuchda turgan yozuvlar — HAR FAN uchun alohida, davrga BOG'LIQ EMAS (joriy holat)."
                  tight
                >
                  {data.active.length === 0 ? (
                    <p className="py-10 text-center text-sm text-slate-400">
                      Hozir amalda turgan chegirma yo'q
                    </p>
                  ) : (
                    <div className="table-wrap">
                      <table className="table">
                        <thead>
                          <tr>
                            <th>O'quvchi</th>
                            <th className="num">Chegirma</th>
                            <th>Davr</th>
                            <th>Fan</th>
                            <th>Guruh</th>
                            <th>Sabab</th>
                            <th>Kim bergan</th>
                          </tr>
                        </thead>
                        <tbody>
                          {data.active.map((a) => (
                            <tr key={a.id}>
                              <td>
                                <Link
                                  to={`/admin/students/${a.studentId}`}
                                  className="font-medium text-brand-600 hover:underline"
                                >
                                  {a.studentName}
                                </Link>
                              </td>
                              <td className="num font-semibold text-sky-600">
                                {sizeLabel(a.pct, a.amount)}
                              </td>
                              <td className="whitespace-nowrap text-slate-500">
                                {periodLabel(a.startMonth, a.endMonth)}
                              </td>
                              <td className="font-medium text-slate-700">
                                {a.courseName || <span className="font-normal text-slate-400">Barcha guruhlar</span>}
                              </td>
                              <td className="text-slate-500">
                                {a.groupName || <span className="text-slate-400">Barcha guruhlar</span>}
                              </td>
                              <td className="text-slate-500">{a.reason || '—'}</td>
                              <td className="text-slate-500">{a.createdBy || '—'}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  )}
                </Card>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}
