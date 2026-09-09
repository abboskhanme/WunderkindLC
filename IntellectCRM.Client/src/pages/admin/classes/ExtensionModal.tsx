import { useEffect, useMemo, useState } from 'react'
import { GraduationCap } from 'lucide-react'
import type { Group, Teacher } from '@/types'
import { addMemberExtension, getClasses } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { apiErrorMessage } from '@/lib/utils'

const today = () => new Date().toISOString().slice(0, 10)

interface Props {
  open: boolean
  onClose: () => void
  studentId: string
  studentName: string
  fromGroupId: string
  fromGroupName: string
  onDone: () => void
}

/**
 * UZAYTIRISH QAYDI — o'quvchi kursning bir bosqichini TUGATIB, keyingisini boshlagani
 * hodisasi (chiquvchi adminning «Bonus B» manbai, `.claude/rules/kpi.md` §1).
 *
 * ⚠️ Bu modal a'zolikni KO'CHIRMAYDI va hech qanday pul hisobiga tegmaydi — u faqat
 * HODISANI yozadi. Ko'chirish kerak bo'lsa «Boshqa guruhga o'tkazish» ishlatiladi.
 * Guruhni «Tugatish (sertifikat bilan)» yo'li bu hodisani O'ZI yozadi, shuning uchun bu
 * yerdagi qo'lda qayd faqat IKKI holat uchun: tizimdan tashqarida bo'lgan o'tish va
 * o'tmishdagi (modul yo'q paytdagi) o'tishni keyinchalik to'ldirish.
 *
 * ⚠️ Oddiy oylik to'lov yoki bir xil kursdagi guruh almashtirish uzaytirish EMAS — shu
 * ta'rifni buzish butun bonus tizimini qimmatlashtiradi (Excel «KPI qoidalari» F-bo'limi).
 * Shuning uchun modal matnida ham, serverda ham shu chegara takrorlanadi.
 */
export function ExtensionModal({
  open, onClose, studentId, studentName, fromGroupId, fromGroupName, onDone,
}: Props) {
  const [groups, setGroups] = useState<Group[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [teacherId, setTeacherId] = useState('')
  const [toGroupId, setToGroupId] = useState('')
  const [date, setDate] = useState(today())
  const [note, setNote] = useState('')
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open) return
    // ⚠️ setState effekt ICHIDA emas, javob kelgach chaqiriladi (`react-hooks/set-state-in-effect`).
    let alive = true
    Promise.all([getClasses(), getTeachers()])
      .then(([gs, ts]) => {
        if (!alive) return
        // Arxivlangan guruh ham QOLADI: o'tmishdagi uzaytirishni keyinchalik yozish uchun
        // maqsad guruh allaqachon yopilgan bo'lishi mumkin.
        setGroups(gs.filter((g) => g.id !== fromGroupId))
        setTeachers(ts.filter((t) => !t.isArchived))
      })
      .catch(() => {
        if (!alive) return
        setGroups([])
        setTeachers([])
      })
    return () => {
      alive = false
    }
  }, [open, fromGroupId])

  const teacherOptions = useMemo(
    () =>
      teachers
        .filter((t) => groups.some((g) => g.teacherId === t.id))
        .sort((a, b) => a.fullName.localeCompare(b.fullName)),
    [teachers, groups],
  )
  const groupsForTeacher = useMemo(
    () => groups.filter((g) => g.teacherId === teacherId),
    [groups, teacherId],
  )

  const handleSave = async () => {
    if (!toGroupId || saving) return
    setSaving(true)
    setError('')
    try {
      await addMemberExtension(fromGroupId, studentId, toGroupId, date, note.trim() || undefined)
      onDone()
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Uzaytirish qaydini yozib bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title="Uzaytirish qaydi"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button onClick={handleSave} disabled={!toGroupId || saving}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="flex items-start gap-2 rounded-lg bg-indigo-50 p-3 text-[13px] text-indigo-900">
          <GraduationCap className="mt-0.5 h-4 w-4 shrink-0" />
          <div>
            <b>{studentName}</b> «{fromGroupName}» guruhida kurs bosqichini tugatib, keyingi
            bosqichni boshlagani qayd etiladi.
            <div className="mt-1 text-indigo-700">
              Bu qayd a'zolikni ko'chirmaydi va to'lovga tegmaydi. Oddiy oylik to'lov yoki bir xil
              kursdagi guruh almashtirish uzaytirish hisoblanmaydi.
            </div>
          </div>
        </div>

        <Select label="O'qituvchi" value={teacherId} onChange={(e) => { setTeacherId(e.target.value); setToGroupId('') }}>
          <option value="">— tanlang —</option>
          {teacherOptions.map((t) => (
            <option key={t.id} value={t.id}>{t.fullName}</option>
          ))}
        </Select>

        <Select label="Keyingi bosqich guruhi" required value={toGroupId} onChange={(e) => setToGroupId(e.target.value)}>
          <option value="">— tanlang —</option>
          {groupsForTeacher.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}{g.isArchived ? ' (arxiv)' : ''}
            </option>
          ))}
        </Select>

        <Input
          label="Sana"
          type="date"
          value={date}
          onChange={(e) => setDate(e.target.value)}
          required
        />

        <Textarea
          label="Izoh"
          rows={2}
          value={note}
          onChange={(e) => setNote(e.target.value)}
          placeholder="Ixtiyoriy"
        />

        {error && <div className="rounded-lg bg-red-50 px-3 py-2 text-sm text-red-700">{error}</div>}
      </div>
    </Modal>
  )
}
