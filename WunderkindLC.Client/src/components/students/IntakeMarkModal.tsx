import { useState } from 'react'
import { CheckCircle2, Undo2 } from 'lucide-react'
import {
  setStudentIntakeMark,
  type IntakeItem,
  type StudentIntakeDetail,
} from '@/api/services/studentIntake'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'

/**
 * QABUL CHEKLISTI — bandni QO'LDA "bajarildi" deb belgilash / izohni o'zgartirish / belgini olib
 * tashlash. Profil kartasi ham, "Qabul cheklisti" ro'yxati ham SHU oynani ochadi.
 *
 * <p>Oyna har ochilishda `key` bilan qaytadan yaratiladi (chaqiruvchi beradi) — boshlang'ich izoh
 * `useState` initializer'ida, effekt ichida setState YO'Q.</p>
 *
 * <p>⚠️ Xato bo'lsa oyna YOPILMAYDI va izoh saqlanib qoladi (`error-visibility.md` §2).</p>
 */
export function IntakeMarkModal({
  studentId,
  studentName,
  item,
  onClose,
  onSaved,
}: {
  studentId: string
  studentName?: string
  item: IntakeItem
  onClose: () => void
  /** Saqlangach — yangilangan cheklist (ro'yxat/karta shu bilan yangilanadi). */
  onSaved: (detail: StudentIntakeDetail) => void
}) {
  const [note, setNote] = useState(item.mark?.note ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const run = async (done: boolean) => {
    if (busy) return
    setBusy(true)
    setError('')
    try {
      const res = await setStudentIntakeMark(studentId, item.key, done, done ? note.trim() : undefined)
      onSaved(res)
      onClose()
    } catch (e) {
      setError(apiErrorMessage(e, "Belgini saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => !busy && onClose()}
      size="sm"
      title={item.label}
      footer={
        <>
          {item.mark && (
            <Button variant="secondary" onClick={() => run(false)} disabled={busy} className="mr-auto">
              <Undo2 className="h-4 w-4" /> Belgini olib tashlash
            </Button>
          )}
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={() => run(true)} disabled={busy}>
            <CheckCircle2 className="h-4 w-4" />
            {busy ? 'Saqlanmoqda...' : item.mark ? 'Saqlash' : 'Bajarildi deb belgilash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3 text-sm">
        {studentName && <p className="font-semibold text-slate-700">{studentName}</p>}
        <div className="rounded-lg bg-slate-50 px-3 py-2 text-slate-600">
          <p>
            Tizimdagi holat: <span className="font-medium text-slate-800">{item.detail}</span>
          </p>
          {item.auto ? (
            <p className="mt-1 text-xs text-emerald-700">Bu band yozuvlardan avtomatik aniqlangan.</p>
          ) : (
            <p className="mt-1 text-xs text-slate-500">
              Ish tizimdan tashqarida bajarilgan bo'lsa (qog'ozda test, qo'lda imzolangan shartnoma) —
              qo'lda belgilang. Kim va qachon belgilagani tarixga yoziladi.
            </p>
          )}
        </div>
        {item.mark && (
          <p className="text-xs text-slate-500">
            Belgilagan: <span className="font-medium text-slate-700">{item.mark.markedBy || '—'}</span> ·{' '}
            {formatDateTime(item.mark.markedAt)}
          </p>
        )}
        <div>
          <label className="mb-1 block text-sm font-medium text-slate-600">Izoh (ixtiyoriy)</label>
          <textarea
            value={note}
            onChange={(e) => setNote(e.target.value)}
            rows={3}
            maxLength={500}
            placeholder="Masalan: test qog'ozda olindi, natija — B1"
            className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
        </div>
        {error && <p className="text-sm text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
