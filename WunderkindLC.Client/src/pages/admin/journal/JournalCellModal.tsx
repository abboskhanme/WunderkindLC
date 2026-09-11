import { useEffect, useState } from 'react'
import type { AbsenceReason, JournalEntry, MasteryLevel } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  studentName: string
  dateLabel: string
  date?: string
  startDate?: string
  entry: JournalEntry | null
  reasons: AbsenceReason[]
  /** Saqlash/tozalash muvaffaqiyatsiz bo'lsa (masalan Jurnal boshqaruvi siyosati taqig'i) — shu yerda
   *  chiroyli banner sifatida ko'rsatiladi (brauzerning standart alert() o'rniga). */
  error?: string | null
  onClose: () => void
  /** Baho, davomat (keldi/sabab), uyga vazifa (0/1/2/3), xulq (0/1/2) va o'zlashtirishni birga saqlaydi.
   *  present — ANIQ "keldi (bor)": orqaga sanalgan a'zolikda ham katak yashil ✓ bo'ladi. */
  onSave: (
    grade: number | null,
    reasonId: string | null,
    homework: number,
    behavior: number,
    mastery: MasteryLevel | null,
    present: boolean,
  ) => void
  onClear: () => void
}

const grades = [1, 2, 3, 4, 5]

const masteryOptions = [
  { value: 0, label: 'Non-Reactive', desc: 'Passiv, qayd qilmaydi' },
  { value: 1, label: 'Reactive', desc: 'Javobi beradi, undama kerak' },
  { value: 2, label: 'Active', desc: 'Faol qatnashadi' },
  { value: 3, label: 'Pro-Active', desc: "Kuzatuvchi, o'zini urganadi" },
]

export function JournalCellModal({
  open,
  studentName,
  dateLabel,
  date,
  startDate,
  entry,
  reasons,
  error,
  onClose,
  onSave,
  onClear,
}: Props) {
  const [grade, setGrade] = useState<number | null>(null)
  const [reasonId, setReasonId] = useState<string | null>(null)
  const [present, setPresent] = useState(false)
  const [homework, setHomework] = useState(0)
  const [behavior, setBehavior] = useState(0)
  const [mastery, setMastery] = useState<MasteryLevel | ''>('')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda joriy katak qiymatini yuklash (maqsadli)
    setGrade(entry?.grade ?? null)
    setReasonId(entry?.reasonId ?? null)
    setPresent(entry?.present ?? false)
    setHomework(entry?.homework ?? 0)
    setBehavior(entry?.behavior ?? 0)
    setMastery(entry?.mastery ?? '')
  }, [open, entry])

  const toggle = (cur: number, set: (v: number) => void, val: number) =>
    set(cur === val ? 0 : val)

  const lateReasons = reasons.filter((r) => r.isLate)
  const absentReasons = reasons.filter((r) => !r.isLate)
  const selectedLate = reasonId != null && lateReasons.some((r) => r.id === reasonId)
  const isBeforeStart = !!(startDate && date && date < startDate)

  // "Keldi" va "kelmadi (sabab)" bir vaqtda bo'lmaydi — biri tanlansa ikkinchisi o'chadi.
  const toggleReason = (id: string) =>
    setReasonId((cur) => {
      const next = cur === id ? null : id
      if (next) setPresent(false)
      return next
    })
  const togglePresent = () =>
    setPresent((cur) => {
      if (!cur) setReasonId(null)
      return !cur
    })

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="sm"
      title={studentName}
      footer={
        <>
          {entry && (
            <Button variant="danger" className="mr-auto" onClick={onClear}>
              Tozalash
            </Button>
          )}
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button disabled={isBeforeStart} onClick={() => onSave(grade, reasonId, homework, behavior, mastery === '' ? null : mastery, present)}>
            Saqlash
          </Button>
        </>
      }
    >
      <p className="mb-4 font-mono text-sm text-slate-400">{dateLabel}</p>
      {isBeforeStart && (
        <div className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-600">
          <strong>Ogohlantirish:</strong> Sana guruh yaratilishidan oldin. Saqlab bo'lmaydi.
        </div>
      )}
      {error && (
        <div className="mb-4 rounded-lg bg-red-50 p-3 text-sm text-red-600">
          <strong>Xatolik:</strong> {error}
        </div>
      )}

      <div className="space-y-4">
        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Baho</p>
          <div className="flex gap-2">
            {grades.map((g) => (
              <button
                key={g}
                type="button"
                onClick={() => setGrade((cur) => (cur === g ? null : g))}
                className={cn(
                  'flex h-11 w-11 items-center justify-center rounded-xl border font-mono text-base font-bold transition-all',
                  grade === g
                    ? 'border-brand-500 bg-brand-600 text-white'
                    : 'border-slate-200 text-slate-700 hover:border-brand-300 hover:bg-slate-50',
                )}
              >
                {g}
              </button>
            ))}
          </div>
        </div>

        {lateReasons.length > 0 && (
          <div>
            <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Kech keldi</p>
            <div className="flex flex-wrap gap-2">
              {lateReasons.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => toggleReason(r.id)}
                  className={cn(
                    'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                    reasonId === r.id
                      ? 'border-amber-400 bg-amber-50 text-amber-700'
                      : 'border-slate-200 text-slate-700 hover:bg-slate-50',
                  )}
                >
                  {r.name}
                </button>
              ))}
            </div>
            {selectedLate && (
              <p className="mt-1.5 text-xs text-amber-600">
                Kech kelgan - darsda qatnashgan, baho ham qo'yishingiz mumkin.
              </p>
            )}
          </div>
        )}

        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Davomat</p>
          <div className="mb-2">
            <button
              type="button"
              onClick={togglePresent}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                present
                  ? 'border-emerald-400 bg-emerald-50 text-emerald-700'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              ✓ Keldi (bor)
            </button>
          </div>
          {absentReasons.length === 0 ? (
            <p className="text-xs text-slate-400">Sabablar yo'q — O'quv bo'limi → Sabablar bo'limida qo'shing</p>
          ) : (
            <div className="flex flex-wrap gap-2">
              {absentReasons.map((r) => (
                <button
                  key={r.id}
                  type="button"
                  onClick={() => toggleReason(r.id)}
                  className={cn(
                    'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                    reasonId === r.id
                      ? 'border-red-400 bg-red-50 text-red-600'
                      : 'border-slate-200 text-slate-700 hover:bg-slate-50',
                  )}
                >
                  {r.name}
                </button>
              ))}
            </div>
          )}
        </div>

        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Uyga vazifa</p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => toggle(homework, setHomework, 1)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                homework === 1
                  ? 'border-emerald-400 bg-emerald-50 text-emerald-700'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Qildi
            </button>
            <button
              type="button"
              onClick={() => toggle(homework, setHomework, 3)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                homework === 3
                  ? 'border-amber-400 bg-amber-50 text-amber-700'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Chala qildi
            </button>
            <button
              type="button"
              onClick={() => toggle(homework, setHomework, 2)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                homework === 2
                  ? 'border-red-400 bg-red-50 text-red-600'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Qilmadi
            </button>
          </div>
        </div>

        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Xulq</p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={() => toggle(behavior, setBehavior, 1)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                behavior === 1
                  ? 'border-emerald-400 bg-emerald-50 text-emerald-700'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Yaxshi
            </button>
            <button
              type="button"
              onClick={() => toggle(behavior, setBehavior, 2)}
              className={cn(
                'rounded-lg border px-3 py-2 text-sm font-semibold transition-colors',
                behavior === 2
                  ? 'border-red-400 bg-red-50 text-red-600'
                  : 'border-slate-200 text-slate-700 hover:bg-slate-50',
              )}
            >
              Yomon
            </button>
          </div>
        </div>

        <div>
          <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-400">Darsga munosabat</p>
          <div className="space-y-2">
            {masteryOptions.map((opt) => (
              <label
                key={opt.value}
                className="flex cursor-pointer items-center gap-3 rounded-lg border border-slate-200 p-3 transition-colors hover:bg-slate-50"
              >
                <input
                  type="radio"
                  name="mastery"
                  value={opt.value}
                  checked={mastery === opt.value}
                  onChange={() => setMastery(opt.value as MasteryLevel)}
                  className="cursor-pointer"
                />
                <div className="flex-1">
                  <div className="font-semibold text-slate-700">{opt.label}</div>
                  <div className="text-xs text-slate-500">{opt.desc}</div>
                </div>
              </label>
            ))}

            {mastery !== '' && (
              <button
                type="button"
                onClick={() => setMastery('')}
                className="mt-2 text-xs text-slate-400 hover:text-slate-600"
              >
                Tozalash
              </button>
            )}
          </div>
        </div>
      </div>
    </Modal>
  )
}