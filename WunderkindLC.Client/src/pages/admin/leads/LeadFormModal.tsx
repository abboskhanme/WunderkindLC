import { Children, useEffect, useState, type ReactNode } from 'react'
import type { District, Lead } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { genderOptions, leadSourceOptions } from '@/config/constants'
import { getLeadSources } from '@/api/services/leadSources'
import { getLeadCourses, type LeadAnswersPayload } from '@/api/services/leads'
import { getDistricts } from '@/api/services/districts'
import {
  getLeadAnswers,
  getLeadEntryForm,
  type LeadEntryField,
  type LeadEntryForm,
  type LeadEntryState,
} from '@/api/services/leadEntryForm'

export type LeadFormValues = Omit<Lead, 'id' | 'stage'>

interface Props {
  open: boolean
  onClose: () => void
  /**
   * Saqlash. ⚠️ Promise QAYTARADI: modal uni `await` qiladi — muvaffaqiyatda O'ZI yopiladi,
   * xatoda esa OCHIQ qoladi va xato matnini ko'rsatadi (ilgari xato umuman ko'rinmasdi:
   * menejer "saqlandi" deb o'ylab ketardi, lid esa yozilmagan bo'lardi).
   */
  onSubmit: (values: LeadFormValues, answers: LeadAnswersPayload) => Promise<void>
  /** Tahrirlash uchun mavjud lid, qo'shish uchun null */
  initial?: Lead | null
}

const empty: LeadFormValues = {
  fullName: '',
  gender: 'male',
  birthDate: '',
  phone: '',
  fatherFullName: '',
  fatherPhone: '',
  motherFullName: '',
  motherPhone: '',
  note: '',
  source: '',
  interestSubject: '',
  districtId: '',
  schoolId: '',
}

/** Standart maydon yorliqlari — XATO MATNI uchun ("«Otasi raqami» to'ldirilmagan"). */
const standardLabels: Record<string, string> = {
  fullName: 'F.I.SH',
  gender: 'Jinsi',
  birthDate: "Tug'ilgan kun",
  phone: "O'z telefon raqami",
  fatherFullName: 'Otasi F.I.SH',
  fatherPhone: 'Otasi raqami',
  motherFullName: 'Onasi F.I.SH',
  motherPhone: 'Onasi raqami',
  source: 'Manba',
  interestSubject: 'Qiziqqan fani (kurs)',
  districtId: 'Tuman',
  schoolId: 'Maktab',
  note: 'Izoh',
}

/** Serverdagi xato matni (axios), bo'lmasa umumiy matn. */
function errorText(err: unknown): string {
  const data = (err as { response?: { data?: unknown } })?.response?.data
  const msg =
    typeof data === 'string' ? data : (data as { message?: string } | undefined)?.message
  return typeof msg === 'string' && msg.trim()
    ? msg
    : "Saqlab bo'lmadi. Qayta urinib ko'ring."
}

/**
 * Ikki ustunli blok.
 *
 * ⚠️ Ustun soni KO'RINADIGAN maydonlar soniga qarab hisoblanadi: bittasi sozlamada yashirilgan
 * bo'lsa qolgani TO'LIQ enni egallaydi (yarim en + bo'sh katak "forma buzilgan"dek ko'rinardi),
 * ikkalasi ham yashirin bo'lsa blok umuman chizilmaydi (aks holda ortiqcha bo'shliq qolardi).
 */
function Pair({ children }: { children: ReactNode }) {
  const items = Children.toArray(children)
  if (items.length === 0) return null
  return (
    <div className={`grid gap-4 ${items.length > 1 ? 'grid-cols-1 sm:grid-cols-2' : 'grid-cols-1'}`}>
      {items}
    </div>
  )
}

/** Qo'shimcha savol — turiga qarab matn / raqam / select / radio / checkbox. */
function CustomAnswer({
  field,
  value,
  onSet,
  onToggle,
}: {
  field: LeadEntryField
  value: string[]
  onSet: (vals: string[]) => void
  onToggle: (val: string) => void
}) {
  const single = value[0] ?? ''

  if (field.kind === 'textarea')
    return (
      <Textarea
        label={field.label}
        required={field.required}
        rows={3}
        placeholder={field.placeholder}
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
      />
    )

  if (field.kind === 'number')
    return (
      <Input
        label={field.label}
        required={field.required}
        type="number"
        placeholder={field.placeholder}
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
      />
    )

  if (field.kind === 'select')
    return (
      <Select
        label={field.label}
        required={field.required}
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
      >
        <option value="">— tanlanmagan —</option>
        {field.options.map((o) => (
          <option key={o} value={o}>
            {o}
          </option>
        ))}
      </Select>
    )

  if (field.kind === 'radio' || field.kind === 'checkbox') {
    // checkbox — bir nechta javob, radio — bittasi (ommaviy formadagi bilan bir xil ko'rinish).
    const multiple = field.kind === 'checkbox'
    return (
      <div>
        <span className="mb-1 block text-sm font-medium text-slate-600">
          {field.label}
          {field.required && <span className="text-red-500"> *</span>}
        </span>
        <div className="space-y-2">
          {field.options.map((o) => {
            const selected = value.includes(o)
            return (
              <button
                key={o}
                type="button"
                onClick={() => (multiple ? onToggle(o) : onSet(selected ? [] : [o]))}
                className={
                  'flex w-full items-center gap-3 rounded-lg border px-3 py-2 text-left text-sm transition-colors ' +
                  (selected
                    ? 'border-brand-400 bg-brand-50 text-brand-800'
                    : 'border-slate-200 text-slate-700 hover:border-brand-300 hover:bg-slate-50')
                }
              >
                <span
                  className={
                    'h-4 w-4 shrink-0 border-2 ' +
                    (multiple ? 'rounded ' : 'rounded-full ') +
                    (selected ? 'border-brand-500 bg-brand-500' : 'border-slate-300')
                  }
                />
                <span className="flex-1">{o}</span>
              </button>
            )
          })}
        </div>
      </div>
    )
  }

  return (
    <Input
      label={field.label}
      required={field.required}
      placeholder={field.placeholder}
      value={single}
      onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
    />
  )
}

export function LeadFormModal({ open, onClose, onSubmit, initial }: Props) {
  const [form, setForm] = useState<LeadFormValues>(empty)
  // Manba ro'yxati serverdan ("O'quv bo'limi → Sabablar" → "Lid manbalari"); xato/bo'sh bo'lsa fallback.
  const [sourceOptions, setSourceOptions] = useState<string[]>(leadSourceOptions)
  // Tashqi maktab ma'lumotnomasi (tuman → maktablar) — o'quvchi formasidagi bilan bir xil.
  const [districts, setDistricts] = useState<District[]>([])
  // "Qiziqqan fani" ro'yxati — markazdagi kurslar (Subject nomlari).
  const [courseOptions, setCourseOptions] = useState<string[]>([])

  /**
   * «Lid kiritish formasi» sozlamasi (`/admin/forms/lid-kiritish`): qaysi standart maydon
   * ko'rinadi/majburiy va markaz qanday QO'SHIMCHA savol qo'shgan.
   *
   * ⚠️ ZAXIRA — sozlama yuklanmasa (tarmoq/server xatosi) `states` BO'SH qoladi: barcha standart
   * maydonlar ko'rinadi va faqat F.I.SH majburiy bo'ladi, ya'ni oyna avvalgidek ishlaydi.
   * Sozlama tushmagani uchun lid kiritish TO'XTAB qolmasin.
   */
  const [states, setStates] = useState<Record<string, LeadEntryState>>({})
  const [fields, setFields] = useState<LeadEntryField[]>([])
  // Qo'shimcha savollarga javoblar: { savolId: [javob...] }
  const [answers, setAnswers] = useState<LeadAnswersPayload>({})

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!open) return
    getDistricts()
      .then(setDistricts)
      .catch(() => setDistricts([]))
    getLeadCourses()
      .then(setCourseOptions)
      .catch(() => setCourseOptions([]))
    getLeadSources()
      .then((list) => {
        const names = list.map((s) => s.name)
        setSourceOptions(names.length > 0 ? names : leadSourceOptions)
      })
      .catch(() => setSourceOptions(leadSourceOptions))
  }, [open])

  /**
   * Sozlama + (tahrirlashda) lidning eski javoblari BIRGA yuklanadi: javoblar server tomonda
   * savol MATNI bilan saqlanadi (snapshot), demak ularni `id` ga aylantirish uchun aynan shu
   * so'rovda kelgan savollar ro'yxati kerak. Mos savol topilmasa (savol o'chirilgan yoki
   * nomi o'zgargan) javob JIM o'tkazib yuboriladi.
   */
  useEffect(() => {
    if (!open) return
    let cancelled = false
    const load = async () => {
      let cfg: LeadEntryForm
      try {
        cfg = await getLeadEntryForm()
      } catch {
        cfg = { standard: [], fields: [] }
      }
      if (cancelled) return
      const map: Record<string, LeadEntryState> = {}
      for (const s of cfg.standard) map[s.key] = s.state
      setStates(map)
      setFields(cfg.fields)

      if (!initial) {
        setAnswers({})
        return
      }
      try {
        const saved = await getLeadAnswers(initial.id)
        if (cancelled) return
        const next: LeadAnswersPayload = {}
        for (const a of saved) {
          const f = cfg.fields.find((x) => x.label === a.question)
          if (f) next[f.id] = a.answers
        }
        setAnswers(next)
      } catch {
        if (!cancelled) setAnswers({})
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [open, initial])

  useEffect(() => {
    if (!open) return
    // eslint-disable-next-line react-hooks/set-state-in-effect -- modal ochilganda xato va formani initial bilan sinxronlash (maqsadli)
    setError(null)
    // ⚠️ Forma HAR DOIM `initial` dan TO'LIQ to'ldiriladi — sozlamada yashirilgan maydon ham.
    // Uning qiymati keyin serverga o'sha holicha yuboriladi: menejer KO'RMAGAN ma'lumot
    // (masalan onasi raqami) tahrirlash paytida jimgina o'chib ketmasin.
    setForm(
      initial
        ? {
            fullName: initial.fullName,
            gender: initial.gender,
            birthDate: initial.birthDate,
            phone: initial.phone,
            fatherFullName: initial.fatherFullName,
            fatherPhone: initial.fatherPhone,
            motherFullName: initial.motherFullName,
            motherPhone: initial.motherPhone,
            note: initial.note ?? '',
            source: initial.source ?? '',
            interestSubject: initial.interestSubject ?? '',
            districtId: initial.districtId ?? '',
            schoolId: initial.schoolId ?? '',
          }
        : empty,
    )
  }, [open, initial])

  const update = <K extends keyof LeadFormValues>(key: K, value: LeadFormValues[K]) =>
    setForm((f) => ({ ...f, [key]: value }))

  /** Maydon holati. F.I.SH — DOIM majburiy; sozlamada topilmagan maydon ko'rinadi (ixtiyoriy). */
  const stateOf = (key: string): LeadEntryState =>
    key === 'fullName' ? 'required' : (states[key] ?? 'optional')
  const show = (key: string) => stateOf(key) !== 'hidden'
  const req = (key: string) => stateOf(key) === 'required'

  const setAnswer = (id: string, vals: string[]) => setAnswers((a) => ({ ...a, [id]: vals }))
  const toggleAnswer = (id: string, val: string) =>
    setAnswers((a) => {
      const cur = a[id] ?? []
      return { ...a, [id]: cur.includes(val) ? cur.filter((v) => v !== val) : [...cur, val] }
    })

  /**
   * Klientdagi oldindan tekshiruv — server baribir tekshiradi, bu faqat TEZKOR javob
   * (bekorga so'rov yubormasdan). Faqat KO'RINADIGAN majburiy maydonlar tekshiriladi:
   * yashirilgani menejerga ko'rsatilmagan, undan to'ldirishni talab qilib bo'lmaydi.
   */
  const validate = (): string | null => {
    for (const key of Object.keys(standardLabels)) {
      if (!show(key) || !req(key)) continue
      if (!String(form[key as keyof LeadFormValues] ?? '').trim())
        return `«${standardLabels[key]}» to'ldirilmagan.`
    }
    for (const f of fields) {
      if (!f.required) continue
      const vals = (answers[f.id] ?? []).filter((v) => v.trim())
      if (vals.length === 0) return `«${f.label}» to'ldirilmagan.`
    }
    return null
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (saving) return
    const problem = validate()
    if (problem) {
      setError(problem)
      return
    }
    setError(null)
    setSaving(true)
    try {
      await onSubmit(form, answers)
      onClose()
    } catch (err) {
      // Modal OCHIQ qoladi — kiritilgan ma'lumot yo'qolmasin, xato esa tepada ko'rinadi.
      setError(errorText(err))
    } finally {
      setSaving(false)
    }
  }

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={initial ? 'Lidni tahrirlash' : 'Yangi lid'}
      footer={
        <>
          <Button variant="secondary" onClick={onClose} disabled={saving}>
            Bekor qilish
          </Button>
          <Button type="submit" form="lead-form" disabled={saving}>
            {saving ? 'Saqlanmoqda…' : 'Saqlash'}
          </Button>
        </>
      }
    >
      <form id="lead-form" onSubmit={handleSubmit} className="space-y-4">
        {error && (
          <p className="rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-sm text-red-700">
            {error}
          </p>
        )}
        {/* F.I.SH — sozlamada yashirib ham, ixtiyoriy qilib ham bo'lmaydi (`locked`). */}
        <Input
          label="F.I.SH"
          required
          value={form.fullName}
          onChange={(e) => update('fullName', e.target.value)}
        />
        <Pair>
          {show('gender') && (
            <Select
              label="Jinsi"
              required={req('gender')}
              value={form.gender}
              onChange={(e) => update('gender', e.target.value as LeadFormValues['gender'])}
            >
              {genderOptions.map((g) => (
                <option key={g.value} value={g.value}>
                  {g.label}
                </option>
              ))}
            </Select>
          )}
          {show('birthDate') && (
            <Input
              label="Tug'ilgan kun"
              required={req('birthDate')}
              type="date"
              value={form.birthDate}
              onChange={(e) => update('birthDate', e.target.value)}
            />
          )}
        </Pair>
        {show('phone') && (
          <PhoneInput
            label="O'z telefon raqami"
            required={req('phone')}
            value={form.phone}
            onChange={(phone) => update('phone', phone)}
          />
        )}
        <Pair>
          {show('fatherFullName') && (
            <Input
              label="Otasi F.I.SH"
              required={req('fatherFullName')}
              value={form.fatherFullName}
              onChange={(e) => update('fatherFullName', e.target.value)}
            />
          )}
          {show('fatherPhone') && (
            <PhoneInput
              label="Otasi raqami"
              required={req('fatherPhone')}
              value={form.fatherPhone}
              onChange={(phone) => update('fatherPhone', phone)}
            />
          )}
        </Pair>
        <Pair>
          {show('motherFullName') && (
            <Input
              label="Onasi F.I.SH"
              required={req('motherFullName')}
              value={form.motherFullName}
              onChange={(e) => update('motherFullName', e.target.value)}
            />
          )}
          {show('motherPhone') && (
            <PhoneInput
              label="Onasi raqami"
              required={req('motherPhone')}
              value={form.motherPhone}
              onChange={(phone) => update('motherPhone', phone)}
            />
          )}
        </Pair>
        <Pair>
          {show('source') && (
            <Select
              label="Manba"
              required={req('source')}
              value={form.source ?? ''}
              onChange={(e) => update('source', e.target.value)}
            >
              <option value="">— tanlanmagan —</option>
              {form.source && !sourceOptions.includes(form.source) && (
                <option key={form.source} value={form.source}>
                  {form.source}
                </option>
              )}
              {sourceOptions.map((s) => (
                <option key={s} value={s}>
                  {s}
                </option>
              ))}
            </Select>
          )}
          {/* Qiziqqan fani = markazdagi KURSLAR ro'yxati (yangi kurs yaratilsa shu yerda chiqadi).
              Eski/landing lidlarida ro'yxatda yo'q matn bo'lsa — u ham variant sifatida saqlanadi. */}
          {show('interestSubject') && (
            <Select
              label="Qiziqqan fani (kurs)"
              required={req('interestSubject')}
              value={form.interestSubject ?? ''}
              onChange={(e) => update('interestSubject', e.target.value)}
            >
              <option value="">— tanlanmagan —</option>
              {form.interestSubject && !courseOptions.includes(form.interestSubject) && (
                <option key={form.interestSubject} value={form.interestSubject}>
                  {form.interestSubject}
                </option>
              )}
              {courseOptions.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </Select>
          )}
        </Pair>
        {/* Tashqi maktab (lid qayerda o'qiydi) — o'quvchiga aylantirilganda ko'chadi */}
        <Pair>
          {show('districtId') && (
            <Select
              label="Tuman"
              required={req('districtId')}
              value={form.districtId ?? ''}
              onChange={(e) => {
                // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi.
                setForm((f) => ({ ...f, districtId: e.target.value, schoolId: '' }))
              }}
            >
              <option value="">— tanlanmagan —</option>
              {districts.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </Select>
          )}
          {show('schoolId') && (
            <Select
              label="Maktab"
              required={req('schoolId')}
              value={form.schoolId ?? ''}
              disabled={!form.districtId}
              onChange={(e) => update('schoolId', e.target.value)}
            >
              <option value="">
                {form.districtId ? '— tanlanmagan —' : '— avval tumanni tanlang —'}
              </option>
              {(districts.find((d) => d.id === form.districtId)?.schools ?? []).map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </Select>
          )}
        </Pair>
        {show('note') && (
          <Textarea
            label="Izoh"
            required={req('note')}
            rows={3}
            value={form.note}
            onChange={(e) => update('note', e.target.value)}
          />
        )}
        {/* QO'SHIMCHA SAVOLLAR — markaz o'zi qo'shgani, standart maydonlardan KEYIN va
            sozlamadagi TARTIBDA (server `order` bo'yicha saralab beradi). */}
        {fields.length > 0 && (
          <div className="space-y-4 border-t border-slate-100 pt-4">
            {fields.map((f) => (
              <CustomAnswer
                key={f.id}
                field={f}
                value={answers[f.id] ?? []}
                onSet={(vals) => setAnswer(f.id, vals)}
                onToggle={(val) => toggleAnswer(f.id, val)}
              />
            ))}
          </div>
        )}
      </form>
    </Modal>
  )
}
