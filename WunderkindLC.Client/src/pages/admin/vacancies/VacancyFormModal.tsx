import { useEffect, useState } from 'react'
import { Loader2, Save } from 'lucide-react'
import type { EmploymentType, Vacancy, VacancyPayload } from '@/api/services/career'
import { createVacancy, updateVacancy } from '@/api/services/career'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { apiErrorMessage } from '@/lib/utils'
import { employmentOptions } from './careerLabels'

interface Props {
  open: boolean
  /** null — yangi vakansiya; aks holda tahrirlash */
  initial: Vacancy | null
  onClose: () => void
  onSaved: (v: Vacancy) => void
}

/**
 * Vakansiya yaratish/tahrirlash. Talablar/vazifalar/shart-sharoitlar — HAR QATORDA BITTA band
 * (Mini App ularni belgili ro'yxat qilib chizadi), shuning uchun alohida textarea'lar.
 * Holat (faol/arxiv) bu yerda YO'Q — u ro'yxatdagi "Arxivlash" amali orqali o'zgaradi.
 */
export function VacancyFormModal({ open, initial, onClose, onSaved }: Props) {
  const [title, setTitle] = useState('')
  const [department, setDepartment] = useState('')
  const [employmentType, setEmploymentType] = useState<EmploymentType>('full')
  const [location, setLocation] = useState('')
  const [salaryFrom, setSalaryFrom] = useState('')
  const [salaryTo, setSalaryTo] = useState('')
  const [salaryNote, setSalaryNote] = useState('')
  const [description, setDescription] = useState('')
  const [requirements, setRequirements] = useState('')
  const [responsibilities, setResponsibilities] = useState('')
  const [conditions, setConditions] = useState('')
  const [deadline, setDeadline] = useState('')
  const [order, setOrder] = useState('0')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda formani to'ldirish (maqsadli)
    setTitle(initial?.title ?? '')
    setDepartment(initial?.department ?? '')
    setEmploymentType(initial?.employmentType ?? 'full')
    setLocation(initial?.location ?? '')
    setSalaryFrom(initial?.salaryFrom ? String(initial.salaryFrom) : '')
    setSalaryTo(initial?.salaryTo ? String(initial.salaryTo) : '')
    setSalaryNote(initial?.salaryNote ?? '')
    setDescription(initial?.description ?? '')
    setRequirements(initial?.requirements ?? '')
    setResponsibilities(initial?.responsibilities ?? '')
    setConditions(initial?.conditions ?? '')
    setDeadline(initial?.deadline ?? '')
    setOrder(String(initial?.order ?? 0))
    setError('')
  }, [open, initial])

  const submit = async () => {
    if (busy) return
    const name = title.trim()
    if (!name) {
      setError('Lavozim nomini kiriting')
      return
    }
    const from = Number(salaryFrom || 0)
    const to = Number(salaryTo || 0)
    if (!Number.isFinite(from) || from < 0 || !Number.isFinite(to) || to < 0) {
      setError("Maoshni to'g'ri kiriting")
      return
    }
    if (to > 0 && from > to) {
      setError("Maoshning quyi chegarasi yuqori chegaradan katta bo'lmasin")
      return
    }

    const payload: VacancyPayload = {
      title: name,
      department: department.trim(),
      employmentType,
      location: location.trim(),
      salaryFrom: from,
      salaryTo: to,
      salaryNote: salaryNote.trim(),
      description: description.trim(),
      requirements: requirements.trim(),
      responsibilities: responsibilities.trim(),
      conditions: conditions.trim(),
      deadline: deadline.trim(),
      order: Number(order || 0),
    }

    setBusy(true)
    setError('')
    try {
      const saved = initial ? await updateVacancy(initial.id, payload) : await createVacancy(payload)
      onSaved(saved)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Vakansiyani saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      size="lg"
      title={initial ? 'Vakansiyani tahrirlash' : 'Yangi vakansiya'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            Saqlash
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Lavozim nomi"
            required
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder="Ingliz tili o'qituvchisi"
          />
          <Input
            label="Bo'lim / yo'nalish"
            value={department}
            onChange={(e) => setDepartment(e.target.value)}
            placeholder="O'quv bo'limi"
          />
        </div>

        <div className="grid gap-3 sm:grid-cols-2">
          <Select
            label="Bandlik turi"
            value={employmentType}
            onChange={(e) => setEmploymentType(e.target.value as EmploymentType)}
          >
            {employmentOptions.map((o) => (
              <option key={o.value} value={o.value}>
                {o.label}
              </option>
            ))}
          </Select>
          <Input
            label="Ish joyi / filial"
            value={location}
            onChange={(e) => setLocation(e.target.value)}
            placeholder="Qo'qon, markaziy filial"
          />
        </div>

        <div className="grid gap-3 sm:grid-cols-3">
          <Input
            label="Maosh (dan)"
            type="number"
            min={0}
            value={salaryFrom}
            onChange={(e) => setSalaryFrom(e.target.value)}
            placeholder="4000000"
          />
          <Input
            label="Maosh (gacha)"
            type="number"
            min={0}
            value={salaryTo}
            onChange={(e) => setSalaryTo(e.target.value)}
            placeholder="8000000"
          />
          <Input
            label="Maosh izohi"
            value={salaryNote}
            onChange={(e) => setSalaryNote(e.target.value)}
            placeholder="Kelishilgan holda"
          />
        </div>
        <p className="-mt-2 text-xs text-slate-400">
          Raqamlar bo'sh qoldirilsa, ilovada maosh izohi ko'rsatiladi.
        </p>

        <Textarea
          label="Qisqacha tavsif"
          rows={3}
          value={description}
          onChange={(e) => setDescription(e.target.value)}
          placeholder="Ish haqida bir-ikki jumla — vakansiyalar ro'yxatida ham shu ko'rinadi"
        />

        <Textarea
          label="Talablar"
          rows={4}
          value={requirements}
          onChange={(e) => setRequirements(e.target.value)}
          placeholder={"Har qatorda bitta talab, masalan:\nOliy ma'lumot\nKamida 1 yil tajriba\nIELTS 6.5+"}
        />

        <Textarea
          label="Vazifalar"
          rows={4}
          value={responsibilities}
          onChange={(e) => setResponsibilities(e.target.value)}
          placeholder={'Har qatorda bitta vazifa'}
        />

        <Textarea
          label="Shart-sharoitlar"
          rows={4}
          value={conditions}
          onChange={(e) => setConditions(e.target.value)}
          placeholder={'Har qatorda bitta shart, masalan:\nIsh vaqti 09:00–18:00\nRasmiy ish haqi'}
        />

        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Ariza qabul qilish oxirgi sanasi"
            type="date"
            value={deadline}
            onChange={(e) => setDeadline(e.target.value)}
          />
          <Input
            label="Tartib raqami"
            type="number"
            value={order}
            onChange={(e) => setOrder(e.target.value)}
            placeholder="0"
          />
        </div>
        <p className="-mt-2 text-xs text-slate-400">
          Tartib raqami kichik bo'lgan vakansiya ilovada tepada turadi.
        </p>

        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
      </div>
    </Modal>
  )
}
