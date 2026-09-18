import { useEffect, useState } from 'react'
import type { District, Lead } from '@/types'
import { leadSourceOptions } from '@/config/constants'
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

/**
 * LID FORMASI HOLATI — «Yangi lid» oynasi (`LeadFormModal`) ham, lid SAHIFASIDAGI chap panel
 * (`LeadDetailPage`, edutizimdagi "Buyurtma ma'lumotlari" + "Saqlash") ham AYNAN shu hookdan
 * foydalanadi. Ikki joyda ayri yozilsa «Lid kiritish formasi» qoidalari (qaysi maydon
 * ko'rinadi/majburiy, qo'shimcha savollar, yashirin maydon qiymati saqlanishi —
 * `.claude/rules/lead-entry-form.md` §7–8) bir joyda o'zgarib, ikkinchisida eskicha qolardi.
 */

export type LeadFormValues = Omit<Lead, 'id' | 'stage'>

export const emptyLeadForm: LeadFormValues = {
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
export const standardLabels: Record<string, string> = {
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
export function leadErrorText(err: unknown): string {
  const data = (err as { response?: { data?: unknown } })?.response?.data
  const msg =
    typeof data === 'string' ? data : (data as { message?: string } | undefined)?.message
  return typeof msg === 'string' && msg.trim()
    ? msg
    : "Saqlab bo'lmadi. Qayta urinib ko'ring."
}

/** Lid obyektidan forma qiymatlari — HAMMA maydon (yashirilgani ham, §7.1). */
function valuesOf(initial: Lead): LeadFormValues {
  return {
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
}

/**
 * @param open   forma ko'rinayaptimi (modal ochiq / sahifa yuklangan) — yopiq bo'lsa hech narsa yuklanmaydi
 * @param initial tahrirlanayotgan lid, yangi lid uchun `null`
 */
export function useLeadEntryForm(open: boolean, initial: Lead | null | undefined) {
  const [form, setForm] = useState<LeadFormValues>(emptyLeadForm)
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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- forma ochilganda xato va formani initial bilan sinxronlash (maqsadli)
    setError(null)
    // ⚠️ Forma HAR DOIM `initial` dan TO'LIQ to'ldiriladi — sozlamada yashirilgan maydon ham.
    // Uning qiymati keyin serverga o'sha holicha yuboriladi: menejer KO'RMAGAN ma'lumot
    // (masalan onasi raqami) tahrirlash paytida jimgina o'chib ketmasin.
    setForm(initial ? valuesOf(initial) : emptyLeadForm)
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

  return {
    form,
    setForm,
    update,
    sourceOptions,
    districts,
    courseOptions,
    fields,
    answers,
    setAnswer,
    toggleAnswer,
    show,
    req,
    validate,
    saving,
    setSaving,
    error,
    setError,
  }
}
