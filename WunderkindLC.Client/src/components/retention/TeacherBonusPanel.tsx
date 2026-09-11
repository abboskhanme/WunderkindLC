import { Award, Hourglass } from 'lucide-react'
import type { TeacherRetentionSummary } from '@/api/services/retentionBonus'
import { formatMoney, formatDate, cn } from '@/lib/utils'
import { Card } from '@/components/ui/Card'
import { Badge } from '@/components/ui/Badge'
import { Loader } from '@/components/ui/Loader'

interface Props {
  data: TeacherRetentionSummary | null
  loading?: boolean
  /** O'qituvchi ilovasida sarlavha boshqacha bo'ladi. */
  title?: string
}

/**
 * O'QITUVCHINING USHLAB TURISH BONUSLARI — admin profilidagi «Bonus» tabi va o'qituvchi
 * ilovasidagi maosh sahifasi AYNAN shu komponentni ishlatadi (raqamlar ikki joyda ikki xil
 * ko'rinmasin).
 *
 * Ikki bo'lim: YO'LDAGILAR (oylari to'planayotgan (o'quvchi × fan) sikllari) va BERILGAN
 * bonuslar. Sikl har FAN uchun alohida yuritiladi.
 *
 * DIQQAT: bu summalar maosh (`SalaryLedger`) raqamlariga QO'SHILMAGAN — bonus alohida qayd.
 * Pul odatdagi maosh to'lovi orqali beriladi.
 */
export function TeacherBonusPanel({ data, loading, title = 'Ushlab turish bonuslari' }: Props) {
  if (loading) return <Loader />

  const items = data?.items ?? []
  const inProgress = data?.inProgress ?? []

  return (
    <Card title={title} className="p-0">
      <div className="flex flex-wrap items-center gap-6 border-b border-slate-100 px-4 py-3">
        <div>
          <div className="text-xs uppercase tracking-wide text-slate-400">Jami bonus</div>
          <div className="text-xl font-bold text-emerald-600">{formatMoney(data?.total ?? 0)}</div>
        </div>
        <div>
          <div className="text-xs uppercase tracking-wide text-slate-400">Bonuslar soni</div>
          <div className="text-xl font-bold text-slate-800">{data?.count ?? 0}</div>
        </div>
        <div>
          <div className="text-xs uppercase tracking-wide text-slate-400">Yo'lda</div>
          <div className="text-xl font-bold text-slate-800">{inProgress.length}</div>
        </div>
        <p className="ml-auto max-w-md text-xs text-slate-400">
          Bonus o'quvchini uzoq muddat ushlab turgani uchun beriladi. Bu summa maosh
          hisob-kitobiga <b>qo'shilmagan</b> — u alohida qayd; pul odatdagi maosh to'lovi
          orqali beriladi.
        </p>
      </div>

      {/* ---------- 1. YO'LDAGILAR ---------- */}
      <div className="border-b border-slate-100">
        <div className="flex flex-wrap items-center gap-2 px-4 pt-4">
          <Hourglass className="h-4 w-4 text-slate-400" />
          <span className="text-sm font-bold text-slate-800">Yo'ldagilar</span>
          <span className="text-xs text-slate-400">
            oylari to'planayotgan (o'quvchi × fan) sikllari
          </span>
        </div>
        <p className="px-4 pt-1.5 text-xs text-slate-400">
          «Menda» ustuni — bonusdan sizga qancha <b>ULUSH</b> tegishini ko'rsatadi, «necha oy dars
          berdim» degani EMAS: faqat sanoqqa kirgan (to'langan) oylar hisoblanadi.
        </p>

        {inProgress.length === 0 ? (
          <div className="px-4 py-6 text-sm text-slate-400">
            Hozircha yo'ldagi o'quvchi yo'q — sanoq boshlangach shu yerda ko'rinadi.
          </div>
        ) : (
          <div className="mt-3 overflow-x-auto">
            <table className="w-full min-w-[720px] text-sm">
              <thead className="border-y border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
                <tr>
                  <th className="px-4 py-2.5">O'quvchi</th>
                  <th className="px-4 py-2.5">Fan</th>
                  <th className="px-4 py-2.5">Guruh</th>
                  <th className="px-4 py-2.5">Sanoq</th>
                  <th className="px-4 py-2.5 text-right">Menda</th>
                  <th className="px-4 py-2.5">Holat</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-50">
                {inProgress.map((p) => {
                  const pct =
                    p.required > 0 ? Math.min(100, Math.round((p.counted / p.required) * 100)) : 0
                  return (
                    <tr
                      key={`${p.studentId}:${p.courseId}`}
                      className={cn('hover:bg-slate-50/60', p.alreadyAwarded && 'opacity-50')}
                    >
                      <td className="px-4 py-3 font-medium text-slate-800">
                        {p.studentName}
                        {p.alreadyAwarded && (
                          <Badge tone="amber" className="ml-2">
                            bonus olingan
                          </Badge>
                        )}
                      </td>
                      <td className="px-4 py-3 text-slate-600">{p.courseName || '—'}</td>
                      <td className="px-4 py-3 text-slate-500">{p.groupNames || '—'}</td>
                      <td className="px-4 py-3">
                        <div className="font-mono text-xs text-slate-600">
                          {p.counted}/{p.required}
                        </div>
                        <div className="mt-1 h-1.5 w-24 overflow-hidden rounded-full bg-slate-100">
                          <div
                            className={cn(
                              'h-full rounded-full',
                              pct >= 100 ? 'bg-emerald-500' : 'bg-brand-500',
                            )}
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      </td>
                      <td className="px-4 py-3 text-right font-mono text-slate-600">
                        {p.myMonths}
                      </td>
                      <td className="px-4 py-3">
                        <div className="text-xs text-slate-500">{p.statusNote}</div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ---------- 2. BERILGAN BONUSLAR ---------- */}
      <div className="flex items-center gap-2 px-4 pt-4">
        <Award className="h-4 w-4 text-slate-400" />
        <span className="text-sm font-bold text-slate-800">Berilgan bonuslar</span>
      </div>

      {items.length === 0 ? (
        <div className="flex flex-col items-center gap-2 px-4 py-10 text-center text-sm text-slate-400">
          <Award className="h-8 w-8 text-slate-300" />
          Hali bonus berilmagan.
        </div>
      ) : (
        <div className="mt-3 overflow-x-auto">
          <table className="w-full min-w-[720px] text-sm">
            <thead className="border-y border-slate-100 text-left text-xs uppercase tracking-wide text-slate-400">
              <tr>
                <th className="px-4 py-3">O'quvchi</th>
                <th className="px-4 py-3">Fan</th>
                <th className="px-4 py-3">Davr</th>
                <th className="px-4 py-3 text-right">Oy</th>
                <th className="px-4 py-3 text-right">Summa</th>
                <th className="px-4 py-3">Berilgan</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-50">
              {items.map((x) => (
                <tr
                  key={x.awardId}
                  className={cn('hover:bg-slate-50/60', x.status === 'cancelled' && 'opacity-50')}
                >
                  <td className="px-4 py-3 font-medium text-slate-800">
                    {x.studentName}
                    {x.status === 'cancelled' && (
                      <Badge tone="red" className="ml-2">
                        bekor qilingan
                      </Badge>
                    )}
                  </td>
                  <td className="px-4 py-3 text-slate-600">{x.courseName || '—'}</td>
                  <td className="px-4 py-3 text-slate-500">
                    {x.periodFrom} … {x.periodTo}
                  </td>
                  <td className="px-4 py-3 text-right font-mono text-slate-600">{x.months}</td>
                  <td
                    className={cn(
                      'px-4 py-3 text-right font-semibold',
                      x.status === 'cancelled' ? 'text-slate-400 line-through' : 'text-emerald-600',
                    )}
                  >
                    {formatMoney(x.amount)}
                  </td>
                  <td className="px-4 py-3 text-xs text-slate-400">
                    {formatDate(x.givenAt)}
                    {x.givenBy ? ` · ${x.givenBy}` : ''}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </Card>
  )
}
