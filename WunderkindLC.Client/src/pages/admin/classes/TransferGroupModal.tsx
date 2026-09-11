import { useEffect, useMemo, useState } from 'react'
import { ArrowLeftRight } from 'lucide-react'
import type { Group, Teacher } from '@/types'
import { getClasses, transferMember } from '@/api/services/classes'
import { getTeachers } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Select } from '@/components/ui/Input'
import { apiErrorMessage } from '@/lib/utils'

const today = () => new Date().toISOString().slice(0, 10)

interface Props {
  open: boolean
  onClose: () => void
  studentId: string
  studentName: string
  fromGroupId: string
  fromGroupName: string
  /** Muvaffaqiyatli o'tkazilgach — chaqiruvchi ro'yxatini yangilash uchun. */
  onDone: () => void
}

/**
 * O'quvchini joriy guruhdan boshqa (mavjud) guruhga o'tkazish: eski guruh muzlatiladi,
 * yangi guruh darhol aktivlashtiriladi — bitta modal, ikkita sana bilan.
 */
export function TransferGroupModal({ open, onClose, studentId, studentName, fromGroupId, fromGroupName, onDone }: Props) {
  const [groups, setGroups] = useState<Group[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [teacherId, setTeacherId] = useState('')
  const [toGroupId, setToGroupId] = useState('')
  const [freezeDate, setFreezeDate] = useState(today())
  const [activateDate, setActivateDate] = useState(today())
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open) return
    setTeacherId('')
    setToGroupId('')
    setFreezeDate(today())
    setActivateDate(today())
    setError('')
    Promise.all([getClasses(), getTeachers()])
      .then(([gs, ts]) => {
        setGroups(gs.filter((g) => !g.isArchived && g.id !== fromGroupId))
        setTeachers(ts.filter((t) => !t.isArchived))
      })
      .catch(() => {
        setGroups([])
        setTeachers([])
      })
  }, [open, fromGroupId])

  // Faqat guruhi bor o'qituvchilar tanlov ro'yxatida ko'rinadi.
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
      await transferMember(fromGroupId, studentId, toGroupId, freezeDate, activateDate)
      onDone()
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Guruhni almashtirib bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={() => !saving && onClose()}
      size="sm"
      title="Guruhni almashtirish"
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor
          </Button>
          <Button onClick={handleSave} disabled={!toGroupId || saving}>
            <ArrowLeftRight className="h-4 w-4" /> {saving ? 'Saqlanmoqda...' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          <span className="font-medium text-slate-800">{studentName}</span>
        </p>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Aktiv guruhi (hozirgi)</span>
          <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-700">
            {fromGroupName}
          </div>
        </div>

        <Select
          label="O'qituvchi"
          value={teacherId}
          onChange={(e) => {
            // O'qituvchi o'zgarsa, oldingi guruh tanlovi tozalanadi (boshqa o'qituvchiga tegishli edi).
            setTeacherId(e.target.value)
            setToGroupId('')
          }}
        >
          <option value="">— tanlanmagan —</option>
          {teacherOptions.map((t) => (
            <option key={t.id} value={t.id}>
              {t.fullName}
            </option>
          ))}
        </Select>

        <Select
          label="Yangi guruh"
          value={toGroupId}
          disabled={!teacherId}
          onChange={(e) => setToGroupId(e.target.value)}
        >
          <option value="">{teacherId ? '— guruh tanlang —' : '— avval o\'qituvchini tanlang —'}</option>
          {groupsForTeacher.map((g) => (
            <option key={g.id} value={g.id}>
              {g.name}
            </option>
          ))}
        </Select>

        {toGroupId && (
          <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">
                Eski guruhda muzlatish sanasi
              </span>
              <input
                type="date"
                value={freezeDate}
                onChange={(e) => setFreezeDate(e.target.value)}
                className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
            </label>
            <label className="block">
              <span className="mb-1 block text-sm font-medium text-slate-600">
                Yangi guruhda aktivlashtirish sanasi
              </span>
              <input
                type="date"
                value={activateDate}
                onChange={(e) => setActivateDate(e.target.value)}
                className="w-full rounded-lg border border-slate-200 px-3 py-2 text-sm text-slate-700 outline-none focus:border-brand-400"
              />
            </label>
          </div>
        )}

        {error && <p className="text-sm text-red-600">{error}</p>}

        <p className="text-xs text-slate-400">
          Saqlanganda: eski guruhda shu sanagacha qatnashgan darslar uchun qisman to'lov hisoblanib
          a'zolik muzlatiladi; yangi guruhda esa shu sanadan qisman oylik hisoblanib darhol
          aktivlashtiriladi. O'quvchi shu oy uchun eski guruhga allaqachon to'lagan bo'lsa —
          ortib qolgan summa avtomatik YANGI guruhga o'tkaziladi (to'lagan o'quvchi yangi guruhda
          qarzdor bo'lib ko'rinmasligi uchun).
        </p>
      </div>
    </Modal>
  )
}
