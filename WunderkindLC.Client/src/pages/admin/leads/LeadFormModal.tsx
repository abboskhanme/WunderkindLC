import { Children, type ReactNode } from 'react'
import type { Lead } from '@/types'
import { Modal } from '@/components/ui/Modal'
import { Button } from '@/components/ui/Button'
import { Input, Select, Textarea } from '@/components/ui/Input'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { genderOptions } from '@/config/constants'
import type { LeadAnswersPayload } from '@/api/services/leads'
import { LeadCustomAnswer } from './LeadCustomAnswer'
import { leadErrorText, useLeadEntryForm, type LeadFormValues } from './useLeadEntryForm'

export type { LeadFormValues } from './useLeadEntryForm'

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

export function LeadFormModal({ open, onClose, onSubmit, initial }: Props) {
  // Holat, sozlama («Lid kiritish formasi») va tekshiruv — `useLeadEntryForm` da: lid sahifasining
  // chap paneli ham AYNAN shundan foydalanadi (qoidalar ikki joyda ayri ketmasin).
  const {
    form, setForm, update, sourceOptions, districts, courseOptions, fields, answers,
    setAnswer, toggleAnswer, show, req, validate, saving, setSaving, error, setError,
  } = useLeadEntryForm(open, initial)

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
      setError(leadErrorText(err))
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
              <LeadCustomAnswer
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
