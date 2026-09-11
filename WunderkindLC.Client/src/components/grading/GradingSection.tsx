import { useCallback, useEffect, useState } from 'react'
import { Check, ClipboardCheck, ChevronLeft, ChevronRight, ChevronDown, X } from 'lucide-react'
import type { GradingBoard, SetGrade, BulkGrade } from '@/api/services/grading'
import { Loader } from '@/components/ui/Loader'
import { cn, apiErrorMessage } from '@/lib/utils'

interface Props {
  groupId: string
  fetchBoard: (groupId: string, month?: string) => Promise<GradingBoard>
  saveGrade: (req: SetGrade) => Promise<void>
  bulkGrade: (req: BulkGrade) => Promise<void>
}

const MONTHS = [
  'Yanvar', 'Fevral', 'Mart', 'Aprel', 'May', 'Iyun',
  'Iyul', 'Avgust', 'Sentabr', 'Oktabr', 'Noyabr', 'Dekabr',
]
const WD = ['Ya', 'Du', 'Se', 'Ch', 'Pa', 'Ju', 'Sh'] // 0=Yak..6=Shan
function monthLabel(m: string) {
  const [y, mm] = m.split('-')
  return `${MONTHS[Number(mm) - 1] ?? mm} ${y}`
}
function weekday(d: string) {
  const [y, m, day] = d.split('-').map(Number)
  return WD[new Date(y, m - 1, day).getDay()]
}
const TODAY = new Date().toISOString().slice(0, 10)

/**
 * Guruh BAHOLASH grid'i (umumiy — admin va o'qituvchi guruh detalida).
 * Mezonlar bo'yicha HAR DARSGA "bajardi/bajarmadi". Oy → dars sanasi (jurnaldagidek)
 * tanlanadi; mezon sarlavhasiga bosilsa BARCHAGA belgilash/belgilamaslik.
 */
export function GradingSection({ groupId, fetchBoard, saveGrade, bulkGrade }: Props) {
  const [board, setBoard] = useState<GradingBoard | null>(null)
  const [loading, setLoading] = useState(true)
  const [date, setDate] = useState('')
  const [done, setDone] = useState<Set<string>>(new Set()) // "studentId|criterionId|date"
  const [savingKey, setSavingKey] = useState<string | null>(null)
  const [menuFor, setMenuFor] = useState<string | null>(null)

  const load = useCallback(
    (month?: string) => {
      setLoading(true)
      setMenuFor(null) // Navda o'zgarganda bulk menyu yopiladi
      fetchBoard(groupId, month)
        .then((b) => {
          setBoard(b)
          setDate((d) =>
            b.dates.includes(d) ? d : b.dates.includes(TODAY) ? TODAY : b.dates[b.dates.length - 1] ?? '',
          )
          const s = new Set<string>()
          b.students.forEach((st) => st.doneKeys.forEach((k) => s.add(`${st.studentId}|${k}`)))
          setDone(s)
        })
        .finally(() => setLoading(false))
    },
    [groupId, fetchBoard],
  )

  // eslint-disable-next-line react-hooks/exhaustive-deps -- guruh almashganda qayta yuklash
  useEffect(() => load(undefined), [groupId])

  const toggle = async (sid: string, cid: string) => {
    if (!date) return
    const key = `${sid}|${cid}|${date}`
    const next = !done.has(key)
    setDone((s) => {
      const n = new Set(s)
      if (next) n.add(key)
      else n.delete(key)
      return n
    })
    setSavingKey(key)
    try {
      await saveGrade({ groupId, studentId: sid, criterionId: cid, date, done: next })
    } catch (err) {
      // Saqlanmadi (masalan, a'zolik boshlanishidan oldingi sana) — optimistik belgini qaytaramiz.
      setDone((s) => {
        const n = new Set(s)
        if (next) n.delete(key)
        else n.add(key)
        return n
      })
      alert(apiErrorMessage(err, 'Belgilab bo\'lmadi'))
    } finally {
      setSavingKey(null)
    }
  }

  const applyBulk = async (cid: string, value: boolean) => {
    setMenuFor(null)
    if (!date || !board) return
    setDone((s) => {
      const n = new Set(s)
      board.students.forEach((st) => {
        // A'zoligi shu sanadan keyin boshlangan o'quvchi bulk'da ham belgilanmaydi (backend ham o'tkazib yuboradi).
        if (st.memberStart && date < st.memberStart) return
        const key = `${st.studentId}|${cid}|${date}`
        if (value) n.add(key)
        else n.delete(key)
      })
      return n
    })
    await bulkGrade({ groupId, criterionId: cid, date, done: value })
  }

  if (loading) return <Loader label="Yuklanmoqda..." />
  if (!board || board.criteria.length === 0)
    return (
      <div className="flex flex-col items-center gap-2 py-8 text-center">
        <ClipboardCheck className="h-7 w-7 text-slate-300" />
        <p className="text-sm font-medium text-slate-500">Baholash mezoni biriktirilmagan</p>
        <p className="text-xs text-slate-400">
          O'quv bo'limi → Baholash mezonlari bo'limidan bu guruhga mezon biriktiring.
        </p>
      </div>
    )

  const idx = board.months.indexOf(board.month)
  const gotoMonth = (i: number) => {
    if (i >= 0 && i < board.months.length) load(board.months[i])
  }

  return (
    <div className="space-y-3">
      {/* Oy navigatsiyasi */}
      <div className="flex items-center justify-center gap-2">
        <button
          type="button"
          onClick={() => gotoMonth(idx - 1)}
          disabled={idx <= 0}
          className="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-50 disabled:opacity-40"
        >
          <ChevronLeft className="h-4 w-4" />
        </button>
        <span className="min-w-[120px] text-center text-sm font-semibold text-slate-700">{monthLabel(board.month)}</span>
        <button
          type="button"
          onClick={() => gotoMonth(idx + 1)}
          disabled={idx >= board.months.length - 1}
          className="rounded-lg border border-slate-200 p-1.5 text-slate-500 hover:bg-slate-50 disabled:opacity-40"
        >
          <ChevronRight className="h-4 w-4" />
        </button>
      </div>

      {board.dates.length === 0 ? (
        <p className="py-6 text-center text-sm text-slate-400">Bu oyda dars kuni yo'q.</p>
      ) : (
        <>
          {/* Dars sanalari — jurnaldagidek (hafta kuni + sana) */}
          <div className="flex gap-1.5 overflow-x-auto pb-1">
            {board.dates.map((d) => {
              const day = Number(d.slice(8, 10))
              const sel = d === date
              const isToday = d === TODAY
              return (
                <button
                  key={d}
                  type="button"
                  onClick={() => setDate(d)}
                  className={cn(
                    'flex h-12 w-11 shrink-0 flex-col items-center justify-center rounded-xl border transition-colors',
                    sel
                      ? 'border-slate-800 bg-slate-800 text-white'
                      : isToday
                        ? 'border-teal-300 bg-teal-50 text-teal-700'
                        : 'border-slate-200 bg-white text-slate-600 hover:bg-slate-50',
                  )}
                >
                  <span className={cn('text-[10px] font-medium', sel ? 'text-white/80' : 'text-slate-400')}>
                    {weekday(d)}
                  </span>
                  <span className="text-[15px] font-bold">{day}</span>
                </button>
              )
            })}
          </div>

          {board.students.length === 0 ? (
            <p className="py-6 text-center text-sm text-slate-400">Guruhda faol o'quvchi yo'q.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full border-collapse text-sm">
                <thead>
                  <tr className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500">
                    <th className="sticky left-0 z-10 border-b border-r border-slate-200 bg-slate-50 px-3 py-2 text-left">
                      O'quvchi
                    </th>
                    {board.criteria.map((c) => (
                      <th key={c.id} className="relative min-w-[96px] border-b border-slate-200 px-2 py-2 text-center">
                        <button
                          type="button"
                          onClick={() => setMenuFor(menuFor === c.id ? null : c.id)}
                          title="Hammaga belgilash / belgilamaslik"
                          className="mx-auto inline-flex items-center gap-1 font-semibold text-slate-700 hover:text-slate-900"
                        >
                          {c.name}
                          <ChevronDown className="h-3.5 w-3.5 text-slate-400" />
                        </button>
                        {menuFor === c.id && (
                          <div className="absolute left-1/2 top-full z-30 mt-1 w-48 -translate-x-1/2 overflow-hidden rounded-xl border border-slate-200 bg-white text-left shadow-lg">
                            <button
                              type="button"
                              onClick={() => applyBulk(c.id, true)}
                              className="flex w-full items-center gap-2 px-3 py-2.5 text-[13px] font-medium normal-case text-slate-700 hover:bg-emerald-50"
                            >
                              <Check className="h-4 w-4 text-emerald-600" /> Hammaga belgilash
                            </button>
                            <button
                              type="button"
                              onClick={() => applyBulk(c.id, false)}
                              className="flex w-full items-center gap-2 border-t border-slate-100 px-3 py-2.5 text-[13px] font-medium normal-case text-slate-700 hover:bg-red-50"
                            >
                              <X className="h-4 w-4 text-red-500" /> Belgilamaslik
                            </button>
                          </div>
                        )}
                      </th>
                    ))}
                    <th className="sticky right-0 z-10 border-b border-l border-slate-200 bg-slate-50 px-3 py-2 text-center font-semibold text-slate-600">
                      Jami
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {board.students.map((s, i) => {
                    const totalDone = board.criteria.reduce((count, c) => {
                      const key = `${s.studentId}|${c.id}|${date}`
                      return count + (done.has(key) ? 1 : 0)
                    }, 0)
                    return (
                      <tr key={s.studentId} className={i % 2 ? 'bg-slate-50/40' : ''}>
                        <td className="sticky left-0 z-10 border-b border-r border-slate-200 bg-inherit px-3 py-2 font-medium text-slate-700">
                          {s.fullName}
                        </td>
                        {board.criteria.map((c) => {
                          const key = `${s.studentId}|${c.id}|${date}`
                          const isDone = done.has(key)
                          // Jurnaldagi kabi: a'zolik boshlanishidan (aktivlashtirilgan bo'lsa ActivatedAt,
                          // aks holda JoinedAt) OLDINGI sanaga mezon belgilab bo'lmaydi.
                          const beforeMember = !!s.memberStart && date < s.memberStart
                          return (
                            <td key={c.id} className="border-b border-slate-100 px-2 py-1.5 text-center">
                              <button
                                type="button"
                                disabled={beforeMember}
                                onClick={() => toggle(s.studentId, c.id)}
                                title={
                                  beforeMember
                                    ? "O'quvchi guruhga qo'shilishidan oldingi dars"
                                    : isDone
                                      ? 'Bajardi'
                                      : 'Belgilash'
                                }
                                className={cn(
                                  'mx-auto flex h-7 w-7 items-center justify-center rounded-lg border transition-colors',
                                  beforeMember
                                    ? 'cursor-not-allowed border-slate-200 bg-slate-50 text-transparent'
                                    : isDone
                                      ? 'border-emerald-500 bg-emerald-500 text-white'
                                      : 'border-slate-300 bg-white text-transparent hover:border-emerald-400',
                                  savingKey === key && 'ring-2 ring-emerald-200',
                                )}
                              >
                                <Check className="h-4 w-4" />
                              </button>
                            </td>
                          )
                        })}
                        <td className="sticky right-0 z-10 border-b border-l border-slate-100 bg-inherit px-3 py-2 text-center">
                          <span className={cn('inline-flex h-7 w-7 items-center justify-center rounded-lg font-semibold text-slate-600', totalDone > 0 ? 'bg-violet-100 text-violet-700' : 'text-slate-400')}>
                            {totalDone || '—'}
                          </span>
                        </td>
                      </tr>
                    )
                  })}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      {/* Menyuni yopish uchun fon */}
      {menuFor && <div className="fixed inset-0 z-20" onClick={() => setMenuFor(null)} />}
    </div>
  )
}
