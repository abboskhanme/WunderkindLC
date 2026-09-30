import { useEffect, useMemo, useState } from 'react'
import { Bar, BarChart, CartesianGrid, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts'
import { GraduationCap, RefreshCw, UserCheck, Users } from 'lucide-react'
import type { Student } from '@/types'
import { getStudents } from '@/api/services/students'
import { Card } from '@/components/ui/Card'
import { StatCard } from '@/components/ui/StatCard'
import { Loader } from '@/components/ui/Loader'
import { Button } from '@/components/ui/Button'
import { PageHeader } from '@/components/ui/PageHeader'
import { schoolGradeDistribution, SCHOOL_GRADE_GRADUATED } from '@/lib/schoolGrades'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * SINFLAR KESIMI — "markazda maktabning nechanchi sinflari o'qiyapti".
 *
 * Joriy sinf SERVERDA hisoblanadi (`Student.schoolGradeNow` — har yili 1-sentabrda o'zi oshadi),
 * bu sahifa faqat sanaydi va chizadi. Manba — o'quvchilar ro'yxatining O'ZI (`GET /admin/students`,
 * arxivsiz), ya'ni sonlar «O'quvchilar» sahifasidagi «Sinf» filtri bilan bir xil chiqadi.
 */

/** Yakka seriya rangi — loyihada tekshirilgan (kurslar analitikasi / chegirmalar bilan bir xil). */
const C_STUDENTS = '#6366f1'
const axisTick = { fontSize: 12, fill: '#94a3b8' }
const tooltipStyle = { borderRadius: 12, border: '1px solid #e2e8f0', fontSize: 13 }
const gridStroke = '#eef0f4'

/** Kimlar sanaladi. Standart — AKTIV: savol "kim haqiqatan o'qiyapti". */
type Scope = 'active' | 'studying' | 'all'

const SCOPES: { key: Scope; label: string; hint: string }[] = [
  { key: 'active', label: 'Aktiv', hint: "Kamida bitta guruhda aktiv o'qiyotganlar" },
  { key: 'studying', label: 'Aktiv + sinovda', hint: "Sinov darsidagilar ham qo'shiladi" },
  { key: 'all', label: 'Hammasi', hint: "Arxivdan tashqari barcha o'quvchilar (muzlatilgan va guruhsiz ham)" },
]

function inScope(s: Student, scope: Scope): boolean {
  if (scope === 'all') return true
  const active = s.active || s.memberState === 'active'
  return scope === 'active' ? active : active || s.memberState === 'trial'
}

const pct = (n: number, total: number) => (total > 0 ? `${Math.round((n / total) * 100)}%` : '—')

export function SchoolGradesReportPage() {
  const [students, setStudents] = useState<Student[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [tick, setTick] = useState(0)
  const [scope, setScope] = useState<Scope>('active')

  useEffect(() => {
    let alive = true
    getStudents()
      .then((list) => {
        if (!alive) return
        setStudents(list)
        setError(null)
      })
      // Sahifaning ASOSIY ma'lumoti — xato ko'rsatiladi (`error-visibility.md` §4).
      .catch((e) => alive && setError(apiErrorMessage(e, "O'quvchilar ro'yxatini yuklab bo'lmadi")))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [tick])

  const scopeCounts = useMemo(
    () => Object.fromEntries(SCOPES.map((o) => [o.key, students.filter((s) => inScope(s, o.key)).length])) as Record<Scope, number>,
    [students],
  )
  const rows = useMemo(
    () => schoolGradeDistribution(students.filter((s) => inScope(s, scope)).map((s) => s.schoolGradeNow)),
    [students, scope],
  )
  const total = rows.reduce((sum, r) => sum + r.count, 0)
  const unknown = rows.find((r) => r.grade == null)?.count ?? 0
  const known = total - unknown
  const top = rows.filter((r) => r.grade != null && r.count > 0).sort((a, b) => b.count - a.count)[0]
  // Grafikda faqat sinflar (1..11, bitirgan): "kiritilmagan" o'lchov emas, to'liqlik ko'rsatkichi —
  // u kartochka va jadvalda turadi, ustun bo'lib chizilsa sinflarni ezib qo'yardi.
  const chartData = rows
    .filter((r) => r.grade != null)
    .map((r) => ({ name: r.grade === SCHOOL_GRADE_GRADUATED ? 'Bitirgan' : String(r.grade), label: r.label, count: r.count }))

  return (
    <div>
      <PageHeader
        title="Sinflar kesimi"
        sub="O'quvchilar maktabda nechanchi sinfda o'qiydi. Sinf o'quvchi profilida kiritiladi va har yili 1-sentabrda avtomatik oshadi."
        actions={
          <Button variant="secondary" onClick={() => setTick((x) => x + 1)} disabled={loading}>
            <RefreshCw className={cn('h-4 w-4', loading && 'animate-spin')} /> Yangilash
          </Button>
        }
      />

      <div className="mb-4 flex flex-wrap gap-1.5">
        {SCOPES.map((o) => (
          <button
            key={o.key}
            type="button"
            title={o.hint}
            onClick={() => setScope(o.key)}
            className={cn(
              'rounded-full border px-3 py-1 text-xs font-medium transition-colors',
              scope === o.key
                ? 'border-brand-500 bg-brand-50 text-brand-700'
                : 'border-slate-200 text-slate-600 hover:bg-slate-50',
            )}
          >
            {o.label}
            <span className="ml-1 opacity-60">{scopeCounts[o.key] ?? 0}</span>
          </button>
        ))}
      </div>

      {error && (
        <div className="mb-4 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>
      )}

      {loading ? (
        <Card>
          <Loader label="Yuklanmoqda..." />
        </Card>
      ) : (
        <div className="space-y-4">
          <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
            <StatCard label="O'quvchilar" value={String(total)} icon={Users} hint={SCOPES.find((o) => o.key === scope)?.hint} />
            <StatCard
              label="Sinfi kiritilgan"
              value={`${known} · ${pct(known, total)}`}
              icon={UserCheck}
              hint={unknown > 0 ? `${unknown} ta o'quvchida sinf kiritilmagan` : 'Hammasida kiritilgan'}
            />
            <StatCard
              label="Eng ko'p"
              value={top ? top.label : '—'}
              icon={GraduationCap}
              hint={top ? `${top.count} ta o'quvchi · ${pct(top.count, known)}` : "Hali sinf kiritilmagan"}
            />
          </div>

          <Card>
            <h3 className="mb-1 font-semibold text-slate-800">Sinflar bo'yicha o'quvchilar soni</h3>
            <p className="mb-4 text-xs text-slate-500">
              Faqat sinfi kiritilgan o'quvchilar ({known} ta). Kiritilmaganlar — pastdagi jadvalda.
            </p>
            {known === 0 ? (
              <p className="py-10 text-center text-sm text-slate-400">
                Hali hech bir o'quvchida sinf kiritilmagan. O'quvchi qo'shishda yoki profilidagi «Tahrirlash»da
                «Sinf» maydonini to'ldiring.
              </p>
            ) : (
              <ResponsiveContainer width="100%" height={280}>
                <BarChart data={chartData} margin={{ top: 8, right: 8, left: -12, bottom: 0 }}>
                  <CartesianGrid stroke={gridStroke} vertical={false} />
                  <XAxis dataKey="name" tick={axisTick} axisLine={false} tickLine={false} />
                  <YAxis allowDecimals={false} tick={axisTick} axisLine={false} tickLine={false} />
                  <Tooltip
                    cursor={{ fill: '#f1f5f9' }}
                    contentStyle={tooltipStyle}
                    labelFormatter={(_, p) => (p?.[0]?.payload as { label?: string } | undefined)?.label ?? ''}
                    formatter={(v) => [`${v} ta`, "O'quvchilar"]}
                  />
                  <Bar dataKey="count" name="O'quvchilar" fill={C_STUDENTS} radius={[4, 4, 0, 0]} maxBarSize={24} />
                </BarChart>
              </ResponsiveContainer>
            )}
          </Card>

          <Card className="overflow-hidden p-0">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-slate-100 text-left text-xs uppercase text-slate-400">
                  <th className="px-4 py-2.5 font-medium">Sinf</th>
                  <th className="px-4 py-2.5 text-right font-medium">O'quvchilar</th>
                  <th className="px-4 py-2.5 text-right font-medium">Ulushi</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((r) => (
                  <tr
                    key={r.grade ?? 'none'}
                    className={cn('border-b border-slate-50 last:border-0', r.grade == null && 'bg-slate-50/60 text-slate-500')}
                  >
                    <td className="px-4 py-2">{r.label}</td>
                    <td className="px-4 py-2 text-right font-mono">{r.count}</td>
                    <td className="px-4 py-2 text-right font-mono text-slate-500">{pct(r.count, total)}</td>
                  </tr>
                ))}
                <tr className="border-t border-slate-200 font-semibold text-slate-800">
                  <td className="px-4 py-2">Jami</td>
                  <td className="px-4 py-2 text-right font-mono">{total}</td>
                  <td className="px-4 py-2 text-right font-mono text-slate-500">{total > 0 ? '100%' : '—'}</td>
                </tr>
              </tbody>
            </table>
          </Card>
        </div>
      )}
    </div>
  )
}
