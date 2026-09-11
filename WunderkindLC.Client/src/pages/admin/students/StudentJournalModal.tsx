import { useCallback, useEffect, useRef, useState } from 'react'
import { ChevronLeft, ChevronRight, CalendarCheck, CheckCircle2, XCircle, Clock, GraduationCap } from 'lucide-react'
import { getStudentJournal, type StudentJournal, type StudentJournalCell } from '@/api/services/studentAttendance'
import type { MasteryLevel } from '@/types'
import { formatMonth } from '@/config/constants'
import { cn, apiErrorMessage, gradeBadgeCls } from '@/lib/utils'
import { Modal } from '@/components/ui/Modal'
import { Loader } from '@/components/ui/Loader'

interface StudentJournalModalProps {
  studentId: string | null
  onClose: () => void
}

/** Mastery darajasi — rangi va yorlig'i. Nusxa: ClassDetailPage. */
function masteryDisplay(m: MasteryLevel | undefined): { label: string; cls: string } {
  switch (m) {
    case 0:
      return { label: '😴', cls: 'border-slate-100 bg-slate-50 text-slate-600' }
    case 1:
      return { label: '👂', cls: 'border-blue-100 bg-blue-50 text-blue-600' }
    case 2:
      return { label: '🙋', cls: 'border-emerald-100 bg-emerald-50 text-emerald-600' }
    case 3:
      return { label: '⭐', cls: 'border-amber-100 bg-amber-50 text-amber-600' }
    default:
      return { label: '', cls: '' }
  }
}

/** Bitta kunlik katakning ko'rinishi (rang + belgi) — guruh jurnali bilan bir xil ustuvorlik. */
function cellDisplay(c: StudentJournalCell): { cls: string; label: string } {
  if (c.blocked) return { cls: 'border-slate-100 bg-slate-50 text-slate-300', label: '' }
  if (c.grade != null) return { cls: gradeBadgeCls(c.grade), label: String(c.grade) }
  if (c.mastery != null) {
    const m = masteryDisplay(c.mastery)
    return { cls: m.cls, label: m.label }
  }
  if (c.reasonShort) {
    return c.isLate
      ? { cls: 'border-amber-100 bg-amber-50 text-amber-700', label: c.reasonShort }
      : { cls: 'border-red-100 bg-red-50 text-red-600', label: c.reasonShort }
  }
  if (c.present) return { cls: 'border-emerald-100 bg-emerald-50 text-emerald-600', label: '✓' }
  return { cls: 'border-slate-100 text-slate-300', label: '·' }
}

function cellTitle(c: StudentJournalCell): string {
  if (c.blocked) return `${c.date} · A'zolik davridan tashqarida`
  if (c.grade != null) return `${c.date} · Baho: ${c.grade}`
  if (c.reasonShort) return `${c.date} · Sababi: ${c.reasonName || c.reasonShort}`
  if (c.present) return `${c.date} · Keldi`
  if (!c.conducted) return `${c.date} · Dars o'tilmagan`
  return `${c.date} · Belgilanmagan`
}

/** O'quvchi profilida — guruh jurnalidagi O'Z QATORI, faqat o'qish uchun (tahrirlash yo'q). */
export function StudentJournalModal({ studentId, onClose }: StudentJournalModalProps) {
  const [journal, setJournal] = useState<StudentJournal | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState('')

  /**
   * SO'ROV RAQAMI — kechikib kelgan javob YANGISINI bosib ketmasin.
   * ⚠️ Bu yerda so'rov uch joydan ketadi (modal ochilishi · guruh tugmasi · oy strelkasi), ya'ni
   * javoblar TARTIBI kafolatlanmaydi: A o'quvchisining javobi B ning kataklari ustiga tushib,
   * ekranda "B ning tanlangan guruhi + A ning davomati" ko'rinishi mumkin edi.
   */
  const reqRef = useRef(0)

  const load = useCallback(
    (groupId?: string, month?: string) => {
      if (!studentId) return
      const req = ++reqRef.current
      setLoading(true)
      setError('')
      getStudentJournal(studentId, groupId, month)
        .then((j) => {
          if (reqRef.current === req) setJournal(j)
        })
        .catch((err) => {
          if (reqRef.current === req) setError(apiErrorMessage(err, "Jurnalni yuklab bo'lmadi"))
        })
        .finally(() => {
          if (reqRef.current === req) setLoading(false)
        })
    },
    [studentId],
  )

  useEffect(() => {
    if (!studentId) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda/guruh-oy o'zgarganda qayta yuklash (maqsadli)
    setJournal(null)
    load()
  }, [studentId, load])

  const monthIdx = journal ? journal.months.indexOf(journal.month) : -1
  const goMonth = (delta: number) => {
    if (!journal || monthIdx < 0) return
    const next = journal.months[monthIdx + delta]
    if (next) load(journal.groupId, next)
  }

  return (
    <Modal
      open={!!studentId}
      onClose={onClose}
      size="lg"
      title={`Jurnal${journal ? ` — ${journal.fullName}` : ''}`}
    >
      {loading && !journal ? (
        <Loader label="Yuklanmoqda..." />
      ) : error && !journal ? (
        /* Hech narsa yuklanmagan — sabab + qayta urinish (bo'sh ekran qoldirilmaydi). */
        <div className="flex flex-col items-center gap-3 py-8 text-center">
          <p className="text-sm text-red-500">{error}</p>
          <button
            type="button"
            onClick={() => load()}
            className="rounded-lg border border-slate-200 px-3 py-1.5 text-sm font-medium text-slate-600 transition-colors hover:bg-slate-50"
          >
            Qayta urinish
          </button>
        </div>
      ) : !journal || journal.groups.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">O'quvchi hech qaysi guruhda emas</p>
      ) : (
        <div className="space-y-5">
          {/* ⚠️ XATO BUTUN TANANI ALMASHTIRMAYDI: ilgari guruh tugmalari va oy strelkalari ham
              yo'qolib ketar, ya'ni foydalanuvchi boshqa oyni/guruhni ochib qayta urina olmasdi
              (oynani yopib qaytadan ochishdan boshqa yo'l qolmasdi). */}
          {error && (
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
              <span>{error}</span>
              <button
                type="button"
                onClick={() => load(journal.groupId, journal.month)}
                className="rounded-md px-2 py-1 text-xs font-semibold text-red-800 hover:bg-red-100"
              >
                Qayta urinish
              </button>
            </div>
          )}
          {/* Guruh tanlovi */}
          {journal.groups.length > 1 && (
            <div className="flex flex-wrap gap-2">
              {journal.groups.map((g) => (
                <button
                  key={g.groupId}
                  type="button"
                  onClick={() => load(g.groupId)}
                  className={cn(
                    'rounded-lg px-3 py-1.5 text-left text-sm font-medium transition-colors',
                    journal.groupId === g.groupId
                      ? 'bg-brand-600 text-white'
                      : 'bg-slate-100 text-slate-600 hover:bg-slate-200',
                  )}
                >
                  {g.groupName}
                  {g.courseName && (
                    <span
                      className={cn(
                        'ml-1.5 text-xs font-normal',
                        journal.groupId === g.groupId ? 'text-white/80' : 'text-slate-400',
                      )}
                    >
                      {g.courseName}
                    </span>
                  )}
                </button>
              ))}
            </div>
          )}

          {/* Oy navigatsiyasi */}
          <div className="flex items-center justify-center gap-3">
            <button
              type="button"
              disabled={loading || monthIdx <= 0}
              onClick={() => goMonth(-1)}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-30"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="min-w-[8rem] text-center text-sm font-semibold text-slate-700">
              {formatMonth(journal.month)}
            </span>
            <button
              type="button"
              disabled={loading || monthIdx < 0 || monthIdx >= journal.months.length - 1}
              onClick={() => goMonth(1)}
              className="rounded-lg border border-slate-200 p-1.5 text-slate-500 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-30"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>

          {/* Xulosa kartachalari */}
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-5">
            <SummaryTile icon={CalendarCheck} label="O'tilgan dars" value={journal.conducted} color="text-brand-600" bg="bg-brand-50" />
            <SummaryTile icon={CheckCircle2} label="Keldi" value={journal.attended} color="text-emerald-600" bg="bg-emerald-50" />
            <SummaryTile icon={XCircle} label="Kelmadi" value={journal.absent} color="text-red-600" bg="bg-red-50" />
            <SummaryTile icon={Clock} label="Kechikdi" value={journal.late} color="text-amber-600" bg="bg-amber-50" />
            <SummaryTile icon={GraduationCap} label="O'rtacha baho" value={journal.avgGrade || '—'} color="text-violet-600" bg="bg-violet-50" />
          </div>

          {/* Kataklar */}
          {journal.cells.length === 0 ? (
            <p className="py-8 text-center text-sm text-slate-400">Bu oyda dars yo'q</p>
          ) : (
            <div className={cn('flex flex-wrap gap-2', loading && 'opacity-50')}>
              {journal.cells.map((c) => {
                const d = cellDisplay(c)
                return (
                  <div
                    key={c.date}
                    title={cellTitle(c)}
                    className={cn(
                      'flex h-16 w-16 flex-col items-center justify-center gap-0.5 rounded-xl border p-1.5',
                      d.cls,
                    )}
                  >
                    <span className="text-[10px] font-medium text-slate-400">{c.date.slice(8, 10)}</span>
                    <span className="text-lg font-bold leading-none">{d.label}</span>
                  </div>
                )
              })}
            </div>
          )}

          {/* Izoh (legend) */}
          <div className="flex flex-wrap items-center gap-x-4 gap-y-1.5 border-t border-slate-100 pt-3 text-xs text-slate-500">
            <LegendItem cls="border-emerald-100 bg-emerald-50 text-emerald-700">5</LegendItem>
            <LegendItem cls="border-brand-100 bg-brand-50 text-brand-700">4</LegendItem>
            <LegendItem cls="border-amber-100 bg-amber-50 text-amber-700">3</LegendItem>
            <LegendItem cls="border-red-100 bg-red-50 text-red-600">2</LegendItem>
            <span className="inline-flex items-center gap-1.5">
              <span className="flex h-5 w-5 items-center justify-center rounded border border-emerald-100 bg-emerald-50 text-emerald-600">✓</span> Keldi
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="h-2.5 w-2.5 rounded bg-red-50 ring-1 ring-red-200" /> Kelmadi
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="h-2.5 w-2.5 rounded bg-amber-50 ring-1 ring-amber-200" /> Kechikdi
            </span>
            <span className="inline-flex items-center gap-1.5">
              <span className="h-2.5 w-2.5 rounded bg-slate-100 ring-1 ring-slate-200" /> A'zolik davridan tashqarida
            </span>
          </div>
        </div>
      )}
    </Modal>
  )
}

function SummaryTile({
  icon: Icon,
  label,
  value,
  color,
  bg,
}: {
  icon: typeof CalendarCheck
  label: string
  value: number | string
  color: string
  bg: string
}) {
  return (
    <div className="flex flex-col items-center gap-1.5 rounded-xl border border-slate-200 bg-white p-3 text-center">
      <span className={cn('flex h-8 w-8 items-center justify-center rounded-lg', bg, color)}>
        <Icon className="h-4 w-4" />
      </span>
      <span className="font-mono text-lg font-semibold text-slate-800">{value}</span>
      <span className="text-[11px] font-medium text-slate-400">{label}</span>
    </div>
  )
}

function LegendItem({ cls, children }: { cls: string; children: React.ReactNode }) {
  return (
    <span className="inline-flex items-center gap-1.5">
      <span className={cn('flex h-5 w-5 items-center justify-center rounded border text-[10px] font-semibold', cls)}>
        {children}
      </span>
    </span>
  )
}
