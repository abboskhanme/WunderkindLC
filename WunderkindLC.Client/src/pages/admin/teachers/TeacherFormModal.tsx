import { useEffect, useState } from 'react'
import { Camera } from 'lucide-react'
import type { Subject, Teacher } from '@/types'
import type { TeacherPayload } from '@/api/services/teachers'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { PhotoUpload } from '@/components/ui/PhotoUpload'
import { PhotoDialog } from '@/components/media/PhotoDialog'
import { genderOptions, teacherPermissions } from '@/config/constants'
import { cn, randomPassword } from '@/lib/utils'

interface Props {
  open: boolean
  onClose: () => void
  onSubmit: (values: TeacherPayload) => void
  initial?: Teacher | null
  subjects: Subject[]
}

const empty: TeacherPayload = {
  fullName: '',
  birthDate: '',
  address: '',
  gender: 'male',
  phone: '',
  homeroomClass: '',
  subjectIds: [],
  salaryMode: 'fixed',
  salary: 0,
  salaryPercent: 0,
  category: '',
  salaryStartMonth: '',
  salaryStartDate: '',
  photoUrl: null,
  isSupport: false,
  // Yangi o'qituvchiga standart — barcha bo'limlar ochiq.
  permissions: teacherPermissions.map((p) => p.key),
}

export function TeacherFormModal({ open, onClose, onSubmit, initial, subjects }: Props) {
  const [form, setForm] = useState<TeacherPayload>(empty)
  /** «Kameradan olish» oynasi (o'quvchi formasidagi bilan bir xil komponent). */
  const [photoOpen, setPhotoOpen] = useState(false)

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani initial bilan sinxronlash (maqsadli)
    setForm(
      initial
        ? {
            fullName: initial.fullName,
            birthDate: initial.birthDate,
            address: initial.address,
            gender: initial.gender,
            phone: initial.phone ?? '',
            homeroomClass: initial.homeroomClass,
            subjectIds: [...initial.subjectIds],
            // Maosh rejimi/foizi — "Oylik hisoblash" sahifasida tahrirlanadi; bu yerda saqlanib qoladi (reset bo'lmaydi).
            salaryMode: initial.salaryMode ?? 'fixed',
            salary: initial.salary,
            salaryPercent: initial.salaryPercent ?? 0,
            category: initial.category ?? '',
            salaryStartMonth: initial.salaryStartMonth ?? '',
            salaryStartDate: initial.salaryStartDate ?? '',
            photoUrl: initial.photoUrl ?? null,
            isSupport: initial.isSupport ?? false,
            permissions: [...(initial.permissions ?? [])],
          }
        : empty,
    )
  }, [open, initial])

  const update = <K extends keyof TeacherPayload>(key: K, value: TeacherPayload[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  const toggleSubject = (id: string) =>
    setForm((f) => ({
      ...f,
      subjectIds: f.subjectIds.includes(id)
        ? f.subjectIds.filter((x) => x !== id)
        : [...f.subjectIds, id],
    }))

  const togglePermission = (key: string) =>
    setForm((f) => ({
      ...f,
      permissions: f.permissions.includes(key)
        ? f.permissions.filter((x) => x !== key)
        : [...f.permissions, key],
    }))

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!form.fullName.trim()) return
    onSubmit(form)
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? "O'qituvchini tahrirlash" : "Yangi o'qituvchi"}
      footer={
        <>
          <Button variant="secondary" onClick={onClose}>
            Bekor qilish
          </Button>
          <Button type="submit" form="teacher-form">
            Saqlash
          </Button>
        </>
      }
    >
      <form id="teacher-form" onSubmit={handleSubmit} className="space-y-4">
        <Input
          label="F.I.SH"
          required
          value={form.fullName}
          onChange={(e) => update('fullName', e.target.value)}
        />
        <div>
          <PhotoUpload
            label="O'qituvchi rasmi"
            value={form.photoUrl ?? null}
            onChange={(url) => update('photoUrl', url)}
          />
          {/* KAMERADAN olish — o'quvchi formasidagi bilan AYNAN bir xil (bitta PhotoDialog). */}
          <button
            type="button"
            onClick={() => setPhotoOpen(true)}
            className="mt-1.5 inline-flex items-center gap-1.5 text-xs font-medium text-brand-600 hover:underline"
          >
            <Camera className="h-3.5 w-3.5" /> Kameradan olish
          </button>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Tug'ilgan kun"
            type="date"
            value={form.birthDate}
            onChange={(e) => update('birthDate', e.target.value)}
          />
          <Select
            label="Jinsi"
            value={form.gender}
            onChange={(e) => update('gender', e.target.value as TeacherPayload['gender'])}
          >
            {genderOptions.map((g) => (
              <option key={g.value} value={g.value}>
                {g.label}
              </option>
            ))}
          </Select>
        </div>
        <div className="grid grid-cols-2 gap-4">
          <Input
            label="Manzil"
            value={form.address}
            onChange={(e) => update('address', e.target.value)}
          />
          <PhoneInput
            label="Telefon"
            value={form.phone ?? ''}
            onChange={(phone) => update('phone', phone)}
          />
        </div>
        <div>
          <Input
            label="Maosh qaysi kundan hisoblansin"
            type="date"
            value={form.salaryStartDate ?? ''}
            onChange={(e) => update('salaryStartDate', e.target.value)}
          />
          <p className="mt-1 text-xs text-slate-400">
            Oylik maosh — o'qituvchini ochib <b>"Maosh"</b> tabida HAR GURUH uchun alohida belgilanadi
            (foiz yoki qat'iy summa); o'qituvchi oyligi guruhlar yig'indisi. O'qituvchi <b>oy o'rtasida</b> kelsa —
            shu kunni belgilang (qat'iy summa birinchi oy o'sha kundan oy oxirigacha qisman). Bo'sh = eng birinchi to'lov oyidan.
          </p>
        </div>

        <div>
          <span className="mb-2 block text-sm font-medium text-slate-600">Dars beradigan fanlar</span>
          <div className="flex flex-wrap gap-2">
            {subjects.map((s) => {
              const active = form.subjectIds.includes(s.id)
              return (
                <button
                  key={s.id}
                  type="button"
                  onClick={() => toggleSubject(s.id)}
                  className={cn(
                    'rounded-full border px-3 py-1 text-sm transition-colors',
                    active
                      ? 'border-brand-500 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {s.name}
                </button>
              )
            })}
            {subjects.length === 0 && (
              <p className="text-sm text-slate-400">Avval fan qo'shing</p>
            )}
          </div>
        </div>

        <div className="border-t border-slate-100 pt-4">
          <label className="flex cursor-pointer items-start gap-2.5">
            <input
              type="checkbox"
              checked={form.isSupport ?? false}
              onChange={(e) => update('isSupport', e.target.checked)}
              className="mt-0.5 h-4 w-4 rounded border-slate-300 text-brand-600 focus:ring-brand-500"
            />
            <span>
              <span className="block text-sm font-medium text-slate-700">Support o'qituvchi</span>
              <span className="block text-xs text-slate-400">
                Bo'sh vaqt slotlarini e'lon qiladi, o'quvchilar bron qiladi (Ilova → Support).
              </span>
            </span>
          </label>
        </div>

        <div className="border-t border-slate-100 pt-4">
          <span className="mb-1 block text-sm font-medium text-slate-600">
            Web panel bo'limlari (ruxsatlar)
          </span>
          <p className="mb-2 text-xs text-slate-400">
            O'qituvchi web panelida qaysi bo'limlardan foydalana olishini belgilang. "Bosh sahifa" har doim ochiq.
          </p>
          <div className="flex flex-wrap gap-2">
            {teacherPermissions.map((p) => {
              const active = form.permissions.includes(p.key)
              return (
                <button
                  key={p.key}
                  type="button"
                  onClick={() => togglePermission(p.key)}
                  className={cn(
                    'rounded-full border px-3 py-1 text-sm transition-colors',
                    active
                      ? 'border-brand-500 bg-brand-50 text-brand-700'
                      : 'border-slate-200 text-slate-600 hover:bg-slate-50',
                  )}
                >
                  {p.label}
                </button>
              )
            })}
          </div>
        </div>

        {initial && (
          <div className="border-t border-slate-100 pt-4">
            <span className="mb-1 block text-sm font-medium text-slate-600">Parolni almashtirish</span>
            <div className="flex items-start gap-2">
              <div className="flex-1">
                <Input
                  type="text"
                  autoComplete="new-password"
                  placeholder="Bo'sh qoldirilsa — parol o'zgarmaydi"
                  value={form.newPassword ?? ''}
                  onChange={(e) => update('newPassword', e.target.value)}
                />
              </div>
              <Button
                type="button"
                variant="secondary"
                onClick={() => update('newPassword', randomPassword())}
              >
                Generatsiya
              </Button>
            </div>
            <p className="mt-1 text-xs text-slate-400">
              Login (username) o'zgarmaydi. Yangi parolni kiriting yoki generatsiya qiling — saqlangach
              o'qituvchiga topshiring.
            </p>
          </div>
        )}
      </form>

      {/* Rasm oynasi — FAQAT faylni yuklaydi va manzilni qaytaradi; bazaga forma saqlanganda
          umumiy payload bilan tushadi (o'quvchi formasidagi bilan bir xil qoida). */}
      <PhotoDialog
        open={photoOpen}
        currentUrl={form.photoUrl ?? null}
        startWithCamera
        title="O'qituvchi rasmi"
        hint="Doira ichidagi qism o'qituvchi profilida dumaloq avatar bo'lib chiqadi."
        onClose={() => setPhotoOpen(false)}
        onSaved={(url) => update('photoUrl', url)}
      />
    </Modal>
  )
}
