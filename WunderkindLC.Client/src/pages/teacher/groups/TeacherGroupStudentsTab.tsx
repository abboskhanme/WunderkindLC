import { useMemo, useRef, useState } from 'react'
import { Pencil, Search, Users } from 'lucide-react'
import {
  getTeacherGroupStudents,
  updateTeacherGroupStudent,
  type TeacherGroupStudent,
} from '@/api/services/teacher'
import { Avatar, EmptyState, SkeletonRows } from '@/components/teacher/TeacherUi'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { Modal } from '@/components/ui/Modal'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { useAsync } from '@/hooks/useAsync'
import { formatPhone } from '@/lib/numberFormat'
import { SCHOOL_GRADE_OPTIONS, schoolGradeLabel } from '@/lib/schoolGrades'
import { toast } from '@/lib/toast'
import { apiErrorMessage, cn } from '@/lib/utils'

/**
 * Guruh sahifasi → «O'quvchilar» tabi (o'qituvchi portali, asosan Telegram Mini App ichida).
 *
 * O'qituvchi o'z guruhi o'quvchisining FAQAT ism-familiyasi, sinfi va telefonini tahrirlaydi
 * (`PUT /teacher/groups/{id}/students/{studentId}`, server `TeacherStudentEdit`). Guruhga qo'shish,
 * chiqarish, muzlatish — bu yerda YO'Q (administrator ishi), va buni sahifaning o'zi aytadi.
 *
 * Telefon birinchi: qator butunlay bosiladi (kichik tugma emas), maydonlar 16px — iOS Telegram'da
 * fokusda sahifa kattalashib ketmasin.
 */

const STATUS_LABEL: Record<string, string> = { trial: 'Sinovda', frozen: 'Muzlatilgan' }

/** "+998-90-123-45-67" → "998901234567" (PhoneInput masklanmagan raqam bilan ishlaydi). */
const digitsOf = (phone: string) => phone.replace(/\D/g, '')

/** 16px mobil, 13px katta ekran — iOS fokusda zoom qilmasin. */
const fieldCls = 'min-h-[42px] text-base sm:min-h-[34px] sm:text-[13px]'

export function TeacherGroupStudentsTab({ groupId }: { groupId: string }) {
  const { data, loading, error } = useAsync(
    () =>
      getTeacherGroupStudents(groupId).catch((e: unknown) => {
        throw new Error(apiErrorMessage(e, "O'quvchilar ro'yxatini yuklab bo'lmadi"))
      }),
    [groupId],
  )
  // Saqlangan qatorlar — serverdan qaytgan yangi qiymat bilan (ro'yxatni qayta yuklamaymiz).
  const [edited, setEdited] = useState<Record<string, TeacherGroupStudent>>({})
  const [editing, setEditing] = useState<TeacherGroupStudent | null>(null)
  const [q, setQ] = useState('')

  const rows = useMemo(() => {
    const list = (data ?? []).map((r) => edited[r.id] ?? r)
    const needle = q.trim().toLowerCase()
    const digits = needle.replace(/\D/g, '')
    if (!needle) return list
    return list.filter(
      (r) =>
        r.fullName.toLowerCase().includes(needle) || (digits.length >= 3 && digitsOf(r.phone).includes(digits)),
    )
  }, [data, edited, q])

  return (
    <div className="space-y-3">
      <p className="rounded-lg border border-line bg-paper2 px-3 py-2 text-[12.5px] leading-snug text-mute">
        O'quvchining ism-familiyasi, sinfi va telefon raqamini o'zgartira olasiz. Guruhga qo'shish
        yoki guruhdan chiqarish — administrator orqali.
      </p>

      {(data?.length ?? 0) > 6 && (
        <div className="relative">
          <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-mute" />
          <input
            type="search"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            placeholder="Ism yoki telefon bo'yicha qidirish"
            className="w-full rounded-lg border border-line bg-white py-2 pl-9 pr-3 text-base text-ink outline-none focus:border-brand-600 sm:text-[13px]"
          />
        </div>
      )}

      {loading && !data ? (
        <SkeletonRows rows={5} />
      ) : error ? (
        <p className="text-[13px] text-red-600">{error}</p>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={Users}
          title={q ? 'Hech kim topilmadi' : "Guruhda o'quvchi yo'q"}
        />
      ) : (
        <ul className="divide-y divide-line overflow-hidden rounded-xl border border-line bg-white">
          {rows.map((s) => {
            const grade = schoolGradeLabel(s.schoolGradeNow)
            const status = STATUS_LABEL[s.status]
            return (
              <li key={s.id}>
                <button
                  type="button"
                  onClick={() => setEditing(s)}
                  className="flex w-full items-center gap-3 px-3 py-3 text-left transition-colors hover:bg-paper2 active:bg-paper2"
                >
                  <Avatar name={s.fullName} size={38} />
                  <span className="min-w-0 flex-1">
                    <span className="flex items-center gap-1.5">
                      <span className="truncate text-[14px] font-semibold text-ink">{s.fullName}</span>
                      {status && (
                        <span
                          className={cn(
                            'shrink-0 rounded-full px-1.5 py-0.5 text-[10.5px] font-semibold',
                            s.status === 'trial' ? 'bg-violet-50 text-violet-700' : 'bg-sky-50 text-sky-700',
                          )}
                        >
                          {status}
                        </span>
                      )}
                    </span>
                    <span className="mt-0.5 block truncate text-[12.5px] text-mute">
                      {[grade || 'Sinf kiritilmagan', s.phone ? formatPhone(s.phone) : 'Telefon yo\'q'].join(' · ')}
                    </span>
                  </span>
                  <span className="flex shrink-0 items-center gap-1 rounded-lg px-2 py-1.5 text-[12.5px] font-semibold text-brand-600">
                    <Pencil className="h-4 w-4" />
                    <span className="hidden sm:inline">Tahrirlash</span>
                  </span>
                </button>
              </li>
            )
          })}
        </ul>
      )}

      {editing && (
        <EditStudentModal
          // `key` — har o'quvchi uchun forma QAYTADAN yaratiladi (boshlang'ich qiymat initializer'da).
          key={editing.id}
          groupId={groupId}
          student={editing}
          onClose={() => setEditing(null)}
          onSaved={(row) => {
            setEdited((prev) => ({ ...prev, [row.id]: row }))
            setEditing(null)
            toast.success('Saqlandi')
          }}
        />
      )}
    </div>
  )
}

function EditStudentModal({
  groupId,
  student,
  onClose,
  onSaved,
}: {
  groupId: string
  student: TeacherGroupStudent
  onClose: () => void
  onSaved: (row: TeacherGroupStudent) => void
}) {
  const [fullName, setFullName] = useState(student.fullName)
  const [grade, setGrade] = useState(student.schoolGradeNow ?? 0)
  const [phone, setPhone] = useState(digitsOf(student.phone))
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Sinxron qulf: tez ikki bosishda so'rov ikki marta ketmasin (`saving` keyingi renderda yangilanadi).
  const inFlight = useRef(false)

  const phoneChanged = phone !== digitsOf(student.phone)
  const dirty =
    fullName.trim() !== student.fullName.trim() || grade !== (student.schoolGradeNow ?? 0) || phoneChanged

  const save = async () => {
    if (inFlight.current || !dirty) return
    if (fullName.trim().length < 2) {
      setError('Ism-familiyani kiriting')
      return
    }
    inFlight.current = true
    setSaving(true)
    setError(null)
    try {
      const row = await updateTeacherGroupStudent(groupId, student.id, {
        fullName: fullName.trim(),
        schoolGrade: grade,
        // O'zgarmagan raqam saqlangan ko'rinishida yuboriladi — server uni qayta tekshirmaydi.
        phone: phoneChanged ? phone : student.phone,
      })
      onSaved(row)
    } catch (e) {
      // Oyna YOPILMAYDI, kiritilgan qiymatlar qoladi (`error-visibility.md`).
      setError(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      inFlight.current = false
      setSaving(false)
    }
  }

  return (
    <Modal
      open
      onClose={() => !saving && onClose()}
      title="O'quvchini tahrirlash"
      size="sm"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={save} disabled={saving || !dirty}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form
        className="space-y-3"
        onSubmit={(e) => {
          e.preventDefault()
          void save()
        }}
      >
        <Input
          label="Ism-familiya"
          required
          value={fullName}
          onChange={(e) => setFullName(e.target.value)}
          autoComplete="off"
          maxLength={150}
          className={fieldCls}
        />
        <Select
          label="Sinf (maktabda)"
          value={grade ? String(grade) : ''}
          onChange={(e) => setGrade(e.target.value ? Number(e.target.value) : 0)}
          className={fieldCls}
        >
          <option value="">— kiritilmagan —</option>
          {SCHOOL_GRADE_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </Select>
        <PhoneInput label="Telefon raqami" value={phone} onChange={setPhone} className={fieldCls} />
        {error && <p className="text-[13px] text-red-600">{error}</p>}
        {/* Enter bilan yuborish uchun (footer tugmalari forma tashqarisida). */}
        <button type="submit" hidden />
      </form>
    </Modal>
  )
}
