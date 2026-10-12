import { useEffect, useImperativeHandle, useMemo, useRef, useState, type Ref } from 'react'
import { Check, Loader2 } from 'lucide-react'
import type { TestResultDetail, TestScoreBatchResult, TestScoreBatchRow } from '@/types'
import { NumberInput } from '@/components/ui/NumberInput'
import { apiErrorMessage, cn } from '@/lib/utils'
import { fmtScore } from '@/lib/ratingPeriods'
import {
  AUTOSAVE_MS,
  cellError,
  livePercent,
  maxSolved,
  nextRowIndex,
  parsePastedColumn,
  pastedError,
} from '@/lib/testScoreGrid'

/** Ota komponent sertifikat/yuborishdan OLDIN kutib turadigan saqlash (navbatdagi o'zgarishlar ham). */
export interface TestScoreGridHandle {
  flush: () => Promise<void>
}

type Status = { kind: 'idle' } | { kind: 'saving' } | { kind: 'saved' } | { kind: 'error'; text: string }

const MEDALS = ['🥇', '🥈', '🥉']

/**
 * TEST NATIJALARI JADVALI — Excel'dek kiritish (CRM U4a, docs/SPEC-crm-upgrade.md P4.2). Admin
 * (`TestDetailPage`) ham, o'qituvchi (`TeacherGroupTestsPanel`) ham SHU komponentni ishlatadi — farq faqat
 * `save` (qaysi endpoint) va `editable`.
 *
 * - Qatorlar ISM bo'yicha va yozish paytida QAYTA SARALANMAYDI (o'rin — oxirgi saqlashdan).
 * - Enter/↓ — keyingi qator, Shift+Enter/↑ — oldingi qator.
 * - Excel ustunini qo'yish: shu qatordan PASTGA to'ldiradi (`"25/30"` → 25, bo'sh — tozalaydi).
 * - Noto'g'ri katak QIZIL (sababi sarlavhada) va yuborilmaydi; server rad etgani ham qizil (qisilmaydi).
 * - Avtosaqlash: oxirgi tahrirdan 800 ms keyin YOKI jadvaldan chiqilganda — bitta so'rov (batch).
 */
export function TestScoreGrid({
  detail,
  editable,
  save,
  onDetail,
  ref,
}: {
  detail: TestResultDetail
  editable: boolean
  /** Bir nechta katakni saqlash (admin yoki o'qituvchi endpointi). */
  save: (rows: TestScoreBatchRow[]) => Promise<TestScoreBatchResult>
  /** Saqlangach yangi tafsilot (o'rin, foiz, yuborilgan vaqt) — ota komponentga. */
  onDetail: (d: TestResultDetail) => void
  ref?: Ref<TestScoreGridHandle>
}) {
  const questions = detail.questionCount ?? 0
  const max = maxSolved(questions, detail.maxScore)
  const denominator = questions > 0 ? questions : detail.maxScore

  const byId = useMemo(() => new Map(detail.rows.map((r) => [r.studentId, r])), [detail.rows])
  // Tartib BIR MARTA (ism bo'yicha) — yozish paytida qator joyidan sakramasin.
  const [order] = useState(() =>
    [...detail.rows].sort((a, b) => a.fullName.localeCompare(b.fullName, 'uz')).map((r) => r.studentId),
  )
  const ids = useMemo(() => {
    const known = new Set(order)
    const extra = detail.rows
      .filter((r) => !known.has(r.studentId))
      .sort((a, b) => a.fullName.localeCompare(b.fullName, 'uz'))
      .map((r) => r.studentId)
    return [...order.filter((id) => byId.has(id)), ...extra]
  }, [order, detail.rows, byId])

  const [values, setValues] = useState<Record<string, number | null>>(() =>
    Object.fromEntries(detail.rows.map((r) => [r.studentId, r.score])),
  )
  /** Server yoki qo'yish (paste) xatolari; oddiy diapazon xatosi qiymatdan jonli hisoblanadi. */
  const [errors, setErrors] = useState<Record<string, string>>({})
  /** Shu ochilishda tahrirlangan kataklar — faqat ularda jonli tekshiruv/foiz (serverdagi qiymatga ishonamiz). */
  const [touched, setTouched] = useState<Set<string>>(() => new Set())
  /** ESKI testdagi KASRLI ball (masalan 37.5) — faqat «✎» bosilganda tahrirlanadi (NumberInput uni
   *  yaxlitlab JIMGINA qayta saqlab yubormasin). */
  const [unlocked, setUnlocked] = useState<Set<string>>(() => new Set())
  const [status, setStatus] = useState<Status>({ kind: 'idle' })

  const box = useRef<HTMLDivElement>(null)
  const valuesRef = useRef(values)
  const dirty = useRef(new Set<string>())
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null)
  const inFlight = useRef<Promise<void> | null>(null)
  const again = useRef(false)
  const latest = useRef({ save, onDetail, max })
  useEffect(() => {
    latest.current = { save, onDetail, max }
  })

  const clearTimer = () => {
    if (timer.current) clearTimeout(timer.current)
    timer.current = null
  }

  const runSave = async (): Promise<void> => {
    if (inFlight.current) {
      again.current = true
      return inFlight.current
    }
    const { save: doSave, onDetail: emit, max: m } = latest.current
    const rows = [...dirty.current]
      .map((studentId) => ({ studentId, solved: valuesRef.current[studentId] ?? null }))
      .filter((r) => cellError(r.solved, m) === null)
    if (rows.length === 0) return
    setStatus({ kind: 'saving' })
    const job = (async () => {
      try {
        const res = await doSave(rows)
        // Javob kelguncha yana o'zgartirilmagan qatorlar — saqlandi; rad etilgani qizil qoladi (qayta yuborilmaydi).
        for (const r of rows) if ((valuesRef.current[r.studentId] ?? null) === r.solved) dirty.current.delete(r.studentId)
        for (const e of res.errors) dirty.current.delete(e.studentId)
        setErrors((prev) => {
          const next = { ...prev }
          for (const r of rows) delete next[r.studentId]
          for (const e of res.errors) next[e.studentId] = e.message
          return next
        })
        emit(res.detail)
        setStatus(
          res.errors.length > 0
            ? { kind: 'error', text: `${res.errors.length} ta qator saqlanmadi — qizil kataklarni tuzating` }
            : { kind: 'saved' },
        )
      } catch (e) {
        // Qiymatlar JOYIDA qoladi (dirty) — keyingi tahrir yoki chiqishda qayta yuboriladi.
        setStatus({ kind: 'error', text: apiErrorMessage(e, "Natijalarni saqlab bo'lmadi") })
      } finally {
        inFlight.current = null
        if (again.current) {
          again.current = false
          await runSave()
        }
      }
    })()
    inFlight.current = job
    return job
  }

  const flush = async () => {
    clearTimer()
    await runSave()
    while (inFlight.current) await inFlight.current
  }

  useImperativeHandle(ref, () => ({ flush }))

  // Jadval yopilganda (boshqa testga o'tildi) — kutib turgan o'zgarishlar yo'qolmasin.
  const flushOnLeave = useRef(flush)
  useEffect(() => {
    flushOnLeave.current = flush
  })
  useEffect(() => () => {
    void flushOnLeave.current()
  }, [])

  const schedule = () => {
    clearTimer()
    timer.current = setTimeout(() => {
      timer.current = null
      void runSave()
    }, AUTOSAVE_MS)
  }

  const setCells = (patch: Record<string, number | null>, patchErrors: Record<string, string | null>) => {
    const next = { ...valuesRef.current, ...patch }
    valuesRef.current = next
    for (const id of Object.keys(patch)) dirty.current.add(id)
    setValues(next)
    setTouched((prev) => {
      const t = new Set(prev)
      for (const id of [...Object.keys(patch), ...Object.keys(patchErrors)]) t.add(id)
      return t
    })
    setErrors((prev) => {
      const e = { ...prev }
      for (const [id, msg] of Object.entries(patchErrors)) {
        if (msg) e[id] = msg
        else delete e[id]
      }
      return e
    })
    if (Object.keys(patch).length > 0) schedule()
  }

  const focusRow = (i: number) => {
    const el = box.current?.querySelector<HTMLInputElement>(`input[data-grid-row="${i}"]`)
    if (el) {
      el.focus()
      el.select()
    }
  }

  const onPaste = (index: number, e: React.ClipboardEvent<HTMLInputElement>) => {
    const text = e.clipboardData.getData('text')
    if (!text) return
    e.preventDefault()
    const parsed = parsePastedColumn(text)
    const patch: Record<string, number | null> = {}
    const patchErrors: Record<string, string | null> = {}
    parsed.forEach((v, k) => {
      const id = ids[index + k]
      if (!id) return
      const err = pastedError(v, max)
      if (v !== null && typeof v === 'object') {
        patchErrors[id] = err // tanib bo'lmagan matn — qiymat o'zgarmaydi, qizil
        return
      }
      patch[id] = v
      patchErrors[id] = null
    })
    setCells(patch, patchErrors)
    focusRow(Math.min(ids.length - 1, index + parsed.length - 1))
  }

  const onKeyDown = (index: number, e: React.KeyboardEvent<HTMLInputElement>) => {
    const n = nextRowIndex(e.key, e.shiftKey, index, ids.length)
    if (n === null) return
    e.preventDefault()
    focusRow(n)
  }

  /** Jadvaldan TASHQARIGA chiqildi — darhol saqlaymiz (qatorlar orasida yurish so'rov yubormaydi). */
  const onBlurBox = (e: React.FocusEvent<HTMLDivElement>) => {
    if (box.current && e.relatedTarget instanceof Node && box.current.contains(e.relatedTarget)) return
    if (dirty.current.size > 0) {
      clearTimer()
      void runSave()
    }
  }

  const scored = ids.filter((id) => (values[id] ?? null) !== null).length
  const header = questions > 0 ? `Yechgan (0–${questions})` : `Ball (0–${fmtScore(detail.maxScore)})`

  return (
    <div>
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2 text-[12px]">
        <span className="text-mute">
          Kiritilgan: <b className="text-ink">{scored}</b>/{ids.length}
          {questions > 0 && <> · Savollar: <b className="text-ink">{questions}</b></>}
        </span>
        <span aria-live="polite" className="min-h-[18px]">
          {status.kind === 'saving' && (
            <span className="inline-flex items-center gap-1 text-mute">
              <Loader2 className="h-3.5 w-3.5 animate-spin" /> Saqlanmoqda…
            </span>
          )}
          {status.kind === 'saved' && (
            <span className="inline-flex items-center gap-1 text-emerald-600">
              <Check className="h-3.5 w-3.5" /> Saqlandi
            </span>
          )}
          {status.kind === 'error' && <span className="font-semibold text-rose-600">{status.text}</span>}
        </span>
      </div>

      <div ref={box} onBlur={onBlurBox} className="overflow-x-auto rounded-xl border border-line bg-white">
        <table className="w-full min-w-[420px] text-left text-sm">
          <thead>
            <tr className="border-b border-line bg-panel2 text-[11px] uppercase tracking-wide text-mute">
              <th className="w-10 px-3 py-2 font-semibold">№</th>
              <th className="px-2 py-2 font-semibold">F.I.Sh</th>
              <th className="w-32 px-2 py-2 font-semibold">{header}</th>
              <th className="w-20 px-2 py-2 text-right font-semibold">Foiz</th>
              <th className="w-16 px-3 py-2 text-right font-semibold">O'rin</th>
            </tr>
          </thead>
          <tbody>
            {ids.map((id, i) => {
              const row = byId.get(id)!
              const value = values[id] ?? null
              const edited = touched.has(id)
              const error = errors[id] ?? (edited ? cellError(value, max) : null)
              const pct = edited ? livePercent(value, max, denominator) : (row.percent ?? null)
              const legacy = value !== null && !Number.isInteger(value) && !unlocked.has(id)
              return (
                <tr key={id} className="border-b border-line last:border-0">
                  <td className="px-3 py-1.5 text-mute">{i + 1}</td>
                  <td className="px-2 py-1.5">
                    <span className="font-medium text-ink">{row.fullName}</span>
                    {row.member === false && (
                      <span
                        className="ml-1.5 rounded bg-sky-50 px-1.5 py-0.5 text-[10px] font-semibold text-sky-700"
                        title="Natija qo'yilgan, lekin hozir bu guruhning faol a'zosi emas"
                      >
                        GURUHDA EMAS
                      </span>
                    )}
                  </td>
                  <td className="px-2 py-1">
                    {editable && legacy ? (
                      <span className="inline-flex items-center gap-1.5">
                        <span className="text-ink">{fmtScore(value)}</span>
                        <button
                          type="button"
                          onClick={() => setUnlocked((prev) => new Set(prev).add(id))}
                          className="rounded px-1 text-[12px] text-mute hover:bg-panel2 hover:text-ink"
                          title="Kasrli eski ball — butun son bilan almashtirish"
                        >
                          ✎
                        </button>
                      </span>
                    ) : editable ? (
                      <NumberInput
                        as="input"
                        value={value !== null && !Number.isInteger(value) ? null : value}
                        onChange={(v) => setCells({ [id]: v }, { [id]: null })}
                        onKeyDown={(e) => onKeyDown(i, e)}
                        onPaste={(e) => onPaste(i, e)}
                        onFocus={(e) => e.currentTarget.select()}
                        data-grid-row={i}
                        aria-label={`${row.fullName} — natija`}
                        aria-invalid={error ? true : undefined}
                        title={error ?? undefined}
                        placeholder="—"
                        className={cn(
                          'h-9 w-24 rounded-lg border bg-white px-2.5 text-right text-[16px] text-ink outline-none transition-colors sm:text-sm',
                          error
                            ? 'border-rose-400 bg-rose-50 text-rose-700 focus:border-rose-500'
                            : 'border-line focus:border-brand-400',
                        )}
                      />
                    ) : (
                      <span className="text-ink">{value ?? '—'}</span>
                    )}
                    {error && editable && <p className="mt-0.5 text-[11px] leading-tight text-rose-600 sm:hidden">{error}</p>}
                  </td>
                  <td className="px-2 py-1.5 text-right text-mute">{pct === null ? '—' : `${fmtScore(pct)}%`}</td>
                  <td className="px-3 py-1.5 text-right font-semibold text-ink">
                    {row.rank > 0 ? (row.rank <= 3 ? MEDALS[row.rank - 1] : row.rank) : <span className="text-faint">—</span>}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {editable && (
        <p className="mt-2 text-[11px] text-faint">
          Enter/↓ — keyingi o'quvchi, Shift+Enter/↑ — oldingisi. Excel'dan ustunni nusxalab katakka qo'ysangiz — pastga
          to'ldiriladi. Natija avtomatik saqlanadi.
        </p>
      )}
    </div>
  )
}
