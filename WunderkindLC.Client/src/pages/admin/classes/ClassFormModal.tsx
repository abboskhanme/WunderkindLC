import { useEffect, useState } from 'react'
import type { Group, Subject, Teacher, Room } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getRooms } from '@/api/services/rooms'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { languageOptions } from '@/config/constants'
import { cn } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: ClassPayload) => void
  initial?: Group | null
}

/** Hafta kunlari (0=Dushanba .. 6=Yakshanba) — backend kontrakti */
const dayLabels: { value: number; label: string }[] = [
  { value: 0, label: 'Du' },
  { value: 1, label: 'Se' },
  { value: 2, label: 'Cho' },
  { value: 3, label: 'Pay' },
  { value: 4, label: 'Ju' },
  { value: 5, label: 'Sha' },
  { value: 6, label: 'Yak' },
]

const empty: ClassPayload = {
  name: '',
  grade: 0,
  language: 'uz',
  monthlyFee: 0,
  room: '',
  roomId: '',
  status: 'active',
  startDate: '',
  endDate: '',
  capacity: 0,
  courseId: '',
  teacherId: '',
  note: '',
  days: [],
  startTime: '',
  endTime: '',
}

export function ClassFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<ClassPayload>(empty)
  const [subjects, setSubjects] = useState<Subject[]>([])
  const [teachers, setTeachers] = useState<Teacher[]>([])
  const [rooms, setRooms] = useState<Room[]>([])
  // Xona konflikt ogohlantirishini ko'rsatish uchun (backend 200 + roomConflict: true)
  const [roomConflict, setRoomConflict] = useState<{
    conflictList: string
    pendingSubmit: ClassPayload
  } | null>(null)

  useEffect(() => {
    if (!open) return
    Promise.all([getSubjects(), getTeachers(), getRooms()])
      .then(([s, t, r]) => {
        setSubjects(s)
        setTeachers(t)
        setRooms(r.filter((rm) => rm.isActive))
      })
      .catch(() => {})
  }, [open])

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setForm(
      initial
        ? {
            name: initial.name,
            grade: initial.grade,
            language: initial.language,
            monthlyFee: initial.monthlyFee,
            room: initial.room ?? '',
            roomId: initial.roomId ?? '',
            status: initial.status ?? 'active',
            startDate: initial.startDate ?? '',
            endDate: initial.endDate ?? '',
            capacity: initial.capacity ?? 0,
            courseId: initial.courseId ?? '',
            teacherId: initial.teacherId ?? '',
            note: initial.note ?? '',
            days: initial.days ?? [],
            startTime: initial.startTime ?? '',
            endTime: initial.endTime ?? '',
          }
        : empty,
    )
  }, [open, initial])

  const update = <K extends keyof ClassPayload>(key: K, value: ClassPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  /** Kurs tanlanganda oylik narxni avtomatik to'ldirish (tahrirlash mumkin). */
  const onCourseChange = (courseId: string) => {
    const course = subjects.find((s) => s.id === courseId)
    setForm((f) => ({
      ...f,
      courseId,
      monthlyFee: course ? course.price : f.monthlyFee,
    }))
  }

  const toggleDay = (day: number) =>
    setForm((f) => {
      const days = f.days ?? []
      return {
        ...f,
        days: days.includes(day) ? days.filter((d) => d !== day) : [...days, day].sort((a, b) => a - b),
      }
    })

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.name.trim()) return
    // O'qituvchi biriktirish MAJBURIY (foizli maosh, jurnal, hisobotlar shunga tayanadi).
    if (!form.teacherId) {
      alert("Guruhga o'qituvchi biriktirish majburiy")
      return
    }
    try {
      doSubmit(form)
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { message?: string } } })?.response?.data?.message
      alert(msg ?? 'Saqlashda xatolik yuz berdi')
    }
  }

  const doSubmit = (values: ClassPayload) => {
    onSubmit(values)
  }

  const handleConflictForce = () => {
    if (!roomConflict) return
    onSubmit(roomConflict.pendingSubmit)
    setRoomConflict(null)
  }

  return (
    <>
    {/* Xona konflikt ogohlantiruv modali */}
    {roomConflict && (
      <Modal
        open
        onClose={() => setRoomConflict(null)}
        title="Xonada jadval konflikti!"
        size="sm"
        footer={
          <>
            <Button variant="secondary" onClick={() => setRoomConflict(null)}>
              Bekor qilish
            </Button>
            <Button variant="danger" onClick={handleConflictForce}>
              Baribir saqlash
            </Button>
          </>
        }
      >
        <div className="space-y-3">
          <p className="text-sm text-red-700 font-medium">
            Tanlangan xona shu vaqt oralig'ida band:
          </p>
          <p className="text-sm text-slate-600">{roomConflict.conflictList}</p>
          <p className="text-sm text-slate-500">Davom etishni xohlaysizmi?</p>
        </div>
      </Modal>
    )}
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Guruhni tahrirlash' : 'Yangi guruh'}
      size="md"
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="class-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="class-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="Guruh nomi"
          required
          placeholder="Masalan: Ingliz tili — ertalabki"
          value={form.name}
          onChange={(e) => update('name', e.target.value)}
        />

        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Kurs"
            value={form.courseId ?? ''}
            onChange={(e) => onCourseChange(e.target.value)}
          >
            <option value="">Tanlang...</option>
            {subjects.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </Select>
          <Input
            label="Oylik narx (so'm)"
            type="number"
            min={0}
            step="any"
            value={form.monthlyFee}
            onChange={(e) => update('monthlyFee', Number(e.target.value))}
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Select
            label="O'qituvchi *"
            value={form.teacherId ?? ''}
            onChange={(e) => update('teacherId', e.target.value)}
          >
            <option value="">Tanlang...</option>
            {teachers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </Select>
          <Select
            label="Xona"
            value={form.roomId ?? ''}
            onChange={(e) => {
              const selectedRoom = rooms.find((r) => r.id === e.target.value)
              setForm((f) => ({
                ...f,
                roomId: e.target.value,
                room: selectedRoom?.name ?? '',
              }))
            }}
          >
            <option value="">Tanlang...</option>
            {rooms.map((r) => (
              <option key={r.id} value={r.id}>
                {r.name}{r.building ? ` (${r.building})` : ''} — {r.capacity} o'rin
              </option>
            ))}
          </Select>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Til"
            value={form.language}
            onChange={(e) => update('language', e.target.value as ClassPayload['language'])}
          >
            {languageOptions.map((l) => (
              <option key={l.value} value={l.value}>
                {l.label}
              </option>
            ))}
          </Select>
          <Input
            label="Tashkil topgan sana"
            type="date"
            value={form.startDate ?? ''}
            onChange={(e) => update('startDate', e.target.value)}
          />
        </div>

        <div>
          <span className="mb-1 block text-sm font-medium text-slate-600">Hafta kunlari</span>
          <div className="flex flex-wrap gap-1.5">
            {dayLabels.map((d) => {
              const active = (form.days ?? []).includes(d.value)
              return (
                <button
                  key={d.value}
                  type="button"
                  onClick={() => toggleDay(d.value)}
                  className={cn(
                    'rounded-lg border px-3 py-1.5 text-sm font-medium transition-colors',
                    active
                      ? 'border-brand-400 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-500 hover:bg-slate-50',
                  )}
                >
                  {d.label}
                </button>
              )
            })}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Boshlanish vaqti"
            type="time"
            value={form.startTime ?? ''}
            onChange={(e) => update('startTime', e.target.value)}
          />
          <Input
            label="Tugash vaqti"
            type="time"
            value={form.endTime ?? ''}
            onChange={(e) => update('endTime', e.target.value)}
          />
        </div>

        <div className="grid grid-cols-2 gap-4">
          <Select
            label="Holat"
            value={form.status ?? 'active'}
            onChange={(e) => update('status', e.target.value as Group['status'])}
          >
            <option value="active">Faol</option>
            <option value="full">To'lgan</option>
            <option value="archived">Arxiv</option>
          </Select>
          <Input
            label="Sig'im (0 = cheksiz)"
            type="number"
            min={0}
            value={form.capacity ?? 0}
            onChange={(e) => update('capacity', Number(e.target.value))}
          />
        </div>

        <Textarea
          label="Izoh"
          rows={2}
          placeholder="Qo'shimcha ma'lumot"
          value={form.note ?? ''}
          onChange={(e) => update('note', e.target.value)}
        />
      </form>
    </Modal>
    </>
  )
}
