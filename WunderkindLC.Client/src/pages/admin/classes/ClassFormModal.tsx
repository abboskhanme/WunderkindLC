import { useEffect, useState } from 'react'
import type { Group, Subject, Teacher, Room } from '@/types'
import type { ClassPayload } from '@/api/services/classes'
import { getSubjects } from '@/api/services/subjects'
import { getTeachers } from '@/api/services/teachers'
import { getRooms } from '@/api/services/rooms'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { EduFormModal, EduField } from '@/components/ui/list/EduFormModal'
import { eduInputCls, eduTextareaCls } from '@/components/ui/list/eduStyles'
import { MoneyInput, NumberInput } from '@/components/ui/NumberInput'
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

// Narx va sig'im maydonlari BO'SH bo'la oladi — saqlashda aylantiriladi.
type ClassForm = Omit<ClassPayload, 'monthlyFee' | 'capacity'> & {
  monthlyFee: number | null
  capacity: number | null
}

const empty: ClassForm = {
  name: '',
  grade: 0,
  language: 'uz',
  monthlyFee: null,
  room: '',
  roomId: '',
  status: 'active',
  startDate: '',
  endDate: '',
  capacity: null,
  courseId: '',
  teacherId: '',
  note: '',
  days: [],
  startTime: '',
  endTime: '',
}

export function ClassFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<ClassForm>(empty)
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
            capacity: initial.capacity ?? null,
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

  const update = <K extends keyof ClassForm>(key: K, value: ClassForm[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  /**
   * Kurs tanlangan bo'lsa guruh oyligi KURS NARXI — server saqlashda baribir `MonthlyFee` ni kurs
   * narxi bilan almashtiradi. Ilgari maydon tahrirlanadigan edi va kiritilgan son jimgina
   * yo'qolardi; endi u faqat ko'rsatiladi (narx kursda o'zgartiriladi).
   */
  const selectedCourse = form.courseId ? subjects.find((s) => s.id === form.courseId) : undefined
  const feeLockedByCourse = !!selectedCourse
  const shownFee = selectedCourse ? selectedCourse.price : form.monthlyFee

  /** Kurs tanlanganda oylik narx kurs narxidan olinadi (kurs olib tashlansa — oldingi qiymat qoladi). */
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
    if (form.endDate && form.startDate && form.endDate < form.startDate) {
      alert("Tugash sanasi boshlanish sanasidan oldin bo'lmaydi")
      return
    }
    // Bo'sh narx jimgina 0 deb saqlanmaydi (bepul guruh bo'lsa 0 aniq yoziladi).
    if (shownFee === null || shownFee === undefined) {
      alert("Oylik narxni kiriting (bepul bo'lsa 0 yozing)")
      return
    }
    try {
      // Sig'im bo'sh = 0 = cheksiz. Kurs tanlangan bo'lsa narx — kurs narxi (server ham shuni yozadi),
      // ya'ni "oylik o'zgardimi" so'rovi faqat HAQIQIY o'zgarishda chiqadi.
      doSubmit({ ...form, monthlyFee: shownFee, capacity: form.capacity ?? 0 })
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
    {/* edutizim ko'rinishi: "Yangi guruh qo'shish" / "Guruhni tahrirlash" — yorliq tepada, kulrang
        to'ldirilgan maydonlar, "Orqaga" / "Saqlash". Maydonlar va tekshiruv AVVALGIDEK (bizning
        guruh modeli), xona/o'qituvchi to'qnashuvi ogohlantirishi chaqiruvchida (onSubmit). */}
    <EduFormModal
      open={open}
      onClose={onClose}
      title={initial ? 'Guruhni tahrirlash' : "Yangi guruh qo'shish"}
      formId="class-form"
    >
      <form id="class-form" onSubmit={handleSubmit}>
        <EduField label="Guruh nomi" required>
          <input
            className={eduInputCls}
            required
            placeholder="Masalan: Ingliz tili — ertalabki"
            value={form.name}
            onChange={(e) => update('name', e.target.value)}
          />
        </EduField>

        <EduField
          label="Guruh holati"
          required
          hint={
            // «Arxiv» bu yerda faqat YORLIQ edi (guruh arxivga tushmas, a'zoliklar muzlamas, oylik
            // yozilaverardi) — server endi uni rad etadi. Arxivlash alohida amal.
            "Guruhni arxivlash — guruhlar ro'yxatidagi «Arxivlash» yoki guruh sahifasidagi «Guruhni yopish» orqali (a'zoliklar shu sanadan muzlatiladi)."
          }
        >
          <select
            className={eduInputCls}
            value={form.status ?? 'active'}
            onChange={(e) => update('status', e.target.value as Group['status'])}
          >
            <option value="active">Faol</option>
            <option value="full">To'lgan</option>
            {/* Faqat allaqachon «Arxiv» yorlig'idagi guruh uchun ko'rinadi (tanlab bo'lmaydi). */}
            {(initial?.status === 'archived' || initial?.isArchived) && (
              <option value="archived" disabled>
                Arxiv
              </option>
            )}
          </select>
        </EduField>

        <div className="grid grid-cols-2 gap-3">
          <EduField label="Kurs">
            <select className={eduInputCls} value={form.courseId ?? ''} onChange={(e) => onCourseChange(e.target.value)}>
              <option value="">Tanlang</option>
              {subjects.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </select>
          </EduField>
          <EduField
            label="Oylik narx (so'm)"
            hint={feeLockedByCourse ? "Kurs narxidan olinadi — narx «Kurslar» bo'limida o'zgartiriladi" : undefined}
          >
            <MoneyInput
              as="input"
              className={cn(eduInputCls, feeLockedByCourse && 'cursor-not-allowed opacity-70')}
              value={shownFee}
              readOnly={feeLockedByCourse}
              onChange={(v) => {
                if (!feeLockedByCourse) update('monthlyFee', v)
              }}
            />
          </EduField>
        </div>

        <div className="mb-3">
          <span className="mb-1 block text-[15px] font-medium text-black">Dars kunini tanlang</span>
          <div className="flex flex-wrap gap-1.5">
            {/* Tez tanlov — edutizimdagi "Toq kunlar" / "Juft kunlar" (Du/Cho/Ju · Se/Pay/Sha). */}
            <button
              type="button"
              onClick={() => update('days', [0, 2, 4])}
              className="h-8 rounded-lg bg-[#f0f2f2] px-3 text-[13px] font-medium text-[#333] transition-colors hover:bg-[#e6eaea]"
            >
              Toq kunlar
            </button>
            <button
              type="button"
              onClick={() => update('days', [1, 3, 5])}
              className="h-8 rounded-lg bg-[#f0f2f2] px-3 text-[13px] font-medium text-[#333] transition-colors hover:bg-[#e6eaea]"
            >
              Juft kunlar
            </button>
            <span className="mx-1 w-px self-stretch bg-[#dbe0e6]" />
            {dayLabels.map((d) => {
              const active = (form.days ?? []).includes(d.value)
              return (
                <button
                  key={d.value}
                  type="button"
                  onClick={() => toggleDay(d.value)}
                  className={cn(
                    'h-8 min-w-10 rounded-lg px-2.5 text-[13px] font-medium transition-colors',
                    active ? 'bg-brand-600 text-white' : 'bg-[#f0f2f2] text-[#333] hover:bg-[#e6eaea]',
                  )}
                >
                  {d.label}
                </button>
              )
            })}
          </div>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <EduField label="Boshlanish vaqti">
            <input
              className={eduInputCls}
              type="time"
              value={form.startTime ?? ''}
              onChange={(e) => update('startTime', e.target.value)}
            />
          </EduField>
          <EduField label="Tugash vaqti">
            <input
              className={eduInputCls}
              type="time"
              value={form.endTime ?? ''}
              onChange={(e) => update('endTime', e.target.value)}
            />
          </EduField>
        </div>

        <EduField label="O'qituvchi" required>
          <select className={eduInputCls} value={form.teacherId ?? ''} onChange={(e) => update('teacherId', e.target.value)}>
            <option value="">Tanlang</option>
            {teachers.map((t) => (
              <option key={t.id} value={t.id}>
                {t.fullName}
              </option>
            ))}
          </select>
        </EduField>

        <div className="grid grid-cols-2 gap-3">
          <EduField label="Xona">
            <select
              className={eduInputCls}
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
              <option value="">Tanlang</option>
              {rooms.map((r) => (
                <option key={r.id} value={r.id}>
                  {r.name}{r.building ? ` (${r.building})` : ''} — {r.capacity} o'rin
                </option>
              ))}
            </select>
          </EduField>
          <EduField label="Til">
            <select
              className={eduInputCls}
              value={form.language}
              onChange={(e) => update('language', e.target.value as ClassPayload['language'])}
            >
              {languageOptions.map((l) => (
                <option key={l.value} value={l.value}>
                  {l.label}
                </option>
              ))}
            </select>
          </EduField>
        </div>

        <div className="grid grid-cols-2 gap-3">
          <EduField label="Boshlanish sanasi">
            <input
              className={eduInputCls}
              type="date"
              value={form.startDate ?? ''}
              onChange={(e) => update('startDate', e.target.value)}
            />
          </EduField>
          <EduField
            label="Tugash sanasi"
            hint="Bo'sh — guruh davom etadi. Shu sanadan keyin darslar jadvalda ko'rinmaydi."
          >
            <input
              className={eduInputCls}
              type="date"
              value={form.endDate ?? ''}
              min={form.startDate || undefined}
              onChange={(e) => update('endDate', e.target.value)}
            />
          </EduField>
        </div>

        <EduField label="Sig'im (0 = cheksiz)">
          <NumberInput
            as="input"
            className={eduInputCls}
            value={form.capacity}
            onChange={(v) => update('capacity', v)}
          />
        </EduField>

        <EduField label="Izoh">
          <textarea
            className={eduTextareaCls}
            rows={2}
            placeholder="Qo'shimcha ma'lumot"
            value={form.note ?? ''}
            onChange={(e) => update('note', e.target.value)}
          />
        </EduField>
      </form>
    </EduFormModal>
    </>
  )
}
