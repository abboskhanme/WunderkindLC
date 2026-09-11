import { useCallback, useEffect, useMemo, useState } from 'react'
import { Award, Download, RefreshCw, Search, Users, XCircle } from 'lucide-react'
import type { RetentionBonusFinanceReport } from '@/api/services/finance'
import { exportRetentionBonusReport, getRetentionBonusReport } from '@/api/services/finance'
import { formatMonth } from '@/config/constants'
import { cn, formatDateTime, formatMoney } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Button } from '@/components/ui/Button'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'
import { StatCard } from '@/components/ui/StatCard'
import { TablePagination, usePagination } from '@/components/ui/TablePagination'

/**
 * MOLIYA → "BONUS" — o'quvchini ushlab turish bonuslarining HISOBOTI (faqat o'qish).
 *
 * Bu yerda bonus BERILMAYDI — berish/bekor qilish "O'quvchilar → Bonus hisoboti" sahifasida.
 * Bu tab markaz egasi uchun: qaysi o'qituvchi qancha bonus oldi va qaysi oyda qancha berildi.
 *
 * DIQQAT: bonus PUL CHIQIMI EMAS — u faqat QAYD (FinanceTransaction ham, maosh yozuvi ham emas),
 * shuning uchun bu raqamlar Moliyaning kirim/chiqim xulosasiga KIRMAYDI. Haqiqiy pul o'qituvchiga
 * maosh to'lovi orqali beriladi. UI'da ham shu eslatma bir qatorda ko'rsatiladi.
 */

const control =
  'rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm font-mono text-slate-700 outline-none focus:border-brand-400'

const searchInput =
  'w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 outline-none focus:border-brand-400'

/** Joriy oy ("YYYY-MM") — standart davr oxiri. */
const currentMonth = new Date().toISOString().slice(0, 7)
/** Joriy yil boshi ("YYYY-01") — standart davr boshi (backend ham shu davrni oladi). */
const yearStartMonth = `${currentMonth.slice(0, 4)}-01`

/** "1.5" / "3" ko'rinishidagi oy soni (ortiqcha nollarsiz). */
const monthsLabel = (n: number) => String(Number(n.toFixed(2)))

/** Oy yorlig'i — bo'sh/noto'g'ri qiymatda "—" (eski yozuvda davr bo'sh bo'lishi mumkin). */
const monthLabel = (ym: string) => (/^\d{4}-\d{2}$/.test(ym) ? formatMonth(ym) : '—')

export function RetentionBonusTab() {
  const [from, setFrom] = useState(yearStartMonth)
  const [to, setTo] = useState(currentMonth)
  const [report, setReport] = useState<RetentionBonusFinanceReport | null>(null)
  const [loading, setLoading] = useState(true)
  const [exporting, setExporting] = useState(false)
  const [search, setSearch] = useState('')
  /** Jadvaldan tanlangan o'qituvchi (pastdagi ro'yxatni filtrlaydi); null = hammasi. */
  const [teacherId, setTeacherId] = useState<string | null>(null)

  const load = useCallback(() => {
    setLoading(true)
    getRetentionBonusReport(from, to)
      .then(setReport)
      .finally(() => setLoading(false))
  }, [from, to])

  // eslint-disable-next-line react-hooks/set-state-in-effect -- davr o'zgarganda qayta yuklash (sahifadagi boshqa bo'limlar bilan bir xil naqsh)
  useEffect(() => load(), [load])

  const handleExport = async () => {
    setExporting(true)
    try {
      await exportRetentionBonusReport(from, to)
    } finally {
      setExporting(false)
    }
  }

  const rows = useMemo(() => {
    const all = report?.rows ?? []
    const q = search.trim().toLowerCase()
    return all.filter((r) => {
      if (teacherId && r.teacherId !== teacherId) return false
      if (!q) return true
      return (
        r.teacherName.toLowerCase().includes(q) ||
        r.studentName.toLowerCase().includes(q) ||
        r.courseName.toLowerCase().includes(q)
      )
    })
  }, [report, search, teacherId])

  const pg = usePagination(rows)

  return (
    <div className="space-y-6">
      {/* Davr + amallar */}
      <div className="toolbar">
        <span className="text-sm font-medium text-slate-600">Davr:</span>
        <input type="month" value={from} onChange={(e) => setFrom(e.target.value)} className={control} />
        <span className="text-slate-400">—</span>
        <input type="month" value={to} onChange={(e) => setTo(e.target.value)} className={control} />
        <Button variant="secondary" onClick={load} disabled={loading}>
          <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
        </Button>
        <Button variant="secondary" onClick={handleExport} disabled={exporting || loading}>
          <Download className="h-4 w-4" /> Excel
        </Button>
      </div>

      {/* Eslatma: bu pul chiqimi emas — admin uni moliyaviy xarajat deb o'ylab qolmasin. */}
      <div className="rounded-lg border border-amber-200 bg-amber-50 px-3.5 py-2.5 text-[13px] font-medium text-amber-700">
        Bonus — pul chiqimi EMAS, faqat qayd. U Moliyaning kirim/chiqim raqamlariga qo'shilmaydi;
        haqiqiy pul o'qituvchiga maosh to'lovi orqali beriladi.
      </div>

      {loading || !report ? (
        <Loader label="Yuklanmoqda..." />
      ) : (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-4">
            <StatCard label="Jami berilgan" value={formatMoney(report.total)} icon={Award} />
            <StatCard
              label="Bonuslar soni"
              value={String(report.count)}
              icon={Award}
              iconBg="bg-emerald-50"
              iconColor="text-emerald-600"
            />
            <StatCard
              label="O'qituvchilar"
              value={String(report.byTeacher.length)}
              icon={Users}
              iconBg="bg-sky-50"
              iconColor="text-sky-600"
            />
            <StatCard
              label="Bekor qilingan"
              value={formatMoney(report.cancelledTotal)}
              icon={XCircle}
              iconBg="bg-red-50"
              iconColor="text-red-600"
              hint={`${report.cancelledCount} ta — jamiga kirmaydi`}
            />
          </div>

          <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
            {/* O'qituvchilar kesimi — qatorni bosish pastdagi ro'yxatni filtrlaydi */}
            <Card
              tight
              title="O'qituvchilar kesimi"
              sub="Qatorni bosing — pastdagi batafsil ro'yxat shu o'qituvchi bo'yicha filtrlanadi"
              actions={
                teacherId && (
                  <Button variant="ghost" onClick={() => setTeacherId(null)}>
                    Filtrni tozalash
                  </Button>
                )
              }
            >
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>O'qituvchi</th>
                      <th className="num">Bonuslar soni</th>
                      <th className="num">Jami summa</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.byTeacher.map((t) => (
                      <tr
                        key={t.teacherId}
                        onClick={() => setTeacherId(teacherId === t.teacherId ? null : t.teacherId)}
                        className={cn(
                          'cursor-pointer',
                          teacherId === t.teacherId && 'bg-brand-50/60',
                        )}
                      >
                        <td className="font-medium text-slate-800">{t.teacherName || '—'}</td>
                        <td className="num text-slate-600">{t.count}</td>
                        <td className="num font-semibold text-slate-800">{formatMoney(t.total)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {report.byTeacher.length === 0 && (
                <div className="state">
                  <div className="state-icon">
                    <Award className="h-5 w-5" />
                  </div>
                  <h4>Bonus yo'q</h4>
                  <p>Tanlangan davrda hech kimga bonus berilmagan.</p>
                </div>
              )}
            </Card>

            {/* Oylar kesimi — qaysi oyda qancha bonus berildi */}
            <Card tight title="Oylar kesimi" sub="Bonus BERILGAN oy bo'yicha (eng yangisi tepada)">
              <div className="table-wrap">
                <table className="table">
                  <thead>
                    <tr>
                      <th>Oy</th>
                      <th className="num">Soni</th>
                      <th className="num">Summa</th>
                    </tr>
                  </thead>
                  <tbody>
                    {report.byMonth.map((m) => (
                      <tr key={m.month}>
                        <td className="font-medium text-slate-800">{monthLabel(m.month)}</td>
                        <td className="num text-slate-600">{m.count}</td>
                        <td className="num font-semibold text-slate-800">{formatMoney(m.total)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              {report.byMonth.length === 0 && (
                <div className="state">
                  <div className="state-icon">
                    <Award className="h-5 w-5" />
                  </div>
                  <h4>Bonus yo'q</h4>
                  <p>Tanlangan davrda bonus berilmagan.</p>
                </div>
              )}
            </Card>
          </div>

          {/* Batafsil ro'yxat — har ulush bitta qator (bitta bonus bir necha o'qituvchiga bo'linishi mumkin) */}
          <Card
            tight
            title="Batafsil ro'yxat"
            sub="Har qator — bitta o'qituvchining ulushi (bitta bonus bir necha o'qituvchiga bo'linishi mumkin)"
            actions={
              <div className="relative w-56">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder="O'qituvchi / o'quvchi / fan"
                  className={searchInput}
                />
              </div>
            }
          >
            <div className="table-wrap">
              <table className="table">
                <thead>
                  <tr>
                    <th>Berilgan sana</th>
                    <th>O'qituvchi</th>
                    <th>O'quvchi</th>
                    <th>Fan</th>
                    <th>Davr</th>
                    <th className="num">Oy</th>
                    <th className="num">Summa</th>
                    <th>Holat</th>
                  </tr>
                </thead>
                <tbody>
                  {pg.paged.map((r) => {
                    const cancelled = r.status === 'cancelled'
                    return (
                      <tr
                        key={`${r.awardId}-${r.teacherId}`}
                        className={cn(cancelled && 'opacity-50')}
                      >
                        <td className="font-mono text-[12.5px] text-slate-500">
                          {formatDateTime(r.givenAt)}
                        </td>
                        <td className="font-medium text-slate-800">{r.teacherName || '—'}</td>
                        <td className="text-slate-700">{r.studentName || '—'}</td>
                        <td>
                          {r.courseName ? (
                            <Badge>{r.courseName}</Badge>
                          ) : (
                            <span className="text-slate-300">—</span>
                          )}
                        </td>
                        <td className="text-slate-600">
                          {monthLabel(r.periodFrom)} — {monthLabel(r.periodTo)}
                        </td>
                        <td className="num text-slate-600">{monthsLabel(r.months)}</td>
                        <td
                          className={cn(
                            'num font-semibold text-slate-800',
                            cancelled && 'line-through',
                          )}
                        >
                          {formatMoney(r.amount)}
                        </td>
                        <td>
                          {cancelled ? (
                            <Badge tone="red">Bekor qilingan</Badge>
                          ) : (
                            <Badge tone="green">Berilgan</Badge>
                          )}
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
            <TablePagination {...pg} />
            {rows.length === 0 && (
              <div className="state">
                <div className="state-icon">
                  <Award className="h-5 w-5" />
                </div>
                <h4>Yozuv topilmadi</h4>
                <p>
                  {report.rows.length === 0
                    ? "Tanlangan davrda bonus berilmagan. Bonus «O'quvchilar → Bonus hisoboti» sahifasida beriladi."
                    : 'Qidiruv yoki o’qituvchi filtriga mos yozuv yo’q.'}
                </p>
              </div>
            )}
          </Card>
        </>
      )}
    </div>
  )
}
