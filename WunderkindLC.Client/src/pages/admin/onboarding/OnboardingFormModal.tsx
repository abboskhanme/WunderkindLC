import { useState } from 'react'
import { Loader2, Save } from 'lucide-react'
import type {
  EmployeeOnboarding, EmployeeOnboardingPayload, OnboardingPerson,
} from '@/api/services/onboarding'
import { createOnboarding, updateOnboarding } from '@/api/services/onboarding'
import { Button } from '@/components/ui/Button'
import { Modal } from '@/components/ui/Modal'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { apiErrorMessage } from '@/lib/utils'
import { decodeAccount, encodeAccount, groupPeople, todayIso } from './onboardingModel'

interface Props {
  /** null — yangi (qo'lda) moslashuv; aks holda sarlavhani tahrirlash */
  initial: EmployeeOnboarding | null
  people: OnboardingPerson[]
  onClose: () => void
  onSaved: (o: EmployeeOnboarding) => void
}

/** Telefon: bazada "998901234567" yoki boshqa ko'rinishda — PhoneInput faqat raqamlarni oladi. */
const digits = (s: string) => s.replace(/\D/g, '')

/**
 * "YANGI XODIM MOSLASHUVI" (qo'lda boshlash) va sarlavhani tahrirlash.
 * Oyna har ochilishda `key` bilan QAYTA yaratiladi — boshlang'ich qiymatlar `useState`
 * initializer'ida (effekt ichida setState yo'q). Xato bo'lsa forma yopilmaydi, matn qoladi.
 */
export function OnboardingFormModal({ initial, people, onClose, onSaved }: Props) {
  const [fullName, setFullName] = useState(initial?.fullName ?? '')
  const [phone, setPhone] = useState(digits(initial?.phone ?? ''))
  const [position, setPosition] = useState(initial?.position ?? '')
  const [startDate, setStartDate] = useState(initial?.startDate || todayIso())
  const [mentorId, setMentorId] = useState(initial?.mentorId ?? '')
  const [account, setAccount] = useState(encodeAccount(initial?.teacherId ?? '', initial?.staffUserId ?? ''))
  const [note, setNote] = useState(initial?.note ?? '')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const { teachers, staff } = groupPeople(people)

  const submit = async () => {
    if (busy) return
    const name = fullName.trim()
    if (name.length < 3) {
      setError("Xodimning F.I.Sh. ni kiriting")
      return
    }
    if (!/^\d{4}-\d{2}-\d{2}$/.test(startDate)) {
      setError('Boshlanish sanasini tanlang')
      return
    }
    const payload: EmployeeOnboardingPayload = {
      fullName: name,
      phone,
      position: position.trim(),
      startDate,
      mentorId,
      ...decodeAccount(account),
      note: note.trim(),
    }
    setBusy(true)
    setError('')
    try {
      const saved = initial ? await updateOnboarding(initial.id, payload) : await createOnboarding(payload)
      onSaved(saved)
      onClose()
    } catch (err) {
      setError(apiErrorMessage(err, "Moslashuvni saqlab bo'lmadi"))
    } finally {
      setBusy(false)
    }
  }

  return (
    <Modal
      open
      onClose={onClose}
      size="md"
      title={initial ? 'Moslashuvni tahrirlash' : 'Yangi xodim moslashuvi'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={busy}>
            Bekor qilish
          </Button>
          <Button onClick={submit} disabled={busy}>
            {busy ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {initial ? 'Saqlash' : 'Boshlash'}
          </Button>
        </>
      }
    >
      <div className="space-y-3">
        {error && <p className="text-sm font-medium text-red-600">{error}</p>}
        {!initial && (
          <p className="rounded-lg bg-slate-50 px-3 py-2 text-xs text-slate-500">
            Ariza «Ishga qabul qilindi» bosqichiga o'tganda moslashuv o'zi boshlanadi. Bu yerdan —
            vakansiyasiz qabul qilingan xodim uchun. Cheklist qadamlar katalogidan nusxa olinadi.
          </p>
        )}
        <Input label="F.I.Sh." required value={fullName} onChange={(e) => setFullName(e.target.value)} />
        <div className="grid gap-3 sm:grid-cols-2">
          <PhoneInput label="Telefon" value={phone} onChange={setPhone} />
          <Input
            label="Lavozim"
            value={position}
            onChange={(e) => setPosition(e.target.value)}
            placeholder="Ingliz tili o'qituvchisi"
          />
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <Input
            label="Boshlanish sanasi"
            required
            type="date"
            value={startDate}
            onChange={(e) => setStartDate(e.target.value)}
          />
          <Select label="Mentor" value={mentorId} onChange={(e) => setMentorId(e.target.value)}>
            <option value="">— biriktirilmagan —</option>
            {teachers.length > 0 && (
              <optgroup label="O'qituvchilar">
                {teachers.map((p) => (
                  <option key={p.id} value={p.id}>{p.fullName}</option>
                ))}
              </optgroup>
            )}
            {staff.length > 0 && (
              <optgroup label="Xodimlar">
                {staff.map((p) => (
                  <option key={p.id} value={p.id}>{p.fullName}</option>
                ))}
              </optgroup>
            )}
          </Select>
        </div>
        <Select
          label="Tizimdagi yozuvi (ixtiyoriy)"
          value={account}
          onChange={(e) => setAccount(e.target.value)}
        >
          <option value="">— bog'lanmagan —</option>
          {teachers.length > 0 && (
            <optgroup label="O'qituvchi sifatida">
              {teachers.map((p) => (
                <option key={p.id} value={`teacher:${p.id}`}>{p.fullName}</option>
              ))}
            </optgroup>
          )}
          {staff.length > 0 && (
            <optgroup label="Panel xodimi sifatida">
              {staff.map((p) => (
                <option key={p.id} value={`staff:${p.id}`}>{p.fullName}</option>
              ))}
            </optgroup>
          )}
        </Select>
        {initial && initial.startDate !== startDate && (
          <p className="text-xs text-amber-600">
            Boshlanish sanasi o'zgarsa barcha qadam muddatlari shunga qarab siljiydi.
          </p>
        )}
        <Textarea label="Izoh" rows={3} value={note} onChange={(e) => setNote(e.target.value)} />
      </div>
    </Modal>
  )
}
