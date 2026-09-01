import { useEffect, useState } from 'react'
import { PhoneCall, CalendarClock, CheckCircle2, XCircle } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { addContactAttempt, type ContactMeta, type ContactRequestItem } from '@/api/services/contacts'
import { apiErrorMessage, cn } from '@/lib/utils'

/** "YYYY-MM-DD" — bugundan `days` kun keyin. */
function inDays(days: number): string {
  const d = new Date()
  d.setDate(d.getDate() + days)
  return d.toISOString().slice(0, 10)
}

/** Keyingi qadam variantlari — server `ContactService.CanTransitionTo` bilan AYNAN bir xil. */
const nextSteps = [
  { key: 'done', label: 'Hal bo\'ldi', hint: 'Masala yopiladi', icon: CheckCircle2, tone: 'emerald' },
  { key: 'callback', label: 'Qayta qo\'ng\'iroq', hint: 'Sana tanlanadi', icon: CalendarClock, tone: 'sky' },
  { key: 'failed', label: 'Bog\'lanib bo\'lmadi', hint: 'Natijasiz yopiladi', icon: XCircle, tone: 'rose' },
] as const

/**
 * BOG'LANILDI — navbatdagi talab bo'yicha bitta urinish natijasini yozadi.
 *
 * <p>Uch narsa so'raladi: NATIJA (ko'tardimi), "JAVOBI NIMA DEDI" va KEYINGI QADAM. Keyingi qadam
 * talabning yangi bosqichini belgilaydi — hisobotdagi "kim qaysi bosqichga oldi" aynan shu.</p>
 *
 * <p>"Bog'lanish kerak" (new) ga qaytarish ATAYIN yo'q: bog'langandan keyin boshiga qaytish
 * navbatni cheksiz aylantirardi. Kerak bo'lsa bugungi sana bilan "Qayta qo'ng'iroq" tanlanadi.</p>
 */
export function ContactAttemptModal({
  open,
  request,
  meta,
  presetNextStatus,
  presetDueDate,
  onClose,
  onSaved,
}: {
  open: boolean
  request: ContactRequestItem | null
  meta: ContactMeta
  /**
   * Kanban taxtasida karta SUDRAB tashlanganda oldindan tanlanadigan keyingi qadam va sanasi.
   *
   * ⚠️ Faqat BOSHLANG'ICH qiymat — operator baribir o'zgartira oladi va natijani ("ko'tardimi")
   * baribir o'zi tanlaydi. Sudrash hech qachon bosqichni o'z-o'zidan yozmaydi: har o'tish
   * hodisa sifatida yoziladi (`.claude/rules/contacts.md` §2).
   */
  presetNextStatus?: string
  presetDueDate?: string
  onClose: () => void
  onSaved: (updated: ContactRequestItem) => void
}) {
  const [result, setResult] = useState('')
  const [response, setResponse] = useState('')
  const [nextStatus, setNextStatus] = useState<string>('')
  const [dueDate, setDueDate] = useState(inDays(1))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda holatni tiklash (maqsadli)
    setResult('')
    setResponse('')
    setNextStatus(presetNextStatus ?? '')
    setDueDate(presetDueDate || inDays(1))
    setError('')
    setBusy(false)
  }, [open, presetNextStatus, presetDueDate])

  // Ko'tarmagan/band bo'lsa keyingi qadam deyarli har doim "qayta qo'ng'iroq" — oldindan taklif
  // qilamiz (operator baribir o'zgartira oladi). Bu klik sonini kamaytiradi, qoidani buzmaydi.
  const pickResult = (key: string) => {
    setResult(key)
    const reached = meta.results.find((r) => r.key === key)?.reached
    if (!nextStatus) setNextStatus(reached ? 'done' : 'callback')
  }

  const save = async () => {
    if (!request || busy) return
    if (!result) return setError('Natijani tanlang')
    if (!nextStatus) return setError('Keyingi qadamni tanlang')
    if (nextStatus === 'callback' && !dueDate) return setError('Qayta qo\'ng\'iroq sanasini tanlang')
    setBusy(true)
    setError('')
    try {
      const updated = await addContactAttempt(request.id, {
        result,
        response: response.trim() || undefined,
        nextStatus,
        dueDate: nextStatus === 'callback' ? dueDate : undefined,
      })
      onSaved(updated)
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={() => !busy && onClose()}
      size="md"
      title="Bog'lanildi"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={busy || !result || !nextStatus}>
            <PhoneCall className="h-4 w-4" /> {busy ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      {request && (
        <div className="space-y-4">
          <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
            <p className="font-semibold text-slate-700">{request.studentName}</p>
            <p className="mt-0.5 text-slate-500">
              {request.reasonLabel || '— sababsiz —'}
              {request.attemptCount > 0 && ` · ${request.attemptCount}-urinish`}
            </p>
            {request.phones.length > 0 && (
              <p className="mt-1 flex flex-wrap gap-2">
                {request.phones.map((p) => (
                  <a key={p} href={`tel:${p}`} className="font-mono text-sm text-brand-600 hover:underline">
                    {p}
                  </a>
                ))}
              </p>
            )}
          </div>

          {/* 1) NATIJA */}
          <div>
            <p className="mb-1.5 text-sm font-medium text-slate-600">Natija</p>
            <div className="flex flex-wrap gap-2">
              {meta.results.map((r) => (
                <button
                  key={r.key}
                  type="button"
                  onClick={() => pickResult(r.key)}
                  className={cn(
                    'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                    result === r.key
                      ? 'border-brand-500 bg-brand-50 text-brand-700'
                      : 'border-slate-200 bg-white text-slate-600 hover:border-slate-300 hover:bg-slate-50',
                  )}
                >
                  {r.label}
                </button>
              ))}
            </div>
          </div>

          {/* 2) JAVOBI NIMA DEDI */}
          <div>
            <label className="mb-1.5 block text-sm font-medium text-slate-600">
              Javobi nima dedi?
            </label>
            <textarea
              value={response}
              onChange={(e) => setResponse(e.target.value)}
              rows={3}
              maxLength={2000}
              placeholder="Masalan: “Kelasi haftadan davom ettiramiz, hozir kasal” yoki “To'lovni juma kuni qiladi”"
              className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none transition-colors focus:border-brand-400 focus:ring-2 focus:ring-brand-100"
            />
            <p className="mt-1 text-xs text-slate-400">
              Bu matn navbatda va hisobotda ko'rinadi — keyingi operator shu yerdan davom etadi.
            </p>
          </div>

          {/* 3) KEYINGI QADAM */}
          <div>
            <p className="mb-1.5 text-sm font-medium text-slate-600">Keyingi qadam</p>
            <div className="grid gap-2 sm:grid-cols-3">
              {nextSteps.map((s) => {
                const Icon = s.icon
                const active = nextStatus === s.key
                return (
                  <button
                    key={s.key}
                    type="button"
                    onClick={() => setNextStatus(s.key)}
                    className={cn(
                      'flex flex-col items-start gap-0.5 rounded-lg border px-3 py-2 text-left transition-colors',
                      active
                        ? s.tone === 'emerald'
                          ? 'border-emerald-500 bg-emerald-50'
                          : s.tone === 'sky'
                            ? 'border-sky-500 bg-sky-50'
                            : 'border-rose-500 bg-rose-50'
                        : 'border-slate-200 bg-white hover:border-slate-300 hover:bg-slate-50',
                    )}
                  >
                    <span className="flex items-center gap-1.5 text-sm font-semibold text-slate-700">
                      <Icon className="h-4 w-4" /> {s.label}
                    </span>
                    <span className="text-xs text-slate-400">{s.hint}</span>
                  </button>
                )
              })}
            </div>

            {nextStatus === 'callback' && (
              <div className="mt-3">
                <label className="mb-1 block text-sm font-medium text-slate-600">
                  Qayta qo'ng'iroq sanasi
                </label>
                <div className="flex flex-wrap items-center gap-2">
                  <input
                    type="date"
                    value={dueDate}
                    onChange={(e) => setDueDate(e.target.value)}
                    className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                  />
                  {[
                    { label: 'Bugun', days: 0 },
                    { label: 'Ertaga', days: 1 },
                    { label: '3 kun', days: 3 },
                    { label: '1 hafta', days: 7 },
                  ].map((q) => (
                    <button
                      key={q.label}
                      type="button"
                      onClick={() => setDueDate(inDays(q.days))}
                      className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-xs font-medium text-slate-500 hover:border-slate-300 hover:bg-slate-50"
                    >
                      {q.label}
                    </button>
                  ))}
                </div>
              </div>
            )}
          </div>

          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
