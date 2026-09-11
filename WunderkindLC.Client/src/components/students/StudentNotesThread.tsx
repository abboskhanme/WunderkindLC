import { useEffect, useState } from 'react'
import { Pencil, Plus, Trash2 } from 'lucide-react'
import {
  getStudentNotes,
  addStudentNote,
  updateStudentNote,
  deleteStudentNote,
  type StudentNote,
} from '@/api/services/students'
import { Button } from '@/components/ui/Button'
import { usePerm } from '@/lib/permissions'
import { apiErrorMessage, formatDateTime } from '@/lib/utils'

/**
 * O'QUVCHI IZOHLARI — xodim yozadigan erkin eslatmalar (ota-ona bilan suhbat, to'lov kelishuvi,
 * sog'lig'i va h.k.).
 *
 * <p>TARIX: har izoh o'z muallifi va vaqti bilan qoladi, ustiga yozilmaydi. Tahrir/o'chirishni
 * faqat MUALLIFI yoki superadmin qila oladi — bayroqlar (`canEdit`/`canDelete`) SERVERDAN
 * keladi, ya'ni qoida klientda takrorlanmaydi.</p>
 *
 * <p><b>IKKI JOYDA ISHLATILADI</b> (nusxa YO'Q): o'quvchi profilidagi "Izohlar" tabi va
 * "O'quvchilar → Izohlarga javoblar" sahifasidagi o'quvchi oynasi. Shu sabab komponent o'z
 * sarlavhasini chizmaydi — uni chaqiruvchi (Section yoki Modal) beradi.</p>
 */
export function StudentNotesThread({
  studentId,
  onChanged,
  autoFocus,
}: {
  studentId: string
  /** Izoh qo'shilgan/o'chirilganda — chaqiruvchi ro'yxatdagi sonni yangilay olsin. */
  onChanged?: (count: number) => void
  autoFocus?: boolean
}) {
  const { canAny } = usePerm()
  // Yozish — `students` bo'limining "qo'shish" amali (serverdagi qoida bilan bir xil):
  // faqat ko'rish ruxsati bor xodim tugmani bosib 403 olmasin.
  // Izoh IKKI joydan yoziladi: o'quvchi profilida (`students.list`) va "Izohlarga javoblar"
  // sahifasida (`students.notes`) — shuning uchun ikkalasidan biri yetadi (serverda ham shunday).
  const canAdd = canAny(['students.list', 'students.notes'], 'create')

  const [notes, setNotes] = useState<StudentNote[]>([])
  const [loading, setLoading] = useState(true)
  const [text, setText] = useState('')
  const [saving, setSaving] = useState(false)
  /** Hozir tahrirlanayotgan izoh id'si (null = tahrir rejimi yopiq) + tahrir matni. */
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editText, setEditText] = useState('')
  const [editSaving, setEditSaving] = useState(false)

  useEffect(() => {
    let alive = true
    // eslint-disable-next-line react-hooks/set-state-in-effect -- o'quvchi almashganda qayta yuklash (maqsadli)
    setLoading(true)
    getStudentNotes(studentId)
      .then((n) => alive && setNotes(n))
      .catch(() => alive && setNotes([]))
      .finally(() => alive && setLoading(false))
    return () => {
      alive = false
    }
  }, [studentId])

  const handleAdd = () => {
    const value = text.trim()
    if (!value || saving) return
    setSaving(true)
    addStudentNote(studentId, value)
      .then((note) => {
        setNotes((prev) => {
          const next = [note, ...prev]
          onChanged?.(next.length)
          return next
        })
        setText('')
      })
      .catch((e) => alert(apiErrorMessage(e, "Izohni saqlab bo'lmadi")))
      .finally(() => setSaving(false))
  }

  const handleDelete = (note: StudentNote) => {
    if (!confirm("Bu izoh o'chirilsinmi?")) return
    deleteStudentNote(note.id)
      .then(() =>
        setNotes((prev) => {
          const next = prev.filter((n) => n.id !== note.id)
          onChanged?.(next.length)
          return next
        }),
      )
      .catch((e) => alert(apiErrorMessage(e, "Izohni o'chirib bo'lmadi")))
  }

  /** Tahrirlashni boshlash — izoh o'rnida matn maydoni ochiladi. */
  const startEdit = (note: StudentNote) => {
    setEditingId(note.id)
    setEditText(note.text)
  }

  const cancelEdit = () => {
    setEditingId(null)
    setEditText('')
  }

  /** Tahrirni saqlash — muallif va yozilgan vaqt o'zgarmaydi, "tahrirlangan" belgisi qo'shiladi. */
  const handleEditSave = (note: StudentNote) => {
    const value = editText.trim()
    if (!value || editSaving) return
    if (value === note.text) {
      cancelEdit()
      return
    }
    setEditSaving(true)
    updateStudentNote(note.id, value)
      .then((updated) => {
        setNotes((prev) => prev.map((n) => (n.id === updated.id ? { ...n, ...updated } : n)))
        cancelEdit()
      })
      .catch((e) => alert(apiErrorMessage(e, "Izohni tahrirlab bo'lmadi")))
      .finally(() => setEditSaving(false))
  }

  return (
    <div>
      {canAdd && (
        <div className="mb-5">
          <textarea
            value={text}
            onChange={(e) => setText(e.target.value)}
            // Ctrl/Cmd+Enter — tez saqlash (matnda oddiy Enter yangi qator bo'lib qolsin).
            onKeyDown={(e) => {
              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                e.preventDefault()
                handleAdd()
              }
            }}
            rows={3}
            autoFocus={autoFocus}
            placeholder="Izoh yozing — masalan: onasi qo'ng'iroq qildi, dushanba kelolmaydi..."
            className="w-full resize-y rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
          />
          <div className="mt-2 flex items-center justify-between gap-3">
            <span className="text-xs text-slate-400">Saqlash: Ctrl + Enter</span>
            <Button onClick={handleAdd} disabled={!text.trim() || saving}>
              <Plus className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : "Qo'shish"}
            </Button>
          </div>
        </div>
      )}

      {loading ? (
        <p className="py-8 text-center text-sm text-slate-400">Yuklanmoqda...</p>
      ) : notes.length === 0 ? (
        <p className="py-8 text-center text-sm text-slate-400">
          Bu o'quvchi haqida hali izoh yozilmagan.
        </p>
      ) : (
        <div className="divide-y divide-slate-100">
          {notes.map((n) => {
            // Tahrirlash huquqi o'chirish bilan bir xil (muallifi yoki superadmin); eski javoblarda
            // canEdit bo'lmasligi mumkin — o'shanda canDelete'ga tayanamiz.
            const canEdit = n.canEdit ?? n.canDelete
            const editing = editingId === n.id
            return (
              <div key={n.id} className="group flex items-start gap-3 py-3">
                <div className="min-w-0 flex-1">
                  {editing ? (
                    <>
                      <textarea
                        value={editText}
                        onChange={(e) => setEditText(e.target.value)}
                        // Ctrl/Cmd+Enter — saqlash, Esc — bekor qilish.
                        onKeyDown={(e) => {
                          if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) {
                            e.preventDefault()
                            handleEditSave(n)
                          } else if (e.key === 'Escape') {
                            e.preventDefault()
                            cancelEdit()
                          }
                        }}
                        rows={3}
                        autoFocus
                        className="w-full resize-y rounded-lg border border-slate-200 bg-white px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
                      />
                      <div className="mt-2 flex items-center gap-2">
                        <Button
                          onClick={() => handleEditSave(n)}
                          disabled={!editText.trim() || editSaving}
                        >
                          {editSaving ? 'Saqlanmoqda...' : 'Saqlash'}
                        </Button>
                        <Button variant="secondary" onClick={cancelEdit} disabled={editSaving}>
                          Bekor qilish
                        </Button>
                        <span className="text-xs text-slate-400">Ctrl + Enter · Esc</span>
                      </div>
                    </>
                  ) : (
                    <>
                      <p className="whitespace-pre-wrap break-words text-sm text-slate-700">{n.text}</p>
                      <p className="mt-1 text-xs text-slate-400">
                        {n.authorName || 'Admin'} · {formatDateTime(n.createdAt)}
                        {n.editedAt ? ` · tahrirlangan ${formatDateTime(n.editedAt)}` : ''}
                      </p>
                    </>
                  )}
                </div>
                {!editing && canEdit && (
                  <button
                    type="button"
                    title="Izohni tahrirlash"
                    onClick={() => startEdit(n)}
                    className="rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-brand-50 hover:text-brand-600"
                  >
                    <Pencil className="h-4 w-4" />
                  </button>
                )}
                {!editing && n.canDelete && (
                  <button
                    type="button"
                    title="Izohni o'chirish"
                    onClick={() => handleDelete(n)}
                    className="rounded-lg p-1.5 text-slate-300 transition-colors hover:bg-red-50 hover:text-red-600"
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                )}
              </div>
            )
          })}
        </div>
      )}
    </div>
  )
}
