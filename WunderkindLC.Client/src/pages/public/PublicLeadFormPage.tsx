import { useEffect, useState } from 'react'
import { useParams, useSearchParams } from 'react-router-dom'
import {
  GraduationCap, Loader2, PartyPopper, AlertCircle, Send, Camera, ThumbsUp, Play, Globe,
} from 'lucide-react'
import {
  getPublicLeadForm, submitPublicLeadForm,
  type PublicLeadForm, type PublicLeadFormField,
} from '@/api/services/publicLeadForm'
import { getPublicBrand, type PublicBrand } from '@/api/services/settings'
import { apiErrorMessage, cn } from '@/lib/utils'
import { inputCls, cardPadX, primaryBtnCls, Field } from './publicFormUi'
import posthog from '@/lib/posthog'

type Phase = 'loading' | 'notfound' | 'form' | 'done'

/**
 * Ikonka + yorliq xaritasi — server yuboradigan `kind` kalitlari (`LeadFormService.SocialsOf`).
 * ⚠️ Ikonkalar UMUMIY (lucide'da brend ikonkalari YO'Q), shu sabab yonida NOM ham yoziladi —
 * mijoz "bu qaysi tarmoq?" deb o'ylab qolmasin.
 */
const socialMeta: Record<string, { icon: typeof Globe; label: string }> = {
  instagram: { icon: Camera, label: 'Instagram' },
  telegram: { icon: Send, label: 'Telegram' },
  facebook: { icon: ThumbsUp, label: 'Facebook' },
  youtube: { icon: Play, label: 'YouTube' },
  website: { icon: Globe, label: 'Sayt' },
}

/**
 * OMMAVIY LID FORMASI (`/forma/{slug}`) — ijtimoiy tarmoqdagi havoladan kelgan mijoz uchun.
 * To'ldirilgan ariza CRM'da lid bo'lib tushadi; manba formaning O'ZIDA belgilangan, ya'ni
 * mijozdan "qayerdan eshitdingiz?" deb so'ralmaydi.
 *
 * <p>Bitta ekran — ko'p qadamli emas: ochiq forma qancha qisqa bo'lsa, shuncha ko'p to'ldiriladi.</p>
 */
export function PublicLeadFormPage() {
  const { slug = '' } = useParams()
  const [params] = useSearchParams()
  // Sub-kanal belgisi: bitta forma havolasi bir necha joyga qo'yilganda ("?ref=story").
  const refTag = params.get('ref') ?? ''

  const [phase, setPhase] = useState<Phase>('loading')
  const [form, setForm] = useState<PublicLeadForm | null>(null)
  const [brand, setBrand] = useState<PublicBrand>({ name: '', logoUrl: '', phone: '' })

  const [fullName, setFullName] = useState('')
  const [phone, setPhone] = useState('')
  const [parentPhone, setParentPhone] = useState('')
  const [age, setAge] = useState('')
  const [course, setCourse] = useState('')
  const [answers, setAnswers] = useState<Record<string, string[]>>({})
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const [doneMessage, setDoneMessage] = useState('')

  useEffect(() => {
    getPublicLeadForm(slug)
      .then((f) => {
        setForm(f)
        setPhase('form')
      })
      .catch(() => setPhase('notfound'))
  }, [slug])

  useEffect(() => {
    getPublicBrand()
      .then(setBrand)
      .catch(() => {})
  }, [])

  const setAnswer = (id: string, vals: string[]) => setAnswers((a) => ({ ...a, [id]: vals }))
  const toggleAnswer = (id: string, val: string) =>
    setAnswers((a) => {
      const cur = a[id] ?? []
      return { ...a, [id]: cur.includes(val) ? cur.filter((x) => x !== val) : [...cur, val] }
    })

  /**
   * Yuborish. `<form>` ichida bo'lgani uchun telefon klaviaturasidagi "Yuborish"/Enter ham shu
   * yerga keladi — shuning uchun brauzerning o'z yuborishini to'xtatamiz.
   */
  const submit = async (e?: React.FormEvent) => {
    e?.preventDefault()
    if (!form || submitting) return
    if (!fullName.trim()) return setError('Ism-familiyangizni kiriting')
    if (phone.replace(/\D/g, '').length < 9)
      return setError("Telefon raqamini to'liq kiriting (kamida 9 ta raqam)")
    // Majburiy qo'shimcha savollar — serverda ham tekshiriladi, bu yerda faqat tez javob uchun.
    const missing = form.fields.find((f) => f.required && (answers[f.id]?.length ?? 0) === 0)
    if (missing) return setError(`«${missing.label}» — bu maydonni to'ldiring`)

    setError('')
    setSubmitting(true)
    try {
      const r = await submitPublicLeadForm(slug, {
        fullName: fullName.trim(),
        phone: phone.trim(),
        parentPhone: parentPhone.trim(),
        age: Number(age) || 0,
        course,
        answers,
        ref: refTag,
      })
      setDoneMessage(r.message)
      posthog.capture('public_lead_form_submitted')
      setPhase('done')
    } catch (err) {
      setError(apiErrorMessage(err, "Xatolik yuz berdi. Qayta urinib ko'ring."))
    } finally {
      setSubmitting(false)
    }
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-brand-50 via-white to-slate-50 px-4 py-8 sm:py-14">
      <div className="mx-auto w-full max-w-xl">
        {/* Brand */}
        <div className="mb-6 flex items-center justify-center gap-2.5">
          {brand.logoUrl ? (
            <img src={brand.logoUrl} alt="Logo" className="h-10 w-10 rounded-xl object-contain shadow-lg" />
          ) : (
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-br from-brand-500 to-brand-700 text-white shadow-lg">
              <GraduationCap className="h-6 w-6" />
            </div>
          )}
          <span className="text-lg font-bold tracking-tight text-slate-800">
            {brand.name || 'WunderkindLC'}
          </span>
        </div>

        <div className="overflow-hidden rounded-2xl border border-slate-100 bg-white shadow-xl">
          {phase === 'loading' && (
            <div className="flex flex-col items-center gap-3 py-20 text-slate-400">
              <Loader2 className="h-7 w-7 animate-spin" />
              <p className="text-sm">Yuklanmoqda...</p>
            </div>
          )}

          {phase === 'notfound' && (
            <div className={cn('flex flex-col items-center gap-3 py-20 text-center', cardPadX)}>
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-red-50 text-red-500">
                <AlertCircle className="h-7 w-7" />
              </div>
              <h1 className="text-lg font-bold text-slate-800">Forma topilmadi</h1>
              <p className="text-sm text-slate-500">
                Bu havola noto'g'ri yoki forma faol emas. Iltimos, markaz bilan bog'laning.
              </p>
            </div>
          )}

          {phase === 'form' && form && (
            <div>
              <div className={cn('bg-gradient-to-br from-brand-500 to-brand-700 py-6 text-white sm:py-7', cardPadX)}>
                <h1 className="break-words text-xl font-bold">{form.title}</h1>
                {form.courseName && !form.askCourse && (
                  <p className="mt-1 text-sm text-white/80">{form.courseName}</p>
                )}
              </div>
              {/* Haqiqiy `<form>`: telefon klaviaturasida "Yuborish" tugmasi chiqadi, brauzerning
                  avto-to'ldirishi (ism/telefon) ishlaydi va Enter bilan yuborsa ham bo'ladi. */}
              <form onSubmit={submit} className={cn('space-y-4 py-6', cardPadX)}>
                {form.intro && <p className="text-sm leading-relaxed text-slate-600">{form.intro}</p>}

                <Field label="Ism-familiya *">
                  <input
                    value={fullName}
                    onChange={(e) => setFullName(e.target.value)}
                    placeholder="Masalan: Aliyev Ali"
                    name="name"
                    autoComplete="name"
                    autoCapitalize="words"
                    enterKeyHint="next"
                    className={inputCls}
                  />
                </Field>
                <Field label="Telefon raqam *">
                  <input
                    value={phone}
                    onChange={(e) => setPhone(e.target.value)}
                    placeholder="+998 90 123 45 67"
                    type="tel"
                    name="tel"
                    autoComplete="tel"
                    inputMode="tel"
                    enterKeyHint="next"
                    className={inputCls}
                  />
                </Field>
                {form.askParentPhone && (
                  <Field label="Ota-onaning telefoni">
                    <input
                      value={parentPhone}
                      onChange={(e) => setParentPhone(e.target.value)}
                      placeholder="+998 90 123 45 67"
                      type="tel"
                      inputMode="tel"
                      /* ⚠️ avtoto'ldirish ATAYIN yo'q — bu BOSHQA odamning raqami, brauzer
                         mijozning o'z raqamini taklif qilib, xato ma'lumot yozdirardi. */
                      autoComplete="off"
                      enterKeyHint="next"
                      className={inputCls}
                    />
                  </Field>
                )}
                {form.askAge && (
                  <Field label="Yoshingiz">
                    <input
                      value={age}
                      onChange={(e) => setAge(e.target.value.replace(/\D/g, ''))}
                      placeholder="18"
                      inputMode="numeric"
                      pattern="[0-9]*"
                      maxLength={3}
                      enterKeyHint="next"
                      className={inputCls}
                    />
                  </Field>
                )}
                {form.askCourse && (
                  <Field label="Qaysi kurs qiziqtiradi?">
                    <select value={course} onChange={(e) => setCourse(e.target.value)} className={inputCls}>
                      <option value="">— Tanlang —</option>
                      {form.courses.map((c) => (
                        <option key={c} value={c}>
                          {c}
                        </option>
                      ))}
                    </select>
                  </Field>
                )}

                {form.fields.map((f) => (
                  <CustomField
                    key={f.id}
                    field={f}
                    value={answers[f.id] ?? []}
                    onSet={(vals) => setAnswer(f.id, vals)}
                    onToggle={(val) => toggleAnswer(f.id, val)}
                  />
                ))}

                {error && <p className="text-sm font-medium text-red-600">{error}</p>}

                <button
                  type="submit"
                  disabled={submitting}
                  className={cn(primaryBtnCls, 'bg-brand-600 shadow-brand-600/20 hover:bg-brand-700')}
                >
                  {submitting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
                  {form.buttonText || 'Yuborish'}
                </button>
              </form>
            </div>
          )}

          {phase === 'done' && (
            <div className={cn('flex flex-col items-center gap-4 py-12 text-center sm:py-14', cardPadX)}>
              <div className="flex h-16 w-16 items-center justify-center rounded-2xl bg-emerald-50 text-emerald-500">
                <PartyPopper className="h-8 w-8" />
              </div>
              <h1 className="text-xl font-bold text-slate-800">Rahmat!</h1>
              <p className="max-w-sm text-sm leading-relaxed text-slate-500">{doneMessage}</p>
              {/* Ijtimoiy tarmoqlar — menejer qo'ng'iroq qilgunicha aloqa uzilmasin. */}
              {(form?.socials?.length ?? 0) > 0 && (
                <div className="flex flex-col items-center gap-2">
                  <span className="text-xs text-slate-400">Bizni kuzatib boring</span>
                  <div className="flex flex-wrap items-center justify-center gap-2">
                    {form!.socials.map((s) => {
                      const meta = socialMeta[s.kind] ?? { icon: Globe, label: s.kind }
                      const Icon = meta.icon
                      return (
                        <a
                          key={s.kind}
                          href={s.url}
                          target="_blank"
                          rel="noreferrer noopener"
                          className="inline-flex items-center gap-1.5 rounded-xl border border-slate-200 px-3.5 py-2.5 text-xs font-medium text-slate-600 transition-colors hover:border-brand-300 hover:text-brand-600"
                        >
                          <Icon className="h-4 w-4" /> {meta.label}
                        </a>
                      )
                    })}
                  </div>
                </div>
              )}
              {brand.phone && (
                <a
                  href={`tel:${brand.phone}`}
                  className="text-sm font-semibold text-brand-600 hover:text-brand-700"
                >
                  {brand.phone}
                </a>
              )}
            </div>
          )}
        </div>

        <p className="mt-6 text-center text-xs text-slate-400">
          {brand.name || 'WunderkindLC'} · O'quv markazi
        </p>
      </div>
    </div>
  )
}

/** Qo'shimcha savol — turiga qarab matn / raqam / select / radio / checkbox. */
function CustomField({
  field, value, onSet, onToggle,
}: {
  field: PublicLeadFormField
  value: string[]
  onSet: (vals: string[]) => void
  onToggle: (val: string) => void
}) {
  const label = field.label + (field.required ? ' *' : '')
  const single = value[0] ?? ''

  if (field.kind === 'textarea')
    return (
      <Field label={label}>
        <textarea
          rows={3}
          value={single}
          onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
          placeholder={field.placeholder}
          className={inputCls + ' resize-y'}
        />
      </Field>
    )

  if (field.kind === 'number')
    return (
      <Field label={label}>
        <input
          value={single}
          onChange={(e) => {
            const v = e.target.value.replace(/\D/g, '')
            onSet(v ? [v] : [])
          }}
          placeholder={field.placeholder}
          inputMode="numeric"
          className={inputCls}
        />
      </Field>
    )

  if (field.kind === 'select')
    return (
      <Field label={label}>
        <select
          value={single}
          onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
          className={inputCls}
        >
          <option value="">— Tanlang —</option>
          {field.options.map((o) => (
            <option key={o} value={o}>
              {o}
            </option>
          ))}
        </select>
      </Field>
    )

  if (field.kind === 'radio' || field.kind === 'checkbox') {
    const multiple = field.kind === 'checkbox'
    return (
      <Field label={label}>
        <div className="space-y-2">
          {field.options.map((o) => {
            const selected = value.includes(o)
            return (
              <button
                key={o}
                type="button"
                onClick={() => (multiple ? onToggle(o) : onSet([o]))}
                className={
                  // py-3 (≈44px) — barmoq uchun eng kichik qulay tegish maydoni.
                  'flex w-full items-center gap-3 rounded-xl border-2 px-4 py-3 text-left text-sm transition-colors ' +
                  (selected
                    ? 'border-brand-500 bg-brand-50 text-brand-800'
                    : 'border-slate-200 text-slate-700 hover:border-brand-300 hover:bg-slate-50')
                }
              >
                <span
                  className={
                    'h-5 w-5 shrink-0 border-2 ' +
                    (multiple ? 'rounded-md ' : 'rounded-full ') +
                    (selected ? 'border-brand-500 bg-brand-500' : 'border-slate-300')
                  }
                />
                <span className="flex-1">{o}</span>
              </button>
            )
          })}
        </div>
      </Field>
    )
  }

  return (
    <Field label={label}>
      <input
        value={single}
        onChange={(e) => onSet(e.target.value ? [e.target.value] : [])}
        placeholder={field.placeholder}
        className={inputCls}
      />
    </Field>
  )
}

