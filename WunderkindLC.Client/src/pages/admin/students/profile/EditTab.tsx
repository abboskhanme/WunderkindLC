import { useEffect, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import type { District, Student } from '@/types'
import type { PhoneMatch, StudentPayload } from '@/api/services/students'
import { archiveStudent, updateStudent } from '@/api/services/students'
import { getDistricts } from '@/api/services/districts'
import { PhoneInput } from '@/components/ui/PhoneInput'
import { ReasonPromptModal } from '@/components/ui/ReasonPromptModal'
import { genderOptions } from '@/config/constants'
import { apiErrorMessage } from '@/lib/utils'
import { readBackState } from '@/lib/nav'
import { PhoneDupeModal } from '../PhoneDupeModal'
import { findPhoneDupes, joinName, payloadFromStudent } from '../studentFormModel'
import { FilledPhoneWrap, FormButton, FormField, ProfileSection, SelectField, TextField } from './shared'

/**
 * «Tahrirlash» — edutizimdagidek profilning O'ZIDA 3 ustunli forma (oyna emas).
 *
 * Saqlash AYNAN o'sha yo'ldan: `PUT /students/{id}` + telefon dublikati tekshiruvi
 * (`studentFormModel` — `StudentFormModal` bilan umumiy). Guruh a'zoligi bu yerdan
 * O'ZGARTIRILMAYDI («Guruh» tabi), chegirma ham («Chegirma» tabi), parol — «Parol o'rnatish».
 *
 * Ruxsat: ko'rish — sahifaning o'zi (`students.list`), saqlash — `students.list:edit`,
 * «O'chirish» (arxivga ko'chirish) — `students.list:delete` (o'quvchilar ro'yxatidagi bilan bir xil).
 */
export function EditTab({
  student,
  canEdit,
  canArchive,
  onSaved,
}: {
  student: Student
  canEdit: boolean
  canArchive: boolean
  onSaved: () => void
}) {
  const navigate = useNavigate()
  const { backTo } = readBackState(useLocation().state, { backTo: '/admin/students', backLabel: '' })
  const [form, setForm] = useState<StudentPayload>(() => payloadFromStudent(student))
  const [districts, setDistricts] = useState<District[]>([])
  const [checking, setChecking] = useState(false)
  const [saving, setSaving] = useState(false)
  const [savedMsg, setSavedMsg] = useState('')
  /** Topilgan telefon dublikatlari (bo'lsa — tasdiq oynasi ochiladi). */
  const [dupes, setDupes] = useState<PhoneMatch[]>([])
  const [pending, setPending] = useState<StudentPayload | null>(null)
  const [archiveOpen, setArchiveOpen] = useState(false)

  useEffect(() => {
    getDistricts()
      .then(setDistricts)
      .catch(() => {
        /* tarmoq/mok — tuman ro'yxati bo'sh qoladi */
      })
  }, [])

  const update = <K extends keyof StudentPayload>(key: K, value: StudentPayload[K]) => {
    setSavedMsg('')
    setForm((f) => ({ ...f, [key]: value }))
  }

  const save = async (payload: StudentPayload) => {
    setSaving(true)
    try {
      await updateStudent(student.id, payload)
      setSavedMsg('Saqlandi')
      onSaved()
    } catch (e) {
      alert(apiErrorMessage(e, "Saqlab bo'lmadi"))
    } finally {
      setSaving(false)
    }
  }

  const submit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!canEdit) return
    const last = (form.lastName ?? '').trim()
    const first = (form.firstName ?? '').trim()
    const middle = (form.middleName ?? '').trim()
    if (!last && !first && !middle) return
    // ⚠️ RASM formadan emas, JORIY yozuvdan: avatar orqali almashtirilgan rasm eski qiymat bilan
    // ustidan yozilib ketmasin (rasm alohida endpoint bilan saqlanadi).
    const payload: StudentPayload = {
      ...form,
      fullName: joinName(last, first, middle),
      birthCertificateUrl: student.birthCertificateUrl ?? null,
    }
    setChecking(true)
    try {
      const matches = await findPhoneDupes(payload, student.id)
      if (matches.length > 0) {
        setPending(payload)
        setDupes(matches)
        return // tasdiq oynasida "Baribir saqlash" kutiladi
      }
    } catch {
      // Tekshiruv ishlamasa (tarmoq/mok) — saqlashni bloklamaymiz.
    } finally {
      setChecking(false)
    }
    await save(payload)
  }

  const doArchive = (reasonId?: string) => {
    archiveStudent(student.id, undefined, reasonId)
      .then(() => {
        setArchiveOpen(false)
        navigate('/admin/students')
      })
      .catch((e) => alert(apiErrorMessage(e, 'Arxivlashda xatolik')))
  }

  const schools = districts.find((d) => d.id === form.districtId)?.schools ?? []

  return (
    <ProfileSection>
      <p className="mb-3 text-[13px] text-[#333]">
        <span className="text-[#e34a29]">*</span> Zarurligini bildiradi
      </p>
      <form onSubmit={submit}>
        <fieldset disabled={!canEdit || saving} className="grid grid-cols-1 gap-x-4 gap-y-3 md:grid-cols-2 xl:grid-cols-3">
          <FormField label="Ism" required>
            <TextField required value={form.firstName ?? ''} onChange={(e) => update('firstName', e.target.value)} />
          </FormField>
          <FormField label="Familiya" required>
            <TextField required value={form.lastName ?? ''} onChange={(e) => update('lastName', e.target.value)} />
          </FormField>
          <FormField label="Telefon raqam">
            <FilledPhoneWrap>
              <PhoneInput value={form.phone ?? ''} onChange={(v) => update('phone', v)} />
            </FilledPhoneWrap>
          </FormField>

          <FormField label="Sharifi (otasining ismi)">
            <TextField value={form.middleName ?? ''} onChange={(e) => update('middleName', e.target.value)} />
          </FormField>
          <FormField label="Jinsi">
            <SelectField
              value={form.gender}
              onChange={(e) => update('gender', e.target.value as StudentPayload['gender'])}
            >
              {genderOptions.map((g) => (
                <option key={g.value} value={g.value}>
                  {g.label}
                </option>
              ))}
            </SelectField>
          </FormField>
          <FormField label="Tug'ilgan sanasi">
            <TextField type="date" value={form.birthDate} onChange={(e) => update('birthDate', e.target.value)} />
          </FormField>

          <FormField label="Otasining ismi">
            <TextField value={form.fatherFullName ?? ''} onChange={(e) => update('fatherFullName', e.target.value)} />
          </FormField>
          <FormField label="Telefon raqam">
            <FilledPhoneWrap>
              <PhoneInput value={form.fatherPhone ?? ''} onChange={(v) => update('fatherPhone', v)} />
            </FilledPhoneWrap>
          </FormField>
          <FormField label="Markazga kelgan sana">
            <TextField
              type="date"
              value={form.enrollmentDate}
              onChange={(e) => update('enrollmentDate', e.target.value)}
            />
          </FormField>

          <FormField label="Onasining ismi">
            <TextField value={form.motherFullName ?? ''} onChange={(e) => update('motherFullName', e.target.value)} />
          </FormField>
          <FormField label="Telefon raqam">
            <FilledPhoneWrap>
              <PhoneInput value={form.motherPhone ?? ''} onChange={(v) => update('motherPhone', v)} />
            </FilledPhoneWrap>
          </FormField>
          <FormField label="Tuman">
            <SelectField
              value={form.districtId ?? ''}
              // Tuman o'zgarsa, oldingi maktab tanlovi tozalanadi.
              onChange={(e) => {
                setSavedMsg('')
                setForm((f) => ({ ...f, districtId: e.target.value, schoolId: '' }))
              }}
            >
              <option value="">Tanlang</option>
              {districts.map((d) => (
                <option key={d.id} value={d.id}>
                  {d.name}
                </option>
              ))}
            </SelectField>
          </FormField>

          <FormField label="Uy adresi">
            <TextField value={form.address} onChange={(e) => update('address', e.target.value)} />
          </FormField>
          <FormField label="O'qish joyi (maktab)">
            <SelectField
              value={form.schoolId ?? ''}
              disabled={!canEdit || !form.districtId}
              onChange={(e) => update('schoolId', e.target.value)}
            >
              <option value="">{form.districtId ? 'Tanlang' : 'Avval tumanni tanlang'}</option>
              {schools.map((s) => (
                <option key={s.id} value={s.id}>
                  {s.name}
                </option>
              ))}
            </SelectField>
          </FormField>
        </fieldset>

        {canEdit && (
          <div className="mt-5 flex flex-wrap items-center justify-end gap-2 border-t border-[#eef0f2] pt-4">
            {savedMsg && <span className="mr-auto text-[13px] font-medium text-emerald-600">{savedMsg}</span>}
            {canArchive && !student.isArchived && (
              <FormButton tone="danger" onClick={() => setArchiveOpen(true)}>
                O'chirish
              </FormButton>
            )}
            <FormButton tone="outline" onClick={() => navigate(backTo)}>
              Orqaga
            </FormButton>
            <FormButton type="submit" disabled={checking || saving}>
              {checking ? 'Tekshirilmoqda...' : saving ? 'Saqlanmoqda...' : 'Saqlash'}
            </FormButton>
          </div>
        )}
      </form>

      <PhoneDupeModal
        dupes={dupes}
        onCancel={() => {
          setDupes([])
          setPending(null)
        }}
        onConfirm={() => {
          const p = pending
          setDupes([])
          setPending(null)
          if (p) void save(p)
        }}
      />

      {/* «O'chirish» — o'quvchilar ro'yxatidagi «Arxivga ko'chirish» bilan AYNAN bir xil amal
          (sabab so'raladi, tarix saqlanadi, login bloklanadi). Butunlay o'chirish — faqat arxivdan. */}
      <ReasonPromptModal
        open={archiveOpen}
        category="archive_student"
        title="O'quvchini arxivga ko'chirish"
        message={`"${student.fullName}" o'quvchini arxivga ko'chirasiz. Tarixiy ma'lumotlar (jurnal, davomat, to'lovlar) saqlanadi, lekin faol ro'yxatdan yashirinadi, oylik to'lov hisoblanmaydi va login bloklanadi.`}
        confirmLabel="Arxivga ko'chirish"
        tone="red"
        onConfirm={doArchive}
        onClose={() => setArchiveOpen(false)}
      />
    </ProfileSection>
  )
}
