import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { PhoneCall, CheckCircle2 } from 'lucide-react'
import type { ActionReason } from '@/types'
import { getActionReasons } from '@/api/services/actionReasons'
import { createContactRequestsBulk, type ContactBulkResult } from '@/api/services/contacts'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage } from '@/lib/utils'

/** Navbatga qo'shiladigan o'quvchi (minimal shakl — ro'yxat ham, profil ham shuni beradi). */
export interface ContactTarget {
  id: string
  fullName: string
}

/**
 * "BOG'LANISH KERAK" — o'quvchi(lar)ni bog'lanish navbatiga qo'shadi.
 *
 * <p>Ikki joydan ochiladi: o'quvchi profilidagi "⋮" menyusi (BITTA o'quvchi) va o'quvchilar
 * ro'yxatidagi tanlash paneli (KO'PLAB o'quvchi). Ikkalasi ham BITTA endpointdan
 * (`POST /contacts/bulk`) foydalanadi — qoida ikki joyda ayri ketmasin.</p>
 *
 * <p>Sabab bir marta tanlanadi va barcha tanlanganlarga birdek qo'llanadi. Sabablar
 * Sozlamalar → Sabablar dan, "contact" kategoriyasi (backend: `ContactService.ReasonCategory`).</p>
 *
 * <p>⚠️ Ochiq talabi bor o'quvchi CHETLAB O'TILADI (amal to'xtamaydi) — natijada nechtasi
 * qo'shilgani va nechtasi o'tkazib yuborilgani ko'rsatiladi.</p>
 */
export function NeedContactModal({
  open,
  students,
  onClose,
  onCreated,
}: {
  open: boolean
  /** Bitta yoki bir nechta o'quvchi. */
  students: ContactTarget[]
  onClose: () => void
  /** Muvaffaqiyatli qo'shilgach (masalan tanlovni tozalash uchun). */
  onCreated?: (result: ContactBulkResult) => void
}) {
  const [reasons, setReasons] = useState<ActionReason[]>([])
  const [reasonId, setReasonId] = useState('')
  const [note, setNote] = useState('')
  /** Bo'sh — darhol "Bog'lanish kerak"; sana berilsa "Qayta qo'ng'iroq" bo'lib tushadi. */
  const [dueDate, setDueDate] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  /** Natija ko'rsatilgan bo'lsa forma o'rniga xulosa turadi (o'tkazib yuborilganlar ko'rinsin). */
  const [result, setResult] = useState<ContactBulkResult | null>(null)

  const many = students.length > 1

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda holatni tiklash (maqsadli)
    setReasonId('')
    setNote('')
    setDueDate('')
    setError('')
    setResult(null)
    setBusy(false)
    getActionReasons()
      .then((all) => setReasons(all.filter((r) => r.category === 'contact')))
      .catch(() => setReasons([]))
  }, [open])

  const submit = async () => {
    if (busy || students.length === 0) return
    setBusy(true)
    setError('')
    try {
      const res = await createContactRequestsBulk({
        studentIds: students.map((s) => s.id),
        reasonId: reasonId || undefined,
        note: note.trim() || undefined,
        dueDate: dueDate || undefined,
      })
      onCreated?.(res)
      // Hammasi qo'shilgan bo'lsa oynani yopamiz; aks holda xulosani KO'RSATAMIZ —
      // aks holda "nega ba'zilari qo'shilmadi" degan savol javobsiz qolardi.
      if (res.skipped === 0 && res.notFound === 0) onClose()
      else setResult(res)
    } catch (e) {
      setError(apiErrorMessage(e, "Navbatga qo'shib bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={() => !busy && onClose()}
      size="sm"
      title={many ? `Bog'lanish kerak (${students.length} ta o'quvchi)` : "Bog'lanish kerak"}
      footer={
        result ? (
          <Button onClick={onClose}>Yopish</Button>
        ) : (
          <>
            <Button variant="secondary" onClick={onClose} disabled={busy}>
              Bekor qilish
            </Button>
            <Button onClick={submit} disabled={busy || students.length === 0}>
              <PhoneCall className="h-4 w-4" /> {busy ? 'Saqlanmoqda...' : "Navbatga qo'shish"}
            </Button>
          </>
        )
      }
    >
      {result ? (
        <div className="space-y-3">
          <p className="flex items-start gap-2 rounded-lg bg-emerald-50 px-3 py-2.5 text-sm text-emerald-800">
            <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" />
            <span>
              <strong>{result.created} ta</strong> o'quvchi navbatga qo'shildi.
            </span>
          </p>
          {result.skipped > 0 && (
            <div className="rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-800">
              <p>
                <strong>{result.skipped} ta</strong> o'quvchida allaqachon ochiq talab bor —
                ular qayta qo'shilmadi (bir o'quvchida bir vaqtda bitta talab bo'ladi).
              </p>
              {result.skippedNames.length > 0 && (
                <p className="mt-1 text-xs">
                  {result.skippedNames.join(', ')}
                  {result.skipped > result.skippedNames.length && ' va boshqalar'}
                </p>
              )}
            </div>
          )}
          {result.notFound > 0 && (
            <p className="rounded-lg bg-slate-100 px-3 py-2 text-sm text-slate-600">
              {result.notFound} ta o'quvchi topilmadi (ro'yxat eskirgan bo'lishi mumkin) —
              sahifani yangilang.
            </p>
          )}
          <Link
            to="/admin/students/boglanish"
            className="inline-block text-sm font-semibold text-brand-600 hover:underline"
          >
            Navbatni ochish →
          </Link>
        </div>
      ) : (
        <div className="space-y-4">
          <div className="rounded-lg bg-slate-50 px-3 py-2 text-sm">
            {many ? (
              <>
                <p className="font-semibold text-slate-700">{students.length} ta o'quvchi tanlandi</p>
                <p className="mt-0.5 line-clamp-2 text-xs text-slate-500">
                  {students.slice(0, 5).map((s) => s.fullName).join(', ')}
                  {students.length > 5 && ` va yana ${students.length - 5} ta`}
                </p>
              </>
            ) : (
              <p className="font-semibold text-slate-700">{students[0]?.fullName ?? ''}</p>
            )}
            <p className="mt-1 text-slate-500">
              {many ? "Hammasi" : "O'quvchi"} "Bog'lanish kerak" navbatiga tushadi — operator
              bog'lanib javobini yozadi.
            </p>
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Sabab</label>
            <select
              value={reasonId}
              onChange={(e) => setReasonId(e.target.value)}
              className="w-full rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            >
              <option value="">— Tanlanmagan —</option>
              {reasons.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.label}
                </option>
              ))}
            </select>
            {reasons.length === 0 && (
              <p className="mt-1 text-xs text-amber-700">
                Sabablar ro'yxati bo'sh — Sozlamalar → Sabablar da "Bog'lanish kerak" kategoriyasiga
                sabab qo'shing (sababsiz ham navbatga qo'shsa bo'ladi).
              </p>
            )}
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">Izoh (ixtiyoriy)</label>
            <textarea
              value={note}
              onChange={(e) => setNote(e.target.value)}
              rows={3}
              maxLength={2000}
              placeholder="Operator uchun qisqacha: nima haqida gaplashish kerak"
              className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
            {many && (
              <p className="mt-1 text-xs text-slate-400">
                Izoh va sabab TANLANGANLARNING HAMMASIGA bir xil qo'yiladi.
              </p>
            )}
          </div>

          <div>
            <label className="mb-1 block text-sm font-medium text-slate-600">
              Qachon bog'lanilsin (ixtiyoriy)
            </label>
            <input
              type="date"
              value={dueDate}
              onChange={(e) => setDueDate(e.target.value)}
              className="rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
            />
            <p className="mt-1 text-xs text-slate-400">
              Bo'sh qolsa — darhol navbatga. Sana tanlansa "Qayta qo'ng'iroq" bo'lib o'sha kunda chiqadi.
            </p>
          </div>

          {error && <p className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
