import { useEffect, useState } from 'react'
import { AlertTriangle, Trash2 } from 'lucide-react'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Loader } from '@/components/ui/Loader'
import {
  getStudentPurgePreview,
  purgeStudent,
  type StudentPurgePreview,
} from '@/api/services/students'
import { apiErrorMessage } from '@/lib/utils'

/**
 * O'QUVCHINI BUTUNLAY O'CHIRISH — xato kiritilgan yoki DUBLIKAT o'quvchi uchun
 * (ruxsat: «O'quvchilar ro'yxati» → «O'chirish», `students.list:delete`).
 *
 * Oddiy arxivlashdan farqi: o'quvchi arxivga ham, «O'chirilganlar»ga ham TUSHMAYDI — bazadan
 * butunlay o'chadi. Shuning uchun oyna avval NIMALAR o'chishini ko'rsatadi va aniq tasdiq
 * (ptichka) talab qiladi.
 *
 * Pul yozuvlari o'chirilmaydi: tirik to'lovi bor o'quvchida tugma o'chiq va sababi yoziladi
 * (server ham rad etadi) — `.claude/rules/student-purge.md`.
 *
 * Ota komponent oynani faqat ochiq paytda chizadi — har ochilishda holat yangidan boshlanadi.
 */
export function StudentPurgeModal({
  studentId,
  onClose,
  onPurged,
}: {
  studentId: string
  onClose: () => void
  onPurged: () => void
}) {
  const [preview, setPreview] = useState<StudentPurgePreview | null>(null)
  const [loadError, setLoadError] = useState('')
  const [confirmed, setConfirmed] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    let alive = true
    getStudentPurgePreview(studentId)
      .then((p) => alive && setPreview(p))
      .catch((e) => alive && setLoadError(apiErrorMessage(e, "Ma'lumotni yuklab bo'lmadi")))
    return () => {
      alive = false
    }
  }, [studentId])

  const submit = async () => {
    if (busy || !preview?.canPurge || !confirmed) return
    setBusy(true)
    setError('')
    try {
      await purgeStudent(studentId)
      onPurged()
    } catch (e) {
      setError(apiErrorMessage(e, "O'chirib bo'lmadi"))
      setBusy(false)
    }
  }

  const removed = preview?.items.filter((i) => !i.detach) ?? []
  const detached = preview?.items.filter((i) => i.detach) ?? []

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      size="sm"
      title="O'quvchini butunlay o'chirish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button variant="danger" onClick={submit} disabled={busy || !preview?.canPurge || !confirmed}>
            <Trash2 className="h-4 w-4" /> {busy ? "O'chirilmoqda..." : "Butunlay o'chirish"}
          </Button>
        </>
      }
    >
      {loadError ? (
        <p className="rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-700">{loadError}</p>
      ) : !preview ? (
        <Loader label="Tekshirilmoqda..." />
      ) : (
        <div className="space-y-4">
          <div className="flex items-start gap-2 rounded-lg bg-red-50 px-3 py-2.5 text-sm text-red-800">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />
            <p>
              <strong>{preview.fullName}</strong> va unga tegishli barcha ma'lumotlar bazadan
              butunlay o'chiriladi. Arxivga ham tushmaydi — bu amalni <strong>qaytarib bo'lmaydi</strong>.
              Xato kiritilgan yoki dublikat o'quvchi uchun.
            </p>
          </div>

          {!preview.canPurge && preview.blockReason && (
            <p className="rounded-lg bg-amber-50 px-3 py-2.5 text-sm text-amber-800">{preview.blockReason}</p>
          )}

          {removed.length > 0 ? (
            <div>
              <p className="mb-1.5 text-sm font-medium text-slate-700">O'chiriladi:</p>
              <ul className="space-y-1 text-sm text-slate-600">
                {removed.map((i) => (
                  <li key={i.key} className="flex justify-between gap-3">
                    <span>{i.label}</span>
                    <span className="font-medium tabular-nums text-slate-800">{i.count}</span>
                  </li>
                ))}
              </ul>
            </div>
          ) : (
            <p className="text-sm text-slate-500">O'quvchiga bog'liq boshqa yozuv yo'q — faqat o'quvchining o'zi o'chadi.</p>
          )}

          {detached.length > 0 && (
            <div>
              <p className="mb-1.5 text-sm font-medium text-slate-700">
                Saqlanadi, faqat o'quvchidan ajratiladi:
              </p>
              <ul className="space-y-1 text-sm text-slate-500">
                {detached.map((i) => (
                  <li key={i.key} className="flex justify-between gap-3">
                    <span>{i.label}</span>
                    <span className="tabular-nums">{i.count}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {preview.canPurge && (
            <label className="flex cursor-pointer items-start gap-2 rounded-lg border border-slate-200 px-3 py-2.5 text-sm text-slate-700">
              <input
                type="checkbox"
                checked={confirmed}
                onChange={(e) => setConfirmed(e.target.checked)}
                disabled={busy}
                className="mt-0.5 h-4 w-4 accent-red-600"
              />
              <span>Tushundim: o'quvchi va uning ma'lumotlari butunlay o'chadi, qaytarib bo'lmaydi.</span>
            </label>
          )}

          {error && <p className="text-sm text-red-600">{error}</p>}
        </div>
      )}
    </Modal>
  )
}
